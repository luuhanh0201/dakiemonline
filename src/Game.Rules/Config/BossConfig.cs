using System;
using System.Collections.Generic;

namespace Game.Rules
{
    public enum BossAttackShape
    {
        /// <summary>A box in front of the boss.</summary>
        Front,

        /// <summary>A low box to one side, then the other.</summary>
        BothSides,

        /// <summary>A box centred on where the player stood when the warning began.</summary>
        Column,
    }

    public sealed class BossAttackConfig : ConfigEntry
    {
        /// <summary>Shown on the end screen.</summary>
        public string Name { get; set; } = string.Empty;

        public BossAttackShape Shape { get; set; }
        public double WarningSec { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public int Damage { get; set; }
        public double RestSec { get; set; }

        /// <summary>Distance the boss walks to before starting; 0 means it attacks from anywhere.</summary>
        public double ApproachRange { get; set; }

        /// <summary>Gap between the two sides of a both-sides attack.</summary>
        public double SideDelaySec { get; set; }
    }

    public sealed class BossConfig : ConfigEntry
    {
        public int MaxHp { get; set; }
        public decimal Defense { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double MoveSpeed { get; set; }
        public double StartDistance { get; set; }
        public double EnrageHpPercent { get; set; }

        /// <summary>Rest time kept below the enrage health, as a percent of normal.</summary>
        public double EnrageRestPercent { get; set; }

        public IReadOnlyList<BossAttackConfig> Cycle { get; set; } = Array.Empty<BossAttackConfig>();
    }
}
