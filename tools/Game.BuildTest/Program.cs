using System.Globalization;
using System.Text;
using Game.BuildTest;
using Game.Rules;

// Compares the builds in config/build-test.json and prints a Markdown report.
// Usage: dotnet run --project tools/Game.BuildTest -- [--config <dir>] [--out <file.md>]
string configDir = Argument("--config") ?? FindConfig();
string? outFile = Argument("--out");

try
{
    GameConfig config = GameConfig.Parse(GameConfig.FileNames.ToDictionary(name => name, name => Read(name)));
    ProgressionConfig prices = config.ParseProgression(Read(GameConfig.ProgressionFile));
    BuildTestConfig test = config.ParseBuildTest(Read(GameConfig.BuildTestFile), prices);

    List<SetResult> sets = test.Sets.Select(set => Trials.RunSet(config, prices, set, test.Trials)).ToList();
    string report = Report.Write(test, sets, DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));

    Console.OutputEncoding = Encoding.UTF8;
    Console.Write(report);

    if (outFile != null)
    {
        File.WriteAllText(outFile, report, new UTF8Encoding(false));
        Console.Error.WriteLine($"Wrote {outFile}");
    }

    return 0;
}
catch (InvalidConfigException error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

string Read(string name) => File.ReadAllText(Path.Combine(configDir, name));

string? Argument(string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string FindConfig()
{
    foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
    {
        for (DirectoryInfo? dir = new(start); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "config");

            if (File.Exists(Path.Combine(candidate, GameConfig.BuildTestFile)))
            {
                return candidate;
            }
        }
    }

    throw new DirectoryNotFoundException($"no config/ folder with {GameConfig.BuildTestFile} found; pass --config");
}
