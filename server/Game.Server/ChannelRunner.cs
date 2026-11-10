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
    public Session(int playerId, byte[] token, ChannelRunner runner)
    {
        PlayerId = playerId;
        Token = token;
        Runner = runner;
    }

    public int PlayerId { get; }
    public byte[] Token { get; }
    public ChannelRunner Runner { get; }
}

/// <summary>
/// Runs one <see cref="Channel"/>. Connection threads only queue commands; everything else
/// happens inside <see cref="Tick"/>, so the channel never needs a lock.
/// </summary>
public sealed class ChannelRunner
{
    private readonly ServerConfig server;
    private readonly ConcurrentQueue<Action> inbox = new();
    private readonly Dictionary<int, PlayerSlot> slots = new();
    private readonly Dictionary<int, MovementIntent> intents = new();
    private readonly int holdTicks;
    private int currentTick;

    public ChannelRunner(int id, GameConfig config, ServerConfig server)
    {
        this.server = server;
        Channel = new Channel(id, config, server.PlayersPerChannel);
        holdTicks = Ticks.FromSeconds(server.ReconnectHoldSec);
    }

    public Channel Channel { get; }
    public int Id => Channel.Id;

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

        Channel.Step(intents);
        Volatile.Write(ref currentTick, Channel.Tick);
        ExpireAwayPlayers();

        if (Channel.Tick % server.SnapshotEveryTicks == 0)
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
        ChannelPlayer? player = Channel.Join(session.PlayerId, server.SpawnX);

        if (player == null)
        {
            connection.Send(Packet.Encode(new RejectMessage(RejectReason.ServerFull)));
            connection.Close();
            PlayerRemoved?.Invoke(session.PlayerId);
            return;
        }

        var slot = new PlayerSlot(session, connection, server.MaxQueuedInputs);
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
            slot.Connection.Close();
        }

        slot.Connection = connection;
        slot.AwayTicksLeft = 0;
        slot.ClearInputs();
        SendWelcome(slot, Channel.Find(session.PlayerId)!, resumed: true);
    }

    private void SendWelcome(PlayerSlot slot, ChannelPlayer player, bool resumed)
    {
        slot.Connection!.Send(Packet.Encode(new WelcomeMessage(
            slot.PlayerId,
            Id,
            (uint)Channel.Tick,
            resumed,
            slot.Session.Token,
            slot.LastSequence,
            player.Movement.Capture())));
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
            Channel.Leave(playerId);
            PlayerRemoved?.Invoke(playerId);
        }
    }

    private void SendSnapshots()
    {
        var everyone = new List<RemotePlayer>(Channel.Count);

        foreach (ChannelPlayer player in Channel.Players)
        {
            MovementState movement = player.Movement;
            RemoteFlags flags = RemoteFlags.None;
            flags |= movement.Grounded ? RemoteFlags.Grounded : RemoteFlags.None;
            flags |= movement.IsDashing ? RemoteFlags.Dashing : RemoteFlags.None;
            flags |= slots[player.Id].Connection == null ? RemoteFlags.Away : RemoteFlags.None;
            everyone.Add(new RemotePlayer(player.Id, (float)movement.X, (float)movement.Y, (sbyte)movement.Facing, flags));
        }

        foreach (PlayerSlot slot in slots.Values)
        {
            if (slot.Connection == null)
            {
                continue;
            }

            List<RemotePlayer> others = everyone.FindAll(player => player.Id != slot.PlayerId);
            var snapshot = new SnapshotMessage(
                (uint)Channel.Tick,
                slot.LastSequence,
                Channel.Find(slot.PlayerId)!.Movement.Capture(),
                others);
            slot.Connection.Send(Packet.Encode(snapshot));
        }
    }

    private sealed class PlayerSlot
    {
        private readonly Queue<InputMessage> inputs = new();
        private readonly int maxQueued;
        private MovementIntent lastIntent;

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
            lastIntent = default;
        }

        /// <summary>One queued input per tick. With none waiting, the stick is held where it was, without a new dash.</summary>
        public MovementIntent NextIntent()
        {
            if (inputs.Count == 0)
            {
                return new MovementIntent(lastIntent.MoveX, lastIntent.MoveY, false);
            }

            InputMessage input = inputs.Dequeue();
            LastSequence = input.Sequence;
            lastIntent = input.ToIntent();
            return lastIntent;
        }
    }
}
