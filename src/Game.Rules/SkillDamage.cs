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

        /// <summary>
        /// Damage of one hit against a target. Multi-hit skills split S by phase;
        /// skills without phases, such as projectiles, deal all of S in one hit.
        /// A combo then scales the hit by its place in the chain.
        /// </summary>
        public static int HitDamage(SkillConfig skill, int phaseIndex, decimal attack, int level, decimal defense, int comboStep = 0)
        {
            decimal total = BeforeDefense(skill.BaseDamage, attack, skill.AttackCoefficient, skill.LevelPercent, level);
            decimal percent = skill.ActivePhases.Count == 0 ? 100m : skill.ActivePhases[phaseIndex].DamagePercent;

            decimal comboPercent = skill.Combo == null ? 100m : skill.Combo.HitDamagePercents[comboStep];

            return AfterDefense(total * percent / 100m * comboPercent / 100m, defense);
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
