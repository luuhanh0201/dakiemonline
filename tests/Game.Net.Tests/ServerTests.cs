using Game.Rules;
using Game.Server;

namespace Game.Net.Tests;

public class ServerTests
{
    private const int Precision = 9;

    private readonly GameConfig config = TestConfig.Load();

    private sealed class FakeConnection : IConnection
    {
        public List<NetMessage> Received { get; } = new();
        public bool Closed { get; private set; }

        public void Send(byte[] packet) => Received.Add(Packet.Decode(packet));

        public void Close() => Closed = true;

        public T Last<T>()
            where T : NetMessage => Received.OfType<T>().Last();
    }

    private static Session Join(GameServer server, FakeConnection connection, byte[]? token = null)
    {
        Session? session = server.Hello(new HelloMessage(Packet.ProtocolVersion, token ?? Array.Empty<byte>()), connection);
        Assert.NotNull(session);
        return session!;
    }

    private static void TickAll(GameServer server, int ticks = 1)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            foreach (ChannelRunner runner in server.Runners)
            {
                runner.Tick();
            }
        }
    }

    private static void SendInputs(Session session, IConnection connection, ref uint sequence, double moveX, int count)
    {
        for (int index = 0; index < count; index++)
        {
            session.Runner.Input(session.PlayerId, connection, InputMessage.FromJoystick(++sequence, moveX, 0d, false));
        }
    }

    /// <summary>One input then one tick, like a client keeping pace with the server.</summary>
    private static void Drive(GameServer server, Session session, IConnection connection, ref uint sequence, double moveX, int ticks)
    {
        for (int tick = 0; tick < ticks; tick++)
        {
            SendInputs(session, connection, ref sequence, moveX, 1);
            TickAll(server);
        }
    }

    [Fact]
    public void NewPlayerIsWelcomedWithATokenAndSpawnPoint()
    {
        var server = new GameServer(config, TestConfig.Server());
        var client = new FakeConnection();

        Session session = Join(server, client);
        TickAll(server);

        WelcomeMessage welcome = client.Last<WelcomeMessage>();
        Assert.Equal(session.PlayerId, welcome.PlayerId);
        Assert.False(welcome.Resumed);
        Assert.Equal(Packet.TokenSize, welcome.Token.Length);
        Assert.Equal(5d, welcome.Self.Movement.X);
    }

    [Fact]
    public void WrongVersionIsRejected()
    {
        var server = new GameServer(config, TestConfig.Server());
        var client = new FakeConnection();

        Assert.Null(server.Hello(new HelloMessage(Packet.ProtocolVersion + 1, Array.Empty<byte>()), client));
        Assert.Equal(RejectReason.VersionMismatch, client.Last<RejectMessage>().Reason);
        Assert.True(client.Closed);
    }

    [Fact]
    public void ChannelsFillInOrderAndRefuseWhenAllAreFull()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 2, players: 2));
        var sessions = Enumerable.Range(0, 4).Select(_ => Join(server, new FakeConnection())).ToList();

        Assert.Equal(new[] { 1, 1, 2, 2 }, sessions.Select(session => session.Runner.Id));

        var late = new FakeConnection();
        Assert.Null(server.Hello(new HelloMessage(Packet.ProtocolVersion, Array.Empty<byte>()), late));
        Assert.Equal(RejectReason.ServerFull, late.Last<RejectMessage>().Reason);
    }

    [Fact]
    public void TwoPlayersInAChannelSeeEachOtherMove()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1, snapshotEvery: 1));
        var first = new FakeConnection();
        var second = new FakeConnection();
        Session a = Join(server, first);
        Session b = Join(server, second);
        TickAll(server);

        uint sequence = 0;
        Drive(server, a, first, ref sequence, 1d, Ticks.PerSecond);

        SnapshotMessage seenByB = second.Last<SnapshotMessage>();
        RemotePlayer seenA = Assert.Single(seenByB.Others);
        Assert.Equal(a.PlayerId, seenA.Id);
        Assert.Equal(5f + 4f, seenA.X, 4);
        Assert.Equal(5d, seenByB.Self.Movement.X, Precision);

        SnapshotMessage seenByA = first.Last<SnapshotMessage>();
        Assert.Equal(sequence, seenByA.AckSequence);
        Assert.Equal(b.PlayerId, Assert.Single(seenByA.Others).Id);
    }

    [Fact]
    public void ChannelsAreSeparate()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 2, players: 1));
        var first = new FakeConnection();
        var second = new FakeConnection();
        Join(server, first);
        Join(server, second);
        TickAll(server, 4);

        Assert.Empty(first.Last<SnapshotMessage>().Others);
        Assert.Empty(second.Last<SnapshotMessage>().Others);
    }

    [Fact]
    public void SnapshotsGoOutEveryConfiguredTicks()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1, snapshotEvery: 3));
        var client = new FakeConnection();
        Join(server, client);

        TickAll(server, 9);

        Assert.Equal(new uint[] { 3, 6, 9 }, client.Received.OfType<SnapshotMessage>().Select(s => s.ServerTick));
    }

    [Fact]
    public void ServerMovesExactlyAsTheClientPredicts()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1, snapshotEvery: 1));
        var client = new FakeConnection();
        Session session = Join(server, client);
        TickAll(server);
        var predictor = new Predictor(config, client.Last<WelcomeMessage>().Self, 0u, testRoom: true);
        int checkedTicks = 0;

        for (int tick = 0; tick < 240; tick++)
        {
            double stick = tick % 30 < 20 ? 1d : -0.4d;
            bool jump = tick % 45 == 10;
            SkillSlot? skill = tick % 25 == 5 ? (SkillSlot)(tick / 25 % 6) : null;
            InputMessage input = predictor.Advance(stick, jump ? 1d : 0d, tick % 50 == 25, skill, tick == 100);
            session.Runner.Input(session.PlayerId, client, input);
            TickAll(server);

            SnapshotMessage snapshot = client.Last<SnapshotMessage>();
            var predicted = new PlayerSnapshot(predictor.Movement.Capture(), predictor.Combat.Capture(), predictor.Resources.Capture(), 0);
            Assert.Equal(predictor.LastSequence, snapshot.AckSequence);

            // Until the boss lands a blow, which only the server knows about, the guess is exact.
            if (snapshot.Self.Resources.Hp == config.Character.MaxHp)
            {
                Assert.Equal(snapshot.Self, predicted);
                checkedTicks++;
            }

            predictor.Boss = BossState.FromSnapshot(config.Boss, snapshot.Boss);
            predictor.Reconcile(snapshot.AckSequence, snapshot.Self);
        }

        Assert.True(checkedTicks > 60, $"{checkedTicks} ticks compared");
    }

    [Fact]
    public void OldOrRepeatedInputsAreIgnored()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1, snapshotEvery: 1));
        var client = new FakeConnection();
        Session session = Join(server, client);

        session.Runner.Input(session.PlayerId, client, InputMessage.FromJoystick(5, 1d, 0d, false));
        session.Runner.Input(session.PlayerId, client, InputMessage.FromJoystick(5, 1d, 0d, false));
        session.Runner.Input(session.PlayerId, client, InputMessage.FromJoystick(3, -1d, 0d, false));
        TickAll(server, 3);

        Assert.Equal(5u, client.Last<SnapshotMessage>().AckSequence);
    }

    [Fact]
    public void TooManyQueuedInputsDropTheOldest()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1, snapshotEvery: 1, maxQueued: 3));
        var client = new FakeConnection();
        Session session = Join(server, client);
        uint sequence = 0;

        SendInputs(session, client, ref sequence, 1d, 10);
        TickAll(server);

        Assert.Equal(8u, client.Last<SnapshotMessage>().AckSequence);
    }

    [Fact]
    public void WithoutNewInputTheStickStaysWhereItWas()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1, snapshotEvery: 1));
        var client = new FakeConnection();
        Session session = Join(server, client);
        uint sequence = 0;

        SendInputs(session, client, ref sequence, 1d, 1);
        TickAll(server, 3);

        Assert.Equal(5d + 3 * config.Movement.RunSpeed * Ticks.DeltaSeconds, client.Last<SnapshotMessage>().Self.Movement.X, Precision);
    }

    [Fact]
    public void ReconnectWithTokenKeepsPositionAndStamina()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 2));
        var first = new FakeConnection();
        Session session = Join(server, first);
        TickAll(server);
        byte[] token = first.Last<WelcomeMessage>().Token;
        session.Runner.Input(session.PlayerId, first, InputMessage.FromJoystick(1, 1d, 0d, true));
        TickAll(server);
        uint sequence = 1;
        Drive(server, session, first, ref sequence, 1d, 20);
        MovementSnapshot before = session.Runner.Room.Find(session.PlayerId)!.Movement.Capture();

        session.Runner.Disconnected(session.PlayerId, first);
        TickAll(server, Ticks.PerSecond);

        var second = new FakeConnection();
        Session resumed = Join(server, second, token);
        TickAll(server);

        WelcomeMessage welcome = second.Last<WelcomeMessage>();
        Assert.True(welcome.Resumed);
        Assert.Equal(session.PlayerId, welcome.PlayerId);
        Assert.Equal(session.Runner.Id, welcome.ChannelId);
        Assert.Same(session, resumed);
        Assert.Equal(sequence, welcome.LastInputSequence);
        Assert.Equal(before.X, welcome.Self.Movement.X, Precision);
        Assert.True(welcome.Self.Movement.Stamina > before.Stamina, $"stamina kept regenerating while away: {before.Stamina} -> {welcome.Self.Movement.Stamina}");
        Assert.True(welcome.Self.Movement.Stamina < config.Character.MaxStamina);
    }

    [Fact]
    public void OthersSeeAnAwayPlayerUntilTheHoldRunsOut()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1, holdSec: 1d));
        var staying = new FakeConnection();
        var leaving = new FakeConnection();
        Join(server, staying);
        Session gone = Join(server, leaving);
        TickAll(server, 2);
        byte[] token = leaving.Last<WelcomeMessage>().Token;

        gone.Runner.Disconnected(gone.PlayerId, leaving);
        TickAll(server, 2);
        Assert.Equal(RemoteFlags.Away, Assert.Single(staying.Last<SnapshotMessage>().Others).Flags & RemoteFlags.Away);
        Assert.Equal(2, server.SessionCount);

        TickAll(server, Ticks.PerSecond);
        Assert.Empty(staying.Last<SnapshotMessage>().Others);
        Assert.Equal(1, server.SessionCount);

        var late = new FakeConnection();
        Session? fresh = server.Hello(new HelloMessage(Packet.ProtocolVersion, token), late);
        TickAll(server);
        Assert.NotEqual(gone.PlayerId, fresh!.PlayerId);
        Assert.False(late.Last<WelcomeMessage>().Resumed);
    }

    [Fact]
    public void LeaveFreesThePlaceAtOnce()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1, players: 1));
        var client = new FakeConnection();
        Session session = Join(server, client);
        TickAll(server);

        session.Runner.Leave(session.PlayerId, client);
        TickAll(server);

        Assert.Equal(0, server.SessionCount);
        Assert.NotNull(server.Hello(new HelloMessage(Packet.ProtocolVersion, Array.Empty<byte>()), new FakeConnection()));
    }

    [Fact]
    public void NewConnectionWithTheTokenReplacesAStaleOne()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1));
        var stale = new FakeConnection();
        Session session = Join(server, stale);
        TickAll(server);
        byte[] token = stale.Last<WelcomeMessage>().Token;

        var fresh = new FakeConnection();
        Join(server, fresh, token);
        TickAll(server, 2);

        Assert.True(stale.Closed);
        session.Runner.Input(session.PlayerId, stale, InputMessage.FromJoystick(50, 1d, 0d, false));
        session.Runner.Disconnected(session.PlayerId, stale);
        TickAll(server, 2);

        Assert.NotEqual(50u, fresh.Last<SnapshotMessage>().AckSequence);
        Assert.Equal(1, server.SessionCount);
        Assert.Empty(fresh.Received.OfType<SnapshotMessage>().Last().Others);
    }

    /// <summary>Both players run to the boss, then keep pressing attack.</summary>
    private static void Fight(GameServer server, (Session Session, IConnection Connection)[] fighters, int ticks)
    {
        uint sequence = 0;

        for (int tick = 0; tick < ticks; tick++)
        {
            sequence++;

            foreach ((Session session, IConnection connection) in fighters)
            {
                bool running = tick < Ticks.PerSecond;
                session.Runner.Input(
                    session.PlayerId,
                    connection,
                    InputMessage.FromJoystick(sequence, running ? 1d : 0d, 0d, false, running ? null : SkillSlot.Attack));
            }

            TickAll(server);
        }
    }

    [Fact]
    public void TwoPlayersBothDamageTheBossAndOnlyTheServerCounts()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1));
        var first = new FakeConnection();
        var second = new FakeConnection();
        Session a = Join(server, first);
        Session b = Join(server, second);
        TickAll(server);

        Fight(server, new[] { (a, (IConnection)first), (b, (IConnection)second) }, Ticks.PerSecond * 4);

        List<NetEvent> seenByA = first.Received.OfType<SnapshotMessage>().SelectMany(s => s.Events).ToList();
        List<NetEvent> hits = seenByA.Where(e => e.Kind == RoomEventKind.Combat && e.CombatKind == CombatEventKind.SkillHit).ToList();
        Assert.Contains(hits, e => e.PlayerId == a.PlayerId);
        Assert.Contains(hits, e => e.PlayerId == b.PlayerId);
        int bossHp = first.Last<SnapshotMessage>().Boss.Hp;
        Assert.Equal(config.Boss.MaxHp - hits.Sum(e => e.Amount), bossHp);
        Assert.Equal(bossHp, second.Last<SnapshotMessage>().Boss.Hp);
        Assert.True(a.Runner.Room.Boss.Hp <= bossHp);
    }

    [Fact]
    public void OthersSeeTheSkillBeingCast()
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1, snapshotEvery: 1));
        var caster = new FakeConnection();
        var watcher = new FakeConnection();
        Session a = Join(server, caster);
        Join(server, watcher);
        TickAll(server);

        a.Runner.Input(a.PlayerId, caster, InputMessage.FromJoystick(1, 0d, 0d, false, SkillSlot.OptionB));
        TickAll(server, 3);

        SnapshotMessage seen = watcher.Last<SnapshotMessage>();
        RemotePlayer other = Assert.Single(seen.Others);
        Assert.Equal(SkillSlot.OptionB, other.Casting);
        Assert.Equal(3, other.CastTicks);
        Assert.Contains(
            watcher.Received.OfType<SnapshotMessage>().SelectMany(s => s.Events),
            e => e.PlayerId == a.PlayerId && e.CombatKind == CombatEventKind.SkillStarted && e.Slot == SkillSlot.OptionB);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RefillWorksOnlyWhenTheServerIsATestRoom(bool testRoom)
    {
        var server = new GameServer(config, TestConfig.Server(channels: 1, snapshotEvery: 1, testRoom: testRoom));
        var client = new FakeConnection();
        Session session = Join(server, client);
        TickAll(server);
        Assert.Equal(testRoom, client.Last<WelcomeMessage>().TestRoom);

        session.Runner.Input(session.PlayerId, client, InputMessage.FromJoystick(1, 1d, 0d, true));
        TickAll(server, 2);
        double spent = client.Last<SnapshotMessage>().Self.Movement.Stamina;
        session.Runner.Input(session.PlayerId, client, InputMessage.FromJoystick(2, 0d, 0d, false, refill: true));
        TickAll(server);

        double after = client.Last<SnapshotMessage>().Self.Movement.Stamina;
        Assert.True(spent < config.Character.MaxStamina);
        Assert.Equal(testRoom, after == config.Character.MaxStamina);
        Assert.Equal(testRoom, client.Last<SnapshotMessage>().Events.Any(e => e.Kind == RoomEventKind.Refilled));
    }
}
