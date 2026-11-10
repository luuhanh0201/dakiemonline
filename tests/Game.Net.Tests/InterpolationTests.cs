namespace Game.Net.Tests;

public class InterpolationTests
{
    private static RemotePlayer At(float x, float y = 0f, sbyte facing = 1, RemoteFlags flags = RemoteFlags.Grounded)
    {
        return new RemotePlayer(1, x, y, facing, flags);
    }

    [Fact]
    public void BlendsBetweenTwoSnapshots()
    {
        var track = new RemoteTrack();
        track.Add(10u, At(0f));
        track.Add(12u, At(4f, 2f));

        TrackSample middle = track.Sample(11d);

        Assert.Equal(2f, middle.X);
        Assert.Equal(1f, middle.Y);
    }

    [Fact]
    public void HoldsAtBothEnds()
    {
        var track = new RemoteTrack();
        track.Add(10u, At(1f));
        track.Add(12u, At(3f));

        Assert.Equal(1f, track.Sample(5d).X);
        Assert.Equal(3f, track.Sample(50d).X);
    }

    [Fact]
    public void FacingAndFlagsSwitchHalfway()
    {
        var track = new RemoteTrack();
        track.Add(10u, At(0f, facing: 1, flags: RemoteFlags.Grounded));
        track.Add(12u, At(0f, facing: -1, flags: RemoteFlags.Dashing));

        Assert.Equal(1, track.Sample(10.9d).Facing);
        Assert.Equal(-1, track.Sample(11.1d).Facing);
        Assert.Equal(RemoteFlags.Dashing, track.Sample(11.5d).Flags);
    }

    [Fact]
    public void LateOrRepeatedSnapshotsAreIgnored()
    {
        var track = new RemoteTrack();
        track.Add(10u, At(0f));
        track.Add(12u, At(2f));
        track.Add(12u, At(99f));
        track.Add(11u, At(99f));

        Assert.Equal(2, track.Count);
        Assert.Equal(1f, track.Sample(11d).X);
    }

    [Fact]
    public void CastKeepsRunningBetweenSnapshots()
    {
        var track = new RemoteTrack();
        track.Add(10u, new RemotePlayer(1, 0f, 0f, 1, RemoteFlags.Grounded, Game.Rules.SkillSlot.Attack, 2, 1, 1));
        track.Add(12u, new RemotePlayer(1, 0f, 0f, 1, RemoteFlags.Grounded, Game.Rules.SkillSlot.Attack, 4, 1, 1));

        Assert.Equal(3, track.Sample(11d).CastTicks);
        Assert.Equal(Game.Rules.SkillSlot.Attack, track.Sample(11d).Casting);
        Assert.Equal(1, track.Sample(11d).ComboStep);
        Assert.Equal(6, track.Sample(14d).CastTicks);
    }

    [Fact]
    public void ANewCastStartsFromItsOwnTicks()
    {
        var track = new RemoteTrack();
        track.Add(10u, new RemotePlayer(1, 0f, 0f, 1, RemoteFlags.Grounded, Game.Rules.SkillSlot.Attack, 12, 0, 1));
        track.Add(12u, new RemotePlayer(1, 0f, 0f, 1, RemoteFlags.Grounded, Game.Rules.SkillSlot.Ranged, 1, 0, 1));

        Assert.Equal(12, track.Sample(10.6d).CastTicks);
        Assert.Equal(Game.Rules.SkillSlot.Attack, track.Sample(10.6d).Casting);
        Assert.Equal(Game.Rules.SkillSlot.Ranged, track.Sample(11.6d).Casting);
        Assert.Equal(1, track.Sample(11.6d).CastTicks);
    }

    [Fact]
    public void ClockStartsAtTheFirstSnapshotAndRuns()
    {
        var clock = new ServerClock();
        clock.Advance(10d);
        Assert.False(clock.Started);

        clock.Observe(100u);
        clock.Advance(3d);

        Assert.Equal(103d, clock.Tick);
    }

    [Fact]
    public void ClockEasesSmallErrorsAndJumpsBigOnes()
    {
        var clock = new ServerClock();
        clock.Observe(100u);

        clock.Observe(110u);
        Assert.Equal(101d, clock.Tick, 6);

        clock.Observe(500u);
        Assert.Equal(500d, clock.Tick);
    }

    [Fact]
    public void LatencyStatsUseNearestRank()
    {
        var stats = new LatencyStats();

        for (int ms = 1; ms <= 20; ms++)
        {
            stats.Add(ms * 10d);
        }

        Assert.Equal(20, stats.Count);
        Assert.Equal(105d, stats.Mean);
        Assert.Equal(190d, stats.Percentile(95d));
        Assert.Equal(100d, stats.Percentile(50d));
        Assert.Equal(10d, stats.Min);
        Assert.Equal(200d, stats.Max);
        Assert.Equal(0d, new LatencyStats().Percentile(95d));
    }
}
