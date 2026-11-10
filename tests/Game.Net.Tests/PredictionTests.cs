using Game.Rules;

namespace Game.Net.Tests;

public class PredictionTests
{
    private const int Precision = 9;

    // The boss waits far off, so every difference would come from the prediction itself.
    private readonly GameConfig config = TestConfig.Load(("\"startDistance\": 8", "\"startDistance\": 24"), ("\"moveSpeed\": 1.5", "\"moveSpeed\": 0.0001"));

    /// <summary>Running, a jump, every skill, a dash and a potion, over and over.</summary>
    private static (double X, double Y, bool Dash, SkillSlot? Skill, bool Potion) Stick(int tick)
    {
        return (tick % 60) switch
        {
            < 10 => (1d, 0d, false, null, false),
            10 => (0d, 0d, false, SkillSlot.Attack, false),
            15 => (1d, 1d, false, null, false),
            22 => (0d, 0d, false, SkillSlot.Ranged, false),
            30 => (-1d, 0d, true, null, false),
            38 => (0d, 0d, false, SkillSlot.OptionA, false),
            46 => (0d, 0d, false, (SkillSlot)(tick / 60 % 6), true),
            < 52 => (0.3d, 0d, false, null, false),
            _ => (-0.8d, 0d, false, null, false),
        };
    }

    /// <summary>A server room that applies each input in order, as <see cref="Game.Server.ChannelRunner"/> does.</summary>
    private sealed class FakeServer
    {
        private readonly Room room;

        public FakeServer(GameConfig config)
        {
            room = new Room(1, config, TestConfig.Server());
            room.Join(1);
        }

        public uint Ack { get; private set; }
        public PlayerSnapshot State => room.Find(1)!.Capture();
        public BossState Boss => room.Boss;

        public void Apply(InputMessage input)
        {
            room.Step(new Dictionary<int, RoomIntent> { [1] = input.ToIntent() });
            Ack = input.Sequence;
        }
    }

    [Fact]
    public void PredictionMatchesTheServerWithLagThroughEverySkill()
    {
        var server = new FakeServer(config);
        var predictor = new Predictor(config, server.State, 0u, testRoom: true);
        var inFlight = new Queue<InputMessage>();
        const int LagTicks = 6;
        int casts = 0;

        for (int tick = 0; tick < 400; tick++)
        {
            (double x, double y, bool dash, SkillSlot? skill, bool potion) = Stick(tick);
            inFlight.Enqueue(predictor.Advance(x, y, dash, skill, potion));
            casts += predictor.PredictedEvents.Count(item => item.Kind == CombatEventKind.SkillStarted);

            if (inFlight.Count > LagTicks)
            {
                server.Apply(inFlight.Dequeue());
            }

            if (tick % 2 == 0 && tick > LagTicks)
            {
                PlayerSnapshot before = Capture(predictor);
                predictor.Boss = BossState.FromSnapshot(config.Boss, server.Boss.Capture());
                predictor.Reconcile(server.Ack, server.State);

                Assert.Equal(Describe(before), Describe(Capture(predictor)));
                Assert.Equal(before, Capture(predictor));
                Assert.Equal(0d, predictor.LastCorrection);
                Assert.Equal(LagTicks, predictor.PendingCount);
            }
        }

        Assert.True(casts >= 12, $"{casts} skills were cast");
    }

    [Fact]
    public void ServerDisagreementIsCorrectedAndReplayed()
    {
        var server = new FakeServer(config);
        var predictor = new Predictor(config, server.State, 0u, testRoom: true);
        var sent = new List<InputMessage>();

        for (int tick = 0; tick < 10; tick++)
        {
            sent.Add(predictor.Advance(1d, 0d, false));
        }

        // The server missed the first three inputs and stood still for them instead.
        for (int index = 3; index < 6; index++)
        {
            server.Apply(sent[index]);
        }

        predictor.Reconcile(server.Ack, server.State);

        double expected = server.State.Movement.X + 4 * config.Movement.RunSpeed * Ticks.DeltaSeconds;
        Assert.Equal(expected, predictor.Movement.X, Precision);
        Assert.Equal(3 * config.Movement.RunSpeed * Ticks.DeltaSeconds, predictor.LastCorrection, Precision);
        Assert.Equal(4, predictor.PendingCount);
    }

    [Fact]
    public void HealthComesOnlyFromTheServer()
    {
        var server = new FakeServer(config);
        var predictor = new Predictor(config, server.State, 0u, testRoom: true);
        PlayerSnapshot state = server.State;
        var hurt = new PlayerSnapshot(state.Movement, state.Combat, new ResourceSnapshot(400, state.Resources.Energy), 0);

        predictor.Reconcile(0u, hurt);

        Assert.Equal(400, predictor.Resources.Hp);
    }

    [Fact]
    public void RefillIsNotSentOutsideATestRoom()
    {
        var server = new FakeServer(config);

        Assert.True(new Predictor(config, server.State, 0u, testRoom: true).Advance(0d, 0d, false, refill: true).Refill);
        Assert.False(new Predictor(config, server.State, 0u, testRoom: false).Advance(0d, 0d, false, refill: true).Refill);
    }

    [Fact]
    public void ResumedPredictorNumbersOnFromTheServer()
    {
        var predictor = new Predictor(config, new FakeServer(config).State, 500u, testRoom: true);

        Assert.Equal(501u, predictor.Advance(0d, 0d, false).Sequence);
    }

    [Fact]
    public void PendingInputsAreCapped()
    {
        var predictor = new Predictor(config, new FakeServer(config).State, 0u, testRoom: true);

        for (int tick = 0; tick < Predictor.MaxPending + 30; tick++)
        {
            predictor.Advance(0d, 0d, false);
        }

        Assert.Equal(Predictor.MaxPending, predictor.PendingCount);
    }

    [Theory]
    [InlineData(5u, 4u, true)]
    [InlineData(4u, 5u, false)]
    [InlineData(5u, 5u, false)]
    [InlineData(1u, uint.MaxValue, true)]
    [InlineData(uint.MaxValue, 1u, false)]
    public void SequenceComparisonWraps(uint sequence, uint reference, bool after)
    {
        Assert.Equal(after, Predictor.IsAfter(sequence, reference));
    }

    private static string Describe(PlayerSnapshot s)
    {
        MovementSnapshot m = s.Movement;
        CombatSnapshot c = s.Combat;
        return $"tick m x={m.X:R} y={m.Y:R} vy={m.VelocityY:R} f={m.Facing} g={m.Grounded} st={m.Stamina:R} pmy={m.PreviousMoveY} jb={m.JumpBufferTicks} db={m.DashBufferTicks} co={m.CoyoteTicks} dt={m.DashTicksLeft} dd={m.DashDirection} ad={m.AirDashesUsed} sd={m.StaminaDelayTicks} | "
            + $"c cast={c.Casting} ct={c.CastTicks} combo={c.ComboStep} dir={c.Direction} ox={c.OriginX:R} lhp={c.LastHitPhase} pot={c.PotionsLeft} cd={string.Join(',', c.Cooldowns)} buf={c.BufferedSkill} sb={c.SkillBufferTicks} pb={c.PotionBufferTicks} pc={c.PotionCooldownTicks} cw={c.ComboWindowTicks} proj={c.Projectiles.Count} | "
            + $"r hp={s.Resources.Hp} en={s.Resources.Energy:R} resp={s.RespawnTicksLeft}";
    }

    private static PlayerSnapshot Capture(Predictor predictor)
    {
        return new PlayerSnapshot(
            predictor.Movement.Capture(),
            predictor.Combat.Capture(),
            predictor.Resources.Capture(),
            predictor.RespawnTicksLeft);
    }
}
