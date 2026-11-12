using System.Collections.Concurrent;
using System.Diagnostics;
using Game.Net;
using Game.Rules;

namespace Game.Server;

/// <summary>A way to send packets to one client. The WebSocket version lives in <see cref="WebSocketConnection"/>.</summary>
public interface IConnection
{
    void Send(byte[] packet);

    void Close();
}

/// <summary>One player's place in a channel, kept while they are away so they can come back.</summary>
public sealed class Session
{
    public Session(int playerId, byte[] token, ChannelRunner runner, PlayerIdentity? identity = null)
    {
        PlayerId = playerId;
        Token = token;
        Runner = runner;
        Identity = identity;
    }

    public int PlayerId { get; }
    public byte[] Token { get; }
    public ChannelRunner Runner { get; }

    /// <summary>The saved character playing; null only where login is not required, as in server tests.</summary>
    public PlayerIdentity? Identity { get; }
}

/// <summary>Which saved character a connection plays, found from its login token.</summary>
public sealed record PlayerIdentity(long CharacterId, string Name);

/// <summary>
/// Runs one <see cref="Room"/>. Connection threads only queue commands; everything else
/// happens inside <see cref="Tick"/>, so the channel never needs a lock.
/// </summary>
public sealed class ChannelRunner
{
    private readonly ServerConfig server;
    private readonly ConcurrentQueue<Action> inbox = new();
    private readonly Dictionary<int, PlayerSlot> slots = new();
    private readonly Dictionary<int, RoomIntent> intents = new();
    private readonly List<NetEvent> pendingEvents = new();
    private readonly GameConfig config;
    private readonly int holdTicks;
    private int currentTick;

    public ChannelRunner(int id, GameConfig config, ServerConfig server)
    {
        this.server = server;
        this.config = config;
        Room = new Room(id, config, server);
        holdTicks = Ticks.FromSeconds(server.ReconnectHoldSec);
    }

    public Room Room { get; }
    public int Id => Room.Id;

    /// <summary>Safe to read from any thread.</summary>
    public uint CurrentTick => (uint)Volatile.Read(ref currentTick);

    /// <summary>Called when a player's place is finally given up, from the tick thread.</summary>
    public event Action<int>? PlayerRemoved;

    public void Join(Session session, IConnection connection)
    {
        inbox.Enqueue(() => AddPlayer(session, connection));
    }

    public void Resume(Session session, IConnection connection)
    {
        inbox.Enqueue(() => ResumePlayer(session, connection));
    }

    public void Input(int playerId, IConnection connection, InputMessage input)
    {
        inbox.Enqueue(() => QueueInput(playerId, connection, input));
    }

    /// <summary>Puts on an item if the catalog has it; anything else is ignored. Not saved until T045.</summary>
    public void Wear(int playerId, IConnection connection, WearMessage wear)
    {
        inbox.Enqueue(() =>
        {
            if (slots.TryGetValue(playerId, out PlayerSlot? slot) && slot.Connection == connection
                && GearRules.TryWear(config, slot.Appearance, wear.Item, out Appearance next))
            {
                slot.Appearance = next;
            }
        });
    }

    public void Disconnected(int playerId, IConnection connection)
    {
        inbox.Enqueue(() => MarkAway(playerId, connection));
    }

    public void Leave(int playerId, IConnection connection)
    {
        inbox.Enqueue(() =>
        {
            if (slots.TryGetValue(playerId, out PlayerSlot? slot) && slot.Connection == connection)
            {
                Remove(playerId);
            }
        });
    }

    /// <summary>Handles queued commands, steps the channel once and sends snapshots when due.</summary>
    public void Tick()
    {
        while (inbox.TryDequeue(out Action? command))
        {
            command();
        }

        intents.Clear();

        foreach (PlayerSlot slot in slots.Values)
        {
            intents[slot.PlayerId] = slot.NextIntent();
        }

        Room.Step(intents);
        CollectEvents();
        Volatile.Write(ref currentTick, Room.Tick);
        ExpireAwayPlayers();

        if (Room.Tick % server.SnapshotEveryTicks == 0)
        {
            SendSnapshots();
        }
    }

    /// <summary>Ticks 30 times a second on its own thread until cancelled.</summary>
    public Thread Start(CancellationToken stop)
    {
        var thread = new Thread(() => Run(stop)) { IsBackground = true, Name = $"channel-{Id}" };
        thread.Start();
        return thread;
    }

    private void Run(CancellationToken stop)
    {
        const int MaxCatchUpTicks = 5;
        double tickMs = 1000d / Ticks.PerSecond;
        var clock = Stopwatch.StartNew();
        double nextMs = tickMs;

        while (!stop.IsCancellationRequested)
        {
            double wait = nextMs - clock.Elapsed.TotalMilliseconds;

            if (wait > 0)
            {
                Thread.Sleep(wait > 2 ? (int)wait - 1 : 0);
                continue;
            }

            Tick();
            nextMs += tickMs;

            if (clock.Elapsed.TotalMilliseconds - nextMs > MaxCatchUpTicks * tickMs)
            {
                nextMs = clock.Elapsed.TotalMilliseconds + tickMs;
            }
        }
    }

    private void AddPlayer(Session session, IConnection connection)
    {
        RoomPlayer? player = Room.Join(session.PlayerId);

        if (player == null)
        {
            connection.Send(Packet.Encode(new RejectMessage(RejectReason.ServerFull)));
            connection.Close();
            PlayerRemoved?.Invoke(session.PlayerId);
            return;
        }

        var slot = new PlayerSlot(session, connection, server.MaxQueuedInputs) { Appearance = GearRules.Default(config) };
        slots[session.PlayerId] = slot;
        SendWelcome(slot, player, resumed: false);
    }

    private void ResumePlayer(Session session, IConnection connection)
    {
        if (!slots.TryGetValue(session.PlayerId, out PlayerSlot? slot))
        {
            connection.Close();
            return;
        }

        if (slot.Connection != null && slot.Connection != connection)
        {
            // Still connected: the same character came in somewhere else, which takes it over.
            slot.Connection.Send(Packet.Encode(new RejectMessage(RejectReason.LoggedInElsewhere)));
            slot.Connection.Close();
        }

        slot.Connection = connection;
        slot.AwayTicksLeft = 0;
        slot.ClearInputs();
        SendWelcome(slot, Room.Find(session.PlayerId)!, resumed: true);
    }

    private void SendWelcome(PlayerSlot slot, RoomPlayer player, bool resumed)
    {
        slot.Connection!.Send(Packet.Encode(new WelcomeMessage(
            slot.PlayerId,
            Id,
            (uint)Room.Tick,
            resumed,
            slot.Session.Token,
            slot.LastSequence,
            Room.TestRoom,
            player.Capture(),
            slot.Session.Identity?.Name ?? string.Empty)));
    }

    private void QueueInput(int playerId, IConnection connection, InputMessage input)
    {
        if (slots.TryGetValue(playerId, out PlayerSlot? slot) && slot.Connection == connection)
        {
            slot.Queue(input);
        }
    }

    private void MarkAway(int playerId, IConnection connection)
    {
        if (!slots.TryGetValue(playerId, out PlayerSlot? slot) || slot.Connection != connection)
        {
            return;
        }

        slot.Connection = null;
        slot.ClearInputs();
        slot.AwayTicksLeft = holdTicks;

        if (holdTicks == 0)
        {
            Remove(playerId);
        }
    }

    private void ExpireAwayPlayers()
    {
        List<int>? expired = null;

        foreach (PlayerSlot slot in slots.Values)
        {
            if (slot.Connection == null && --slot.AwayTicksLeft <= 0)
            {
                (expired ??= new List<int>()).Add(slot.PlayerId);
            }
        }

        if (expired != null)
        {
            foreach (int playerId in expired)
            {
                Remove(playerId);
            }
        }
    }

    private void Remove(int playerId)
    {
        if (slots.Remove(playerId, out PlayerSlot? slot))
        {
            slot.Connection?.Close();
            Room.Leave(playerId);
            PlayerRemoved?.Invoke(playerId);
        }
    }

    /// <summary>Keeps what happened since the last snapshot; a snapshot carries at most 255 events.</summary>
    private void CollectEvents()
    {
        foreach (RoomEvent item in Room.Events)
        {
            if (pendingEvents.Count < byte.MaxValue)
            {
                pendingEvents.Add(NetEvent.From(item, config.Boss));
            }
        }
    }

    private void SendSnapshots()
    {
        var everyone = new List<RemotePlayer>(Room.Count);
        var projectiles = new List<RemoteProjectile>();

        foreach (RoomPlayer player in Room.Players)
        {
            MovementState movement = player.Movement;
            CombatState combat = player.Combat;
            RemoteFlags flags = RemoteFlags.None;
            flags |= movement.Grounded ? RemoteFlags.Grounded : RemoteFlags.None;
            flags |= movement.IsDashing ? RemoteFlags.Dashing : RemoteFlags.None;
            flags |= slots[player.Id].Connection == null ? RemoteFlags.Away : RemoteFlags.None;
            flags |= player.IsDead ? RemoteFlags.Dead : RemoteFlags.None;
            CombatSnapshot cast = combat.Capture();
            everyone.Add(new RemotePlayer(
                player.Id,
                (float)movement.X,
                (float)movement.Y,
                (sbyte)movement.Facing,
                flags,
                cast.Casting,
                cast.CastTicks,
                cast.ComboStep,
                (sbyte)(cast.Direction == 0 ? movement.Facing : cast.Direction),
                slots[player.Id].Appearance));

            foreach (ProjectileSnapshot shot in cast.Projectiles)
            {
                if (projectiles.Count < byte.MaxValue)
                {
                    projectiles.Add(new RemoteProjectile(player.Id, shot.Slot, (float)shot.X, (float)shot.Y, (sbyte)shot.Direction));
                }
            }
        }

        BossSnapshot boss = Room.Boss.Capture();
        var events = pendingEvents.ToArray();
        pendingEvents.Clear();

        foreach (PlayerSlot slot in slots.Values)
        {
            if (slot.Connection == null)
            {
                continue;
            }

            List<RemotePlayer> others = everyone.FindAll(player => player.Id != slot.PlayerId);
            var snapshot = new SnapshotMessage(
                (uint)Room.Tick,
                slot.LastSequence,
                Room.Find(slot.PlayerId)!.Capture(),
                boss,
                Room.BossRespawnTicksLeft,
                others,
                projectiles,
                events,
                slot.Appearance);
            slot.Connection.Send(Packet.Encode(snapshot));
        }
    }

    private sealed class PlayerSlot
    {
        private readonly Queue<InputMessage> inputs = new();
        private readonly int maxQueued;
        private MovementIntent lastMove;

        public PlayerSlot(Session session, IConnection connection, int maxQueued)
        {
            Session = session;
            Connection = connection;
            this.maxQueued = maxQueued;
        }

        public Session Session { get; }
        public int PlayerId => Session.PlayerId;
        public IConnection? Connection { get; set; }
        public int AwayTicksLeft { get; set; }
        public uint LastSequence { get; private set; }

        /// <summary>What the player wears; kept while away, lost when the place is given up (saving is T045).</summary>
        public Appearance Appearance { get; set; }

        public void Queue(InputMessage input)
        {
            uint newest = inputs.Count > 0 ? inputs.Last().Sequence : LastSequence;

            if (!Predictor.IsAfter(input.Sequence, newest))
            {
                return;
            }

            inputs.Enqueue(input);

            while (inputs.Count > maxQueued)
            {
                LastSequence = inputs.Dequeue().Sequence;
            }
        }

        public void ClearInputs()
        {
            inputs.Clear();
            lastMove = default;
        }

        /// <summary>
        /// One queued input per tick. With none waiting, the stick is held where it was; no button
        /// is pressed again, so a late packet never repeats a dash, a skill or a potion.
        /// </summary>
        public RoomIntent NextIntent()
        {
            if (inputs.Count == 0)
            {
                return new RoomIntent(new MovementIntent(lastMove.MoveX, lastMove.MoveY, false), new CombatIntent(null));
            }

            InputMessage input = inputs.Dequeue();
            LastSequence = input.Sequence;
            RoomIntent intent = input.ToIntent();
            lastMove = intent.Move;
            return intent;
        }
    }
}
