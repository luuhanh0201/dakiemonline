using Game.Rules;

namespace Game.Net.Tests;

public class PredictionTests
{
    private const int Precision = 9;

    private readonly GameConfig config = TestConfig.Load();

    private static (double X, double Y, bool Dash) Stick(int tick)
    {
        return (tick % 40) switch
        {
            < 15 => (1d, 0d, false),
            15 => (1d, 1d, false),
            < 25 => (0.3d, 0d, false),
            25 => (-1d, 0d, true),
            _ => (-0.8d, 0d, false),
        };
    }

    /// <summary>A server that applies each input in order, as <see cref="Game.Server.ChannelRunner"/> does.</summary>
    private sealed class FakeServer
    {
        private readonly Channel channel;

        public FakeServer(GameConfig config)
        {
            channel = new Channel(1, config, 20);
            channel.Join(1, 5d);
        }

        public uint Ack { get; private set; }
        public MovementSnapshot State => channel.Find(1)!.Movement.Capture();
        public MovementSnapshot Start => State;

        public void Apply(InputMessage input)
        {
            channel.Step(new Dictionary<int, MovementIntent> { [1] = input.ToIntent() });
            Ack = input.Sequence;
        }
    }

    [Fact]
    public void PredictionMatchesTheServerWithLag()
    {
        var server = new FakeServer(config);
        var predictor = new Predictor(config, server.Start, 0u);
        var inFlight = new Queue<InputMessage>();
        const int LagTicks = 6;

        for (int tick = 0; tick < 200; tick++)
        {
            (double x, double y, bool dash) = Stick(tick);
            inFlight.Enqueue(predictor.Advance(x, y, dash));

            if (inFlight.Count > LagTicks)
            {
                server.Apply(inFlight.Dequeue());
            }

            if (tick % 2 == 0 && tick > LagTicks)
            {
                MovementSnapshot before = predictor.State.Capture();
                predictor.Reconcile(server.Ack, server.State);

                Assert.Equal(before, predictor.State.Capture());
                Assert.Equal(0d, predictor.LastCorrection);
                Assert.Equal(LagTicks, predictor.PendingCount);
            }
        }
    }

    [Fact]
    public void ServerDisagreementIsCorrectedAndReplayed()
    {
        var server = new FakeServer(config);
        var predictor = new Predictor(config, server.Start, 0u);
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

        double expected = server.State.X + 4 * config.Movement.RunSpeed * Ticks.DeltaSeconds;
        Assert.Equal(expected, predictor.State.X, Precision);
        Assert.Equal(3 * config.Movement.RunSpeed * Ticks.DeltaSeconds, predictor.LastCorrection, Precision);
        Assert.Equal(4, predictor.PendingCount);
    }

    [Fact]
    public void ResumedPredictorNumbersOnFromTheServer()
    {
        var predictor = new Predictor(config, new FakeServer(config).Start, 500u);

        Assert.Equal(501u, predictor.Advance(0d, 0d, false).Sequence);
    }

    [Fact]
    public void PendingInputsAreCapped()
    {
        var predictor = new Predictor(config, new FakeServer(config).Start, 0u);

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
}
