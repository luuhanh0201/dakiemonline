namespace Game.Rules
{
    public sealed class PotionConfig : ConfigEntry
    {
        public int HealHp { get; set; }

        /// <summary>Bottles available per fight.</summary>
        public int Charges { get; set; }

        public double CooldownSec { get; set; }
    }
}
