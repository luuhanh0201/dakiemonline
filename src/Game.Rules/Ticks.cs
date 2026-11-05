using System;

namespace Game.Rules
{
    public static class Ticks
    {
        public const int PerSecond = 30;
        public const double DeltaSeconds = 1d / PerSecond;

        private const double RoundingTolerance = 1e-9;

        public static int FromSeconds(double seconds)
        {
            return (int)Math.Ceiling(seconds * PerSecond - RoundingTolerance);
        }
    }
}
