using System.Collections.Generic;

namespace Game.Rules
{
    public sealed class CharacterConfig : ConfigEntry
    {
        public int MaxHp { get; set; }
        public int MaxEnergy { get; set; }
        public double EnergyRegenPerSec { get; set; }
        public int MaxStamina { get; set; }
        public decimal Attack { get; set; }
        public decimal Defense { get; set; }

        /// <summary>Levels of the skills that have been raised; a skill not listed is level 1.</summary>
        public IReadOnlyDictionary<SkillSlot, int> SkillLevels { get; set; } = new Dictionary<SkillSlot, int>();

        public int LevelOf(SkillSlot slot)
        {
            return SkillLevels.TryGetValue(slot, out int level) ? level : 1;
        }
    }
}
