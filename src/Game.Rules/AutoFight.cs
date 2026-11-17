using System;

namespace Game.Rules
{
    /// <summary>The numbers of fighting a pinned target on its own (docs/KE-HOACH-CHAM-DOI-TUONG.md).</summary>
    public sealed class AutoFightConfig : ConfigEntry
    {
        /// <summary>A skill can stand in for the plain attack only if it comes back within this many seconds.</summary>
        public double MaxCooldownSec { get; set; }

        /// <summary>How far from the target a ranged skill is cast, at most.</summary>
        public double RangedStandOff { get; set; }
    }

    /// <summary>What fighting on its own does this tick: which way to walk and which skill to press.</summary>
    public readonly struct AutoStep
    {
        public AutoStep(double moveX, SkillSlot? skill)
        {
            MoveX = moveX;
            Skill = skill;
        }

        /// <summary>-1 left, 1 right, 0 stand; a small value only turns to face the target.</summary>
        public double MoveX { get; }

        public SkillSlot? Skill { get; }
    }

    public static class AutoFight
    {
        /// <summary>How hard the stick is pushed to turn around without walking off.</summary>
        public const double TurnTilt = 0.2d;

        /// <summary>How much of the plain attack's half width the fighter stands inside.</summary>
        private const double MeleeShare = 0.9d;

        /// <summary>
        /// Whether a skill may stand in for the plain attack: one that comes back quickly. Healing and the
        /// ultimate are cast by hand, as are skills slower than the configured limit.
        /// </summary>
        public static bool CanStandIn(SkillConfig skill, AutoFightConfig config)
        {
            return skill.Slot != SkillSlot.Attack
                && skill.Slot != SkillSlot.Support
                && skill.Slot != SkillSlot.Ultimate
                && skill.CooldownSec > 0d
                && skill.CooldownSec <= config.MaxCooldownSec;
        }

        /// <summary>How close to the target a skill is cast from.</summary>
        public static double Reach(SkillConfig skill, AutoFightConfig config)
        {
            if (skill.Projectile != null)
            {
                return Math.Min(config.RangedStandOff, skill.Projectile.Range);
            }

            return skill.Hitbox == null ? 0d : skill.Hitbox.Width / 2d * MeleeShare;
        }

        /// <summary>
        /// Walks toward the target until in reach, turns to face it, then presses the chosen skill when it is
        /// ready and the plain attack otherwise.
        /// </summary>
        public static AutoStep Decide(double selfX, int facing, double targetX, SkillConfig plain, SkillConfig? chosen, bool chosenReady, AutoFightConfig config)
        {
            SkillConfig skill = chosen != null && chosenReady ? chosen : plain;
            double away = targetX - selfX;
            int side = away >= 0d ? 1 : -1;

            if (Math.Abs(away) > Reach(skill, config))
            {
                return new AutoStep(side, null);
            }

            if (facing != side)
            {
                return new AutoStep(side * TurnTilt, null);
            }

            return new AutoStep(0d, skill.Slot);
        }
    }
}
