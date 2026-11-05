using System;

namespace Game.Rules
{
    public static class SkillDamage
    {
        /// <summary>S = (base + attack × coefficient) × [1 + levelPercent% × (level − 1)].</summary>
        public static decimal BeforeDefense(
            decimal baseDamage,
            decimal attack,
            decimal attackCoefficient,
            decimal levelPercent,
            int level)
        {
            if (level < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(level), level, "Skill level starts at 1.");
            }

            return (baseDamage + attack * attackCoefficient) * (1m + levelPercent / 100m * (level - 1));
        }

        /// <summary>Damage taken = S × 100 / (100 + defense), rounded down.</summary>
        public static int AfterDefense(decimal damageBeforeDefense, decimal defense)
        {
            if (defense < 0m)
            {
                throw new ArgumentOutOfRangeException(nameof(defense), defense, "Defense cannot be negative.");
            }

            return (int)Math.Floor(damageBeforeDefense * 100m / (100m + defense));
        }
    }
}
