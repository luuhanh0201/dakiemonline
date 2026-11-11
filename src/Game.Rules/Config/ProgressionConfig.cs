using System.Collections.Generic;
using System.Linq;

namespace Game.Rules
{
    /// <summary>The character numbers TN can be spent on in the build test.</summary>
    public enum CharacterStat
    {
        Attack,
        MaxHp,
        Defense,
        MaxEnergy,
    }

    /// <summary>What one step of a stat gives and costs. Step k costs FirstCost + (k − 1) × CostIncrease.</summary>
    public sealed class StatPrice
    {
        public CharacterStat Stat { get; set; }

        /// <summary>How much the stat grows with each step bought.</summary>
        public int PerStep { get; set; }

        public int FirstCost { get; set; }
        public int CostIncrease { get; set; }
    }

    /// <summary>
    /// What raising a skill costs. Level 1 is free; the step to level 2 costs FirstCost and each
    /// later one CostIncrease more. How much a level adds is the skill's own levelPercent.
    /// </summary>
    public sealed class SkillPrice
    {
        public string SkillId { get; set; } = string.Empty;
        public SkillSlot Slot { get; set; }
        public int MaxLevel { get; set; }
        public int FirstCost { get; set; }
        public int CostIncrease { get; set; }
    }

    /// <summary>The price list of <see cref="GameConfig.ProgressionFile"/>.</summary>
    public sealed class ProgressionConfig : ConfigEntry
    {
        public IReadOnlyList<StatPrice> Stats { get; set; } = new List<StatPrice>();
        public IReadOnlyList<SkillPrice> Skills { get; set; } = new List<SkillPrice>();

        public StatPrice Stat(CharacterStat stat) => Stats.First(price => price.Stat == stat);

        /// <summary>The price of a skill, or null when the skill has no levels, like the basic attack.</summary>
        public SkillPrice? Skill(SkillSlot slot) => Skills.FirstOrDefault(price => price.Slot == slot);
    }

    /// <summary>How one character spent TN: steps bought per stat and the level of each skill.</summary>
    public sealed class BuildPlan
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        /// <summary>Steps bought; a stat not listed has none.</summary>
        public IReadOnlyDictionary<CharacterStat, int> StatSteps { get; set; } = new Dictionary<CharacterStat, int>();

        /// <summary>Skill levels; a skill not listed is level 1.</summary>
        public IReadOnlyDictionary<SkillSlot, int> SkillLevels { get; set; } = new Dictionary<SkillSlot, int>();
    }

    /// <summary>One run against the training dummy; short ones show burst, long ones show what energy allows.</summary>
    public sealed class DummyTrial
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public double Seconds { get; set; }
    }

    /// <summary>How the build test plays its trials. Every number can be changed without touching the tool.</summary>
    public sealed class BuildTrialSettings
    {
        /// <summary>The runs against the training dummy, in the order they are reported.</summary>
        public IReadOnlyList<DummyTrial> Dummies { get; set; } = new List<DummyTrial>();

        public decimal DummyDefense { get; set; }

        /// <summary>A build leads a trial only when it beats the runner-up by at least this much; closer than that is a draw.</summary>
        public double MinLeadPercent { get; set; }

        /// <summary>A boss fight still running after this long counts as lost.</summary>
        public double FightMaxSec { get; set; }

        /// <summary>The bot drinks a potion below this share of max health.</summary>
        public double PotionBelowHpPercent { get; set; }

        /// <summary>The bot casts its heal below this share of max health.</summary>
        public double HealBelowHpPercent { get; set; }

        public double PlayerStartX { get; set; }

        /// <summary>Each build must come first in at least this many trials.</summary>
        public int MinTrialsLedByEachBuild { get; set; }

        /// <summary>False means a build that comes first in every trial fails the test.</summary>
        public bool OneBuildMayLeadEveryTrial { get; set; }
    }

    /// <summary>Builds that spent the same budget and are compared with each other.</summary>
    public sealed class BuildSet
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int BudgetTn { get; set; }
        public IReadOnlyList<BuildPlan> Builds { get; set; } = new List<BuildPlan>();
    }

    /// <summary>The sets of builds to compare and how, from <see cref="GameConfig.BuildTestFile"/>.</summary>
    public sealed class BuildTestConfig : ConfigEntry
    {
        public BuildTrialSettings Trials { get; set; } = new BuildTrialSettings();

        /// <summary>Each set is judged on its own; builds are never compared across sets.</summary>
        public IReadOnlyList<BuildSet> Sets { get; set; } = new List<BuildSet>();

        public BuildSet Set(string id) => Sets.First(set => set.Id == id);
    }
}
