using Game.Rules;

namespace Game.Net.Tests;

internal static class TestConfig
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "config");

    public static GameConfig Load()
    {
        return GameConfig.Parse(GameConfig.FileNames.ToDictionary(name => name, name => File.ReadAllText(Path.Combine(Dir, name))));
    }

    public static ServerConfig Server(int channels = 2, int players = 20, double holdSec = 30d, int snapshotEvery = 2, int maxQueued = 6)
    {
        return new ServerConfig
        {
            Id = "server.test",
            Status = ConfigStatus.Thu,
            Source = "test",
            Channels = channels,
            PlayersPerChannel = players,
            SpawnX = 5d,
            SnapshotEveryTicks = snapshotEvery,
            ReconnectHoldSec = holdSec,
            MaxQueuedInputs = maxQueued,
        };
    }
}
