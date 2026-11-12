using System;
using System.Collections.Generic;
using Game.Rules;

namespace Game.Net
{
    /// <summary>
    /// The client's guess of the server tick, running at 30 ticks a second between snapshots and
    /// pulled toward each snapshot as it arrives, so jitter does not make others stutter.
    /// </summary>
    public sealed class ServerClock
    {
        /// <summary>Further off than this, the clock jumps instead of easing.</summary>
        public const double SnapTicks = 15d;

        private const double Pull = 0.1d;

        public bool Started { get; private set; }
        public double Tick { get; private set; }

        public void Advance(double ticks)
        {
            if (Started)
            {
                Tick += ticks;
            }
        }

        public void Observe(uint serverTick)
        {
            double error = serverTick - Tick;

            if (!Started || Math.Abs(error) > SnapTicks)
            {
                Tick = serverTick;
                Started = true;
                return;
            }

            Tick += error * Pull;
        }
    }

    /// <summary>Where another player is at a moment, and the skill they are in the middle of.</summary>
    public readonly struct TrackSample
    {
        public TrackSample(double tick, float x, float y, in RemotePlayer source, int castTicks)
        {
            Tick = tick;
            X = x;
            Y = y;
            Facing = source.Facing;
            Flags = source.Flags;
            Casting = source.Casting;
            CastTicks = castTicks;
            ComboStep = source.ComboStep;
            CastDirection = source.CastDirection;
            Appearance = source.Appearance;
        }

        public double Tick { get; }
        public float X { get; }
        public float Y { get; }
        public sbyte Facing { get; }
        public RemoteFlags Flags { get; }
        public SkillSlot? Casting { get; }
        public int CastTicks { get; }
        public int ComboStep { get; }
        public sbyte CastDirection { get; }

        /// <summary>What they wear, from the nearer sample, so a change shows with the drawn position.</summary>
        public Appearance Appearance { get; }
    }

    /// <summary>Recent positions of one other player, drawn a little in the past so there are two to blend.</summary>
    public sealed class RemoteTrack
    {
        /// <summary>About 133 ms: two snapshots at 15 a second, with room for one to be late.</summary>
        public const double DefaultDelayTicks = 4d;

        private const int MaxSamples = 32;

        private readonly List<(uint Tick, RemotePlayer Player)> samples = new List<(uint, RemotePlayer)>();

        public int Count => samples.Count;

        public void Add(uint serverTick, in RemotePlayer player)
        {
            if (samples.Count > 0 && serverTick <= samples[samples.Count - 1].Tick)
            {
                return;
            }

            if (samples.Count == MaxSamples)
            {
                samples.RemoveAt(0);
            }

            samples.Add((serverTick, player));
        }

        /// <summary>
        /// The position at a tick: blended between samples, held at the ends. Facing and flags come
        /// from the nearer sample; a cast keeps running from the sample it started in.
        /// </summary>
        public TrackSample Sample(double tick)
        {
            if (samples.Count == 0)
            {
                throw new InvalidOperationException("no samples yet");
            }

            (uint firstTick, RemotePlayer first) = samples[0];

            if (tick <= firstTick)
            {
                return new TrackSample(tick, first.X, first.Y, first, first.CastTicks);
            }

            for (int index = 1; index < samples.Count; index++)
            {
                (uint afterTick, RemotePlayer after) = samples[index];

                if (tick > afterTick)
                {
                    continue;
                }

                (uint beforeTick, RemotePlayer before) = samples[index - 1];
                float share = (float)((tick - beforeTick) / (afterTick - beforeTick));
                RemotePlayer nearer = share < 0.5f ? before : after;
                return new TrackSample(
                    tick,
                    before.X + (after.X - before.X) * share,
                    before.Y + (after.Y - before.Y) * share,
                    nearer,
                    CastTicksAt(before, beforeTick, nearer, tick));
            }

            (uint lastTick, RemotePlayer last) = samples[samples.Count - 1];
            return new TrackSample(tick, last.X, last.Y, last, CastTicksAt(last, lastTick, last, tick));
        }

        private static int CastTicksAt(in RemotePlayer from, uint fromTick, in RemotePlayer shown, double tick)
        {
            if (shown.Casting == null)
            {
                return 0;
            }

            // Run on from the earlier sample while it is the same cast, so the pose moves every frame.
            bool sameCast = from.Casting == shown.Casting && from.CastTicks <= shown.CastTicks;
            return sameCast ? from.CastTicks + (int)Math.Floor(tick - fromTick) : shown.CastTicks;
        }
    }

    /// <summary>Round-trip times in milliseconds, for the latency report.</summary>
    public sealed class LatencyStats
    {
        private readonly List<double> samples = new List<double>();

        public int Count => samples.Count;
        public double Mean => samples.Count == 0 ? 0d : Sum() / samples.Count;
        public double Max => samples.Count == 0 ? 0d : Sorted()[samples.Count - 1];
        public double Min => samples.Count == 0 ? 0d : Sorted()[0];

        public void Add(double milliseconds)
        {
            samples.Add(milliseconds);
        }

        /// <summary>Nearest-rank percentile, so p95 of 20 samples is the 19th smallest.</summary>
        public double Percentile(double percent)
        {
            if (samples.Count == 0)
            {
                return 0d;
            }

            List<double> sorted = Sorted();
            int rank = (int)Math.Ceiling(percent / 100d * sorted.Count);
            return sorted[Math.Max(1, Math.Min(sorted.Count, rank)) - 1];
        }

        private double Sum()
        {
            double sum = 0d;

            foreach (double sample in samples)
            {
                sum += sample;
            }

            return sum;
        }

        private List<double> Sorted()
        {
            var sorted = new List<double>(samples);
            sorted.Sort();
            return sorted;
        }
    }
}
