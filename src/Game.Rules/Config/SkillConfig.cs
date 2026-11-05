using System;
using System.Collections.Generic;

namespace Game.Rules
{
    /// <summary>The six combat slots of GDD 7.2.</summary>
    public enum SkillSlot
    {
        Attack,
        Ranged,
        Support,
        Ultimate,
        OptionA,
        OptionB,
    }

    public sealed class SkillPhase
    {
        public double DurationSec { get; set; }
        public decimal DamagePercent { get; set; }
    }

    public sealed class SkillHitbox
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Pierce { get; set; }
    }

    public sealed class SkillProjectile
    {
        public double Speed { get; set; }
        public double Range { get; set; }
        public double Width { get; set; }
    }

    public sealed class SkillHeal
    {
        public decimal Base { get; set; }
        public decimal MaxHpPercent { get; set; }
    }

    public sealed class SkillSlow
    {
        public double Percent { get; set; }
        public double DurationSec { get; set; }
    }

    public sealed class SkillConfig : ConfigEntry
    {
        public SkillSlot Slot { get; set; }
        public double CooldownSec { get; set; }
        public int EnergyCost { get; set; }
        public decimal BaseDamage { get; set; }
        public decimal AttackCoefficient { get; set; }

        /// <summary>Damage gained per skill level; 0 where the value is still open (GDD 8.3).</summary>
        public decimal LevelPercent { get; set; }

        public double WindupSec { get; set; }
        public double RecoverySec { get; set; }

        /// <summary>Speed kept while casting, as a percent of normal speed; 0 roots the character.</summary>
        public double MoveSpeedPercent { get; set; }

        public bool AllowedInAir { get; set; }
        public double DashDistance { get; set; }
        public IReadOnlyList<SkillPhase> ActivePhases { get; set; } = Array.Empty<SkillPhase>();
        public SkillHitbox? Hitbox { get; set; }
        public SkillProjectile? Projectile { get; set; }
        public SkillHeal? Heal { get; set; }
        public SkillSlow? Slow { get; set; }

        public bool DealsDamage => BaseDamage > 0m || AttackCoefficient > 0m;
    }
}
