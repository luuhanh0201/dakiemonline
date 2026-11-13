namespace Game.Rules
{
    /// <summary>Numbers the server runs channels by. Read by <see cref="GameConfig.ParseServer"/>.</summary>
    public sealed class ServerConfig : ConfigEntry
    {
        /// <summary>Which game server this is. A character belongs to one account on one server number.</summary>
        public int ServerNumber { get; set; }

        /// <summary>The name the server-select screen shows.</summary>
        public string ServerName { get; set; } = string.Empty;

        /// <summary>Marks the server "MỚI" on the server-select screen.</summary>
        public bool ServerIsNew { get; set; }

        public int Channels { get; set; }
        public int PlayersPerChannel { get; set; }
        public double SpawnX { get; set; }

        /// <summary>The map id a new character first appears on; the test map unless server.json says otherwise.</summary>
        public string StartMap { get; set; } = "map.test";

        /// <summary>The weather of a channel holds at least this long, and at most WeatherMaxSec, before it changes.</summary>
        public double WeatherMinSec { get; set; } = 480d;

        public double WeatherMaxSec { get; set; } = 900d;

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
