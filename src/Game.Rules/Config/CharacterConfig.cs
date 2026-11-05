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
    }
}
