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
    private readonly List<(NetEvent Event, int Map)> pendingEvents = new();
    private readonly GameConfig config;
    private readonly int holdTicks;
    private readonly Dictionary<int, LyingItem> ground = new();
    private readonly ConcurrentDictionary<int, MapSpot> positions = new();
    private int lastGroundId;
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

    /// <summary>Called just before a player's place is given up, with where they stood, so it can be saved.</summary>
    public event Action<Session, MapSpot>? PlayerLeaving;

    /// <param name="spawn">Where a saved character last stood, or null for the start map's spawn point.</param>
    public void Join(Session session, IConnection connection, MapSpot? spawn = null)
    {
        inbox.Enqueue(() => AddPlayer(session, connection, spawn));
    }

    public void Resume(Session session, IConnection connection)
    {
        inbox.Enqueue(() => ResumePlayer(session, connection));
    }

    public void Input(int playerId, IConnection connection, InputMessage input)
    {
        inbox.Enqueue(() => QueueInput(playerId, connection, input));
    }

    /// <summary>What a player now wears, worked out from the bag by the inventory service.</summary>
    public void SetAppearance(int playerId, Appearance appearance)
    {
        inbox.Enqueue(() =>
        {
            if (slots.TryGetValue(playerId, out PlayerSlot? slot))
            {
                slot.Appearance = appearance;
            }
        });
    }

    /// <summary>Puts an item that just left a player's bag on the ground at their feet.</summary>
    public void Drop(int playerId, int item, int quantity)
    {
        inbox.Enqueue(() =>
        {
            RoomPlayer? player = Room.Find(playerId);
            double x = player?.Movement.X ?? server.SpawnX;
            double y = player?.Movement.Y ?? 0d;
            int id = ++lastGroundId;
            ground[id] = new LyingItem(id, item, quantity, x, y, Room.Tick + Ticks.FromSeconds(config.Bag.GroundLifetimeSec), player?.Map ?? Room.StartMap, playerId);
        });
    }

    /// <summary>
    /// Reserves an item on the ground for one player who stands near it, so a second player asking at
    /// the same moment gets <see cref="InventoryProblem.Gone"/>. The result comes from the tick thread.
    /// </summary>
    public Task<(InventoryProblem Problem, int Item, int Quantity)> ClaimAsync(int playerId, int groundId)
    {
        var result = new TaskCompletionSource<(InventoryProblem, int, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        inbox.Enqueue(() =>
        {
            RoomPlayer? player = Room.Find(playerId);

            if (!ground.TryGetValue(groundId, out LyingItem? lying)
                || lying.ClaimedBy != 0
                || (player != null && !MapRules.SeeEachOther(config, lying.Map, player.Map, lying.Owner, playerId)))
            {
                // On another map, or in someone else's house, the item is not there for this player.
                result.SetResult((InventoryProblem.Gone, 0, 0));
            }
            else if (player == null || player.IsDead || !InventoryRules.InReach(config.Bag, player.Movement.X, player.Movement.Y, lying.X, lying.Y))
            {
                result.SetResult((InventoryProblem.TooFar, 0, 0));
            }
            else
            {
                lying.ClaimedBy = playerId;
                result.SetResult((InventoryProblem.None, lying.Item, lying.Quantity));
            }
        });
        return result.Task;
    }

    /// <summary>The claimed item is in the player's bag now; it leaves the ground.</summary>
    public void Take(int groundId) => inbox.Enqueue(() => ground.Remove(groundId));

    /// <summary>The claim failed (bag full, database down): the item is free for anyone again.</summary>
    public void Release(int groundId)
    {
        inbox.Enqueue(() =>
        {
            if (ground.TryGetValue(groundId, out LyingItem? lying))
            {
                lying.ClaimedBy = 0;
            }
        });
    }

    /// <summary>Where each player in the channel stood at the last snapshot, for saving when the server stops.</summary>
    public IReadOnlyDictionary<int, MapSpot> Positions => positions;

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
        ExpireGround();

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

    private void AddPlayer(Session session, IConnection connection, MapSpot? spawn)
    {
        RoomPlayer? player = Room.Join(session.PlayerId, spawn?.X, spawn?.Map);

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
            slot.Session.Identity?.Name ?? string.Empty,
            player.Map)));
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
            PlayerLeaving?.Invoke(slot.Session, Room.Find(playerId)?.Spot ?? new MapSpot(Room.StartMap, server.SpawnX));
            Room.Leave(playerId);
            positions.TryRemove(playerId, out _);
            PlayerRemoved?.Invoke(playerId);
        }
    }

    /// <summary>Items left lying too long disappear; one being picked up stays until that finishes.</summary>
    private void ExpireGround()
    {
        List<int>? expired = null;

        foreach (LyingItem lying in ground.Values)
        {
            if (lying.ClaimedBy == 0 && Room.Tick >= lying.ExpiresTick)
            {
                (expired ??= new List<int>()).Add(lying.Id);
            }
        }

        if (expired != null)
        {
            foreach (int id in expired)
            {
                ground.Remove(id);
            }
        }
    }

    /// <summary>
    /// Keeps what happened since the last snapshot, with the map it happened on; a snapshot carries
    /// at most 255 events.
    /// </summary>
    private void CollectEvents()
    {
        foreach (RoomEvent item in Room.Events)
        {
            if (pendingEvents.Count < byte.MaxValue)
            {
                int map = item.PlayerId == 0 ? Room.BossMap : Room.Find(item.PlayerId)?.Map ?? Room.BossMap;
                pendingEvents.Add((NetEvent.From(item, config.Boss), map));
            }
        }
    }

    /// <summary>
    /// Each player hears only about their own map. On a map that is not shared, such as the house,
    /// they see only themselves and what they dropped there.
    /// </summary>
    private void SendSnapshots()
    {
        var everyone = new List<(RemotePlayer Player, int Map)>(Room.Count);
        var projectiles = new List<(RemoteProjectile Shot, int Map)>();

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
            everyone.Add((new RemotePlayer(
                player.Id,
                (float)movement.X,
                (float)movement.Y,
                (sbyte)movement.Facing,
                flags,
                cast.Casting,
                cast.CastTicks,
                cast.ComboStep,
                (sbyte)(cast.Direction == 0 ? movement.Facing : cast.Direction),
                slots[player.Id].Appearance), player.Map));

            foreach (ProjectileSnapshot shot in cast.Projectiles)
            {
                projectiles.Add((new RemoteProjectile(player.Id, shot.Slot, (float)shot.X, (float)shot.Y, (sbyte)shot.Direction), player.Map));
            }
        }

        BossSnapshot boss = Room.Boss.Capture();
        var events = pendingEvents.ToArray();
        pendingEvents.Clear();
        var yards = new Dictionary<int, RemoteDummy[]>();

        foreach (RoomPlayer player in Room.Players)
        {
            positions[player.Id] = player.Spot;
        }

        foreach (PlayerSlot slot in slots.Values)
        {
            if (slot.Connection == null)
            {
                continue;
            }

            RoomPlayer self = Room.Find(slot.PlayerId)!;
            int map = self.Map;
            int id = slot.PlayerId;
            List<RemotePlayer> others = everyone
                .Where(entry => entry.Player.Id != id && MapRules.SeeEachOther(config, entry.Map, map, entry.Player.Id, id))
                .Select(entry => entry.Player)
                .ToList();
            List<RemoteProjectile> shots = projectiles
                .Where(entry => MapRules.SeeEachOther(config, entry.Map, map, entry.Shot.OwnerId, id))
                .Select(entry => entry.Shot)
                .Take(byte.MaxValue)
                .ToList();
            NetEvent[] heard = events
                .Where(entry => MapRules.SeeEachOther(config, entry.Map, map, entry.Event.PlayerId == 0 ? id : entry.Event.PlayerId, id))
                .Select(entry => entry.Event)
                .ToArray();
            GroundItem[] lyingHere = ground.Values
                .Where(lying => lying.ClaimedBy == 0 && MapRules.SeeEachOther(config, lying.Map, map, lying.Owner, id))
                .Take(byte.MaxValue)
                .Select(lying => new GroundItem(lying.Id, lying.Item, (float)lying.X, (float)lying.Y))
                .ToArray();
            var snapshot = new SnapshotMessage(
                (uint)Room.Tick,
                slot.LastSequence,
                self.Capture(),
                boss,
                Room.BossRespawnTicksLeft,
                others,
                shots,
                heard,
                slot.Appearance,
                lyingHere,
                map,
                Room.Weather.Now,
                YardOf(map, yards));
            slot.Connection.Send(Packet.Encode(snapshot));
        }
    }

    /// <summary>The dummies of a map as they go out, worked out once per snapshot for everyone on it.</summary>
    private RemoteDummy[] YardOf(int map, Dictionary<int, RemoteDummy[]> done)
    {
        if (done.TryGetValue(map, out RemoteDummy[]? yard))
        {
            return yard;
        }

        IReadOnlyList<DummyState>? dummies = Room.Yard(map);
        yard = dummies == null
            ? Array.Empty<RemoteDummy>()
            : dummies.Select(dummy => new RemoteDummy(dummy.Hits, dummy.Fallen, dummy.TicksSinceChange)).ToArray();
        done[map] = yard;
        return yard;
    }

    /// <summary>
    /// An item on the ground; ClaimedBy is the player picking it up, 0 while it is free. Map and Owner
    /// say who can see it: anyone on a shared map, only the owner in a house.
    /// </summary>
    private sealed class LyingItem
    {
        public LyingItem(int id, int item, int quantity, double x, double y, int expiresTick, int map, int owner)
        {
            Id = id;
            Item = item;
            Quantity = quantity;
            X = x;
            Y = y;
            ExpiresTick = expiresTick;
            Map = map;
            Owner = owner;
        }

        public int Id { get; }
        public int Item { get; }
        public int Quantity { get; }
        public double X { get; }
        public double Y { get; }
        public int ExpiresTick { get; }
        public int Map { get; }
        public int Owner { get; }
        public int ClaimedBy { get; set; }
    }

    private sealed class PlayerSlot
    {
        private readonly Queue<InputMessage> inputs = new();
        private readonly int maxQueued;
        private MovementIntent lastMove;
        private bool doorWaiting;

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
                InputMessage dropped = inputs.Dequeue();
                LastSequence = dropped.Sequence;

                // A door press is a single tap that is never predicted, so it must not be lost with a burst of old inputs.
                doorWaiting |= dropped.Door;
            }
        }

        public void ClearInputs()
        {
            inputs.Clear();
            lastMove = default;
            doorWaiting = false;
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

            if (doorWaiting)
            {
                doorWaiting = false;
                intent = new RoomIntent(intent.Move, intent.Combat, intent.Refill, door: true);
            }

            return intent;
        }
    }
}
