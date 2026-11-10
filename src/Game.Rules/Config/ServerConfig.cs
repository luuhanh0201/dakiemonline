namespace Game.Rules
{
    /// <summary>Numbers the server runs channels by. Read by <see cref="GameConfig.ParseServer"/>.</summary>
    public sealed class ServerConfig : ConfigEntry
    {
        public int Channels { get; set; }
        public int PlayersPerChannel { get; set; }
        public double SpawnX { get; set; }

        /// <summary>A snapshot goes out every this many ticks.</summary>
        public int SnapshotEveryTicks { get; set; }

        /// <summary>How long a dropped player stays in the channel, waiting to reconnect.</summary>
        public double ReconnectHoldSec { get; set; }

        /// <summary>Inputs beyond this many waiting are dropped, oldest first, so lag cannot pile up.</summary>
        public int MaxQueuedInputs { get; set; }

        /// <summary>A dead player comes back at the spawn point after this long.</summary>
        public double PlayerRespawnSec { get; set; }

        /// <summary>A beaten boss comes back with full health after this long.</summary>
        public double BossRespawnSec { get; set; }

        /// <summary>The test room allows the refill button; real play never does.</summary>
        public bool TestRoom { get; set; }
    }
}
