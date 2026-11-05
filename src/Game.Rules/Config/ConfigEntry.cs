namespace Game.Rules
{
    public enum ConfigStatus
    {
        Thu,
        Chot,
        DeXuat,
    }

    /// <summary>Every config entry says what it is, how final it is and where its numbers come from.</summary>
    public abstract class ConfigEntry
    {
        public string Id { get; set; } = string.Empty;
        public ConfigStatus Status { get; set; }
        public string Source { get; set; } = string.Empty;
    }
}
