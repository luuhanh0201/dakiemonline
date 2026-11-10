using Game.Rules;

namespace Game.Net.Tests;

internal static class TestConfig
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "config");

    /// <summary>The real config files, with each (find, replace) applied to whichever file contains it.</summary>
    public static GameConfig Load(params (string Find, string Replace)[] changes)
    {
        var files = GameConfig.FileNames.ToDictionary(name => name, name => File.ReadAllText(Path.Combine(Dir, name)));

        foreach ((string find, string replace) in changes)
        {
            string name = files.Keys.Single(key => files[key].Contains(find));
            files[name] = files[name].Replace(find, replace);
        }

        return GameConfig.Parse(files);
    }

    public static ServerConfig Server(int channels = 2, int players = 20, double holdSec = 30d, int snapshotEvery = 2, int maxQueued = 6, bool testRoom = true)
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
            PlayerRespawnSec = 5d,
            BossRespawnSec = 10d,
            TestRoom = testRoom,
        };
    }
}
