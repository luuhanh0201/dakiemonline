using System;
using System.Collections.Generic;

namespace Game.Rules
{
    /// <summary>How the straw dummies of a training yard behave; read from the map file's "training".</summary>
    public sealed class TrainingConfig
    {
        /// <summary>A dummy's full health; it falls when a blow brings it to 0.</summary>
        public int MaxHp { get; set; }

        /// <summary>What every blow takes off, whatever the skill: a dummy is for practice, not for testing damage.</summary>
        public int DamagePerHit { get; set; }

        /// <summary>Blows a standing dummy takes before it falls.</summary>
        public int HitsToFall => (MaxHp + DamagePerHit - 1) / DamagePerHit;

        /// <summary>A fallen dummy stands up again this long after it fell.</summary>
        public double RespawnSec { get; set; }

        public double Width { get; set; }
        public double Height { get; set; }

        /// <summary>Where each dummy stands, in file order; a dummy travels as its place here.</summary>
        public IReadOnlyList<MapDummy> Dummies { get; set; } = Array.Empty<MapDummy>();
    }

    public sealed class MapDummy
    {
        public string Id { get; set; } = string.Empty;
        public double X { get; set; }
    }

    /// <summary>One dummy now: standing with some hits taken, or lying and counting down to stand up.</summary>
    public sealed class DummyState
    {
        public DummyState(double x)
        {
            X = x;
        }

        public double X { get; }

        /// <summary>Hits taken since it last stood up.</summary>
        public int Hits { get; internal set; }

        public bool Fallen { get; internal set; }

        /// <summary>While fallen, ticks until it stands up; 0 while standing.</summary>
        public int TicksLeft { get; internal set; }

        /// <summary>Ticks since it was last hit, fell or stood up, so a late viewer can join the motion midway.</summary>
        public int TicksSinceChange { get; internal set; }
    }

    /// <summary>
    /// Straw dummies in a training yard (KICH-BAN-CAC-MAP 6.2): they stand still, never fight back, drop
    /// nothing and give nothing; enough hits knock one down and it stands up again after a while.
    /// </summary>
    public static class TrainingRules
    {
        public static List<DummyState> NewYard(TrainingConfig training)
        {
            var yard = new List<DummyState>(training.Dummies.Count);

            foreach (MapDummy dummy in training.Dummies)
            {
                yard.Add(new DummyState(dummy.X));
            }

            return yard;
        }

        public static CombatBox Box(TrainingConfig training, double x)
        {
            double half = training.Width / 2d;
            return new CombatBox(x - half, 0d, x + half, training.Height);
        }

        /// <summary>The standing dummy nearest to x, which a player's blows aim at; -1 when all are down.</summary>
        public static int Nearest(IReadOnlyList<DummyState> yard, double x)
        {
            int nearest = -1;

            for (int index = 0; index < yard.Count; index++)
            {
                if (!yard[index].Fallen && (nearest < 0 || Math.Abs(yard[index].X - x) < Math.Abs(yard[nearest].X - x)))
                {
                    nearest = index;
                }
            }

            return nearest;
        }

        /// <summary>Health left after this many blows since the dummy last stood up.</summary>
        public static int Hp(TrainingConfig training, int hits)
        {
            return Math.Max(0, training.MaxHp - hits * training.DamagePerHit);
        }

        /// <summary>One blow landed. Returns true when it knocked the dummy down.</summary>
        public static bool Hit(DummyState dummy, TrainingConfig training)
        {
            if (dummy.Fallen)
            {
                return false;
            }

            dummy.Hits++;
            dummy.TicksSinceChange = 0;

            if (Hp(training, dummy.Hits) > 0)
            {
                return false;
            }

            dummy.Fallen = true;
            dummy.TicksLeft = Math.Max(1, Ticks.FromSeconds(training.RespawnSec));
            return true;
        }

        /// <summary>One tick passes. Returns true when a fallen dummy stood up.</summary>
        public static bool Step(DummyState dummy)
        {
            dummy.TicksSinceChange++;

            if (!dummy.Fallen || --dummy.TicksLeft > 0)
            {
                return false;
            }

            dummy.Fallen = false;
            dummy.TicksLeft = 0;
            dummy.Hits = 0;
            dummy.TicksSinceChange = 0;
            return true;
        }
    }

    public enum Weather : byte
    {
        Clear = 0,
        Rain = 1,
        Fog = 2,
        Wind = 3,
    }

    /// <summary>
    /// The weather of a channel: only a picture, the same for everyone in it. It holds for a while
    /// between the server's weatherMinSec and weatherMaxSec, then a new one is drawn evenly.
    /// </summary>
    public sealed class WeatherState
    {
        private static readonly Weather[] Kinds = { Weather.Clear, Weather.Rain, Weather.Fog, Weather.Wind };

        private readonly Random random;
        private readonly int minTicks;
        private readonly int maxTicks;

        public WeatherState(int seed, double minSec, double maxSec)
        {
            random = new Random(seed);
            minTicks = Math.Max(1, Ticks.FromSeconds(minSec));
            maxTicks = Math.Max(minTicks, Ticks.FromSeconds(maxSec));
            Now = Weather.Clear;
            TicksLeft = Draw();
        }

        public Weather Now { get; private set; }
        public int TicksLeft { get; private set; }

        /// <summary>Returns true when the weather changed this tick.</summary>
        public bool Step()
        {
            if (--TicksLeft > 0)
            {
                return false;
            }

            Weather before = Now;
            Now = Kinds[random.Next(Kinds.Length)];
            TicksLeft = Draw();
            return Now != before;
        }

        private int Draw() => random.Next(minTicks, maxTicks + 1);
    }
}
