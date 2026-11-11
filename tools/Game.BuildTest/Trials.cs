using System.Globalization;
using Game.Rules;

namespace Game.BuildTest;

/// <summary>A build with its prices worked out and the character it plays as.</summary>
public sealed record BuiltCharacter(BuildPlan Plan, GameConfig Playing, int StatCost, int SkillCost)
{
    public int Cost => StatCost + SkillCost;
    public CharacterConfig Character => Playing.Character;
}

/// <summary>
/// One build's line in a trial. A lower group always beats a higher one, such as a win over a
/// loss; inside a group the value decides, by how much it differs.
/// </summary>
public sealed record TrialRow(string BuildId, IReadOnlyList<string> Cells, int Group, double Value);

/// <summary>
/// One way of comparing the builds. A build leads only when it is ahead of the runner-up by the
/// trial's margin; anything closer counts as a draw, so small differences decide nothing.
/// </summary>
public sealed record Trial(
    string Id,
    string Name,
    string Note,
    IReadOnlyList<string> Columns,
    IReadOnlyList<TrialRow> Rows,
    bool HigherIsBetter,
    double MinLeadPercent)
{
    /// <summary>Rows from best to worst.</summary>
    public IReadOnlyList<TrialRow> Ranked =>
        Rows.OrderBy(row => row.Group).ThenBy(row => HigherIsBetter ? -row.Value : row.Value).ToList();

    /// <summary>
    /// How far the best row is ahead of the runner-up, as a percent of the runner-up's value.
    /// Infinite when the best is alone in its group, or the runner-up scored nothing.
    /// </summary>
    public double LeadPercent
    {
        get
        {
            IReadOnlyList<TrialRow> ranked = Ranked;

            if (ranked.Count < 2 || ranked[0].Group != ranked[1].Group)
            {
                return double.PositiveInfinity;
            }

            double gap = HigherIsBetter ? ranked[0].Value - ranked[1].Value : ranked[1].Value - ranked[0].Value;

            if (gap == 0d)
            {
                return 0d;
            }

            return ranked[1].Value == 0d ? double.PositiveInfinity : 100d * gap / Math.Abs(ranked[1].Value);
        }
    }

    /// <summary>The build that did clearly best, or null when the best are too close to call.</summary>
    public string? LeaderId
    {
        get
        {
            double lead = LeadPercent;
            return lead > 0d && lead >= MinLeadPercent ? Ranked[0].BuildId : null;
        }
    }
}

/// <summary>Whether the builds pass: each leads enough trials and, unless allowed, none leads them all.</summary>
public sealed record Verdict(bool Passed, IReadOnlyDictionary<string, int> TrialsLed, IReadOnlyList<string> Reasons);

/// <summary>Everything measured for one set of builds.</summary>
public sealed record SetResult(BuildSet Set, IReadOnlyList<BuiltCharacter> Builds, IReadOnlyList<Trial> Trials, Verdict Verdict);

public static class Trials
{
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    public static IReadOnlyList<BuiltCharacter> Build(GameConfig config, ProgressionConfig prices, BuildSet set)
    {
        return set.Builds
            .Select(plan => new BuiltCharacter(
                plan,
                config.WithCharacter(Progression.Apply(config.Character, prices, plan)),
                Progression.StatCost(prices, plan),
                Progression.SkillCost(prices, plan)))
            .ToList();
    }

    /// <summary>Builds, plays and judges one set. Builds are only ever compared inside their own set.</summary>
    public static SetResult RunSet(GameConfig config, ProgressionConfig prices, BuildSet set, BuildTrialSettings settings)
    {
        IReadOnlyList<BuiltCharacter> builds = Build(config, prices, set);
        IReadOnlyList<Trial> trials = RunAll(builds, settings);
        return new SetResult(set, builds, trials, Judge(builds, trials, settings));
    }

    public static IReadOnlyList<Trial> RunAll(IReadOnlyList<BuiltCharacter> builds, BuildTrialSettings settings)
    {
        var trials = new List<Trial>();

        foreach (DummyTrial dummy in settings.Dummies)
        {
            trials.Add(Dummy(dummy, builds, settings));
        }

        trials.Add(Endure(builds, settings));
        trials.Add(Fight("boss_stand", "Boss, không né", "Bot đứng đánh, không rời vùng đỏ.", builds, settings, Bot.FightStanding));
        trials.Add(Fight("boss_dodge", "Boss, có né", "Bot bước hoặc nhảy khỏi mọi vùng đỏ; chiêu đứng yên chỉ dùng khi boss nghỉ.", builds, settings, Bot.FightDodging));
        trials.Add(Duel("duel_full", "Đấu tay đôi, đủ năng lượng", builds, settings, startWithEnergy: true));
        trials.Add(Duel("duel_empty", "Đấu tay đôi, hết năng lượng", builds, settings, startWithEnergy: false));
        return trials;
    }

    public static Verdict Judge(IReadOnlyList<BuiltCharacter> builds, IReadOnlyList<Trial> trials, BuildTrialSettings settings)
    {
        Dictionary<string, int> led = builds.ToDictionary(build => build.Plan.Id, build => trials.Count(trial => trial.LeaderId == build.Plan.Id));
        var reasons = new List<string>();

        foreach (BuiltCharacter build in builds)
        {
            int count = led[build.Plan.Id];

            if (count < settings.MinTrialsLedByEachBuild)
            {
                reasons.Add($"{build.Plan.Name} đứng đầu {count} bài, cần ít nhất {settings.MinTrialsLedByEachBuild}.");
            }

            if (!settings.OneBuildMayLeadEveryTrial && count == trials.Count)
            {
                reasons.Add($"{build.Plan.Name} đứng đầu mọi bài.");
            }
        }

        return new Verdict(reasons.Count == 0, led, reasons);
    }

    private static Trial Dummy(DummyTrial dummy, IReadOnlyList<BuiltCharacter> builds, BuildTrialSettings settings)
    {
        var rows = new List<TrialRow>();

        foreach (BuiltCharacter build in builds)
        {
            DummyResult result = Bot.HitDummy(build.Playing, settings, dummy.Seconds, settings.DummyDefense);
            rows.Add(new TrialRow(
                build.Plan.Id,
                new[]
                {
                    Number(result.TotalDamage),
                    Number(result.DamagePerSecond, 1),
                    result.EnergyShortAtSec == null ? "không thiếu" : $"giây {Number(result.EnergyShortAtSec.Value, 1)}",
                },
                0,
                result.TotalDamage));
        }

        return new Trial(
            dummy.Id,
            dummy.Name,
            $"Mục tiêu đứng yên, Thủ {Number((double)settings.DummyDefense)}, trong {Number(dummy.Seconds)} giây. Bot bắt đầu đầy năng lượng, đánh liên tục, ra chiêu ngay khi hồi xong và đủ năng lượng. So theo tổng sát thương.",
            new[] { "Tổng sát thương", "Sát thương/giây", "Thiếu năng lượng lần đầu" },
            rows,
            HigherIsBetter: true,
            settings.MinLeadPercent);
    }

    private static Trial Endure(IReadOnlyList<BuiltCharacter> builds, BuildTrialSettings settings)
    {
        var rows = new List<TrialRow>();

        foreach (BuiltCharacter build in builds)
        {
            EndureResult result = Bot.Endure(build.Playing, settings);

            // Outlasting the trial beats falling at any time; among the fallen, longer is better.
            rows.Add(new TrialRow(
                build.Plan.Id,
                new[]
                {
                    result.Survived ? "Trụ hết giờ" : "Gục",
                    Number(result.Seconds, 1),
                    Number(result.HitsTaken),
                    Number(result.PotionsUsed),
                },
                result.Survived ? 0 : 1,
                result.Seconds));
        }

        string note = $"Bot đứng trong tầm boss, không đánh trả, không né. Hồi máu khi dưới {Number(settings.HealBelowHpPercent)}% máu, uống thuốc khi dưới {Number(settings.PotionBelowHpPercent)}%; trụ quá {Number(settings.FightMaxSec)} giây thì dừng. So theo thời gian trụ được. Boss đánh từng đòn lớn nên thời gian nhảy theo số đòn chịu nổi.";
        return new Trial(
            "endure",
            "Trụ đòn",
            note,
            new[] { "Kết quả", "Thời gian trụ (giây)", "Đòn đã chịu", "Thuốc đã dùng" },
            rows,
            HigherIsBetter: true,
            settings.MinLeadPercent);
    }

    private static Trial Fight(
        string id,
        string name,
        string how,
        IReadOnlyList<BuiltCharacter> builds,
        BuildTrialSettings settings,
        Func<GameConfig, BuildTrialSettings, FightResult> play)
    {
        var rows = new List<TrialRow>();

        foreach (BuiltCharacter build in builds)
        {
            FightResult result = play(build.Playing, settings);
            string outcome = result.Won ? "Thắng" : result.TimedOut ? "Hết giờ" : "Thua";

            // A win beats anything else. Winners compare by time, the rest by the health they left the boss.
            rows.Add(new TrialRow(
                build.Plan.Id,
                new[]
                {
                    outcome,
                    Number(result.Seconds, 1),
                    Number(result.BossHpLeft),
                    Number(result.PlayerHpLeft),
                    Number(result.HitsTaken),
                    Number(result.PotionsUsed),
                },
                result.Won ? 0 : 1,
                result.Won ? result.Seconds : result.BossHpLeft));
        }

        string note = $"{how} Hồi máu khi dưới {Number(settings.HealBelowHpPercent)}% máu, uống thuốc khi dưới {Number(settings.PotionBelowHpPercent)}%; quá {Number(settings.FightMaxSec)} giây tính là hết giờ. Thắng luôn xếp trên không thắng; cùng thắng thì so thời gian, cùng không thắng thì so máu boss còn lại.";
        return new Trial(
            id,
            name,
            note,
            new[] { "Kết quả", "Thời gian (giây)", "Máu boss còn", "Máu mình còn", "Đòn bị trúng", "Thuốc đã dùng" },
            rows,
            HigherIsBetter: false,
            settings.MinLeadPercent);
    }

    /// <summary>
    /// Not a real fight: each build hits a dummy that has the other's defence, and whoever would
    /// bring the other's full health down clearly sooner wins. Dodging, potions and healing are left out.
    /// </summary>
    private static Trial Duel(string id, string name, IReadOnlyList<BuiltCharacter> builds, BuildTrialSettings settings, bool startWithEnergy)
    {
        var rows = new List<TrialRow>();

        foreach (BuiltCharacter build in builds)
        {
            int wins = 0;
            var cells = new List<string>();

            foreach (BuiltCharacter other in builds.Where(other => other != build))
            {
                double? mine = SecondsToBeat(build, other, settings, startWithEnergy);
                double? theirs = SecondsToBeat(other, build, settings, startWithEnergy);
                bool won = ClearlySooner(mine, theirs, settings.MinLeadPercent);
                bool lost = ClearlySooner(theirs, mine, settings.MinLeadPercent);
                wins += won ? 1 : 0;
                cells.Add($"{(won ? "thắng" : lost ? "thua" : "hòa")} {other.Plan.Name}: hạ sau {Seconds(mine)}, bị hạ sau {Seconds(theirs)}");
            }

            rows.Add(new TrialRow(build.Plan.Id, new[] { Number(wins), string.Join("; ", cells) }, 0, wins));
        }

        string energy = startWithEnergy ? "đầy năng lượng" : "0 năng lượng";
        string note = $"Tính, không phải đánh thật: mỗi build đánh cọc gỗ có Thủ của đối thủ, bắt đầu với {energy}; ai gây đủ sát thương bằng máu tối đa của đối thủ sớm hơn ít nhất {Number(settings.MinLeadPercent)}% thì thắng, gần hơn thế là hòa. Không tính né, thuốc và hồi máu. Đứng đầu là build thắng nhiều trận nhất.";

        // The margin is already applied to each pairing, so one more win is enough to lead.
        return new Trial(id, name, note, new[] { "Số trận thắng", "Chi tiết" }, rows, HigherIsBetter: true, MinLeadPercent: 0d);
    }

    private static bool ClearlySooner(double? mine, double? theirs, double minLeadPercent)
    {
        if (mine == null)
        {
            return false;
        }

        return theirs == null || (mine < theirs && 100d * (theirs.Value - mine.Value) / theirs.Value >= minLeadPercent);
    }

    private static double? SecondsToBeat(BuiltCharacter attacker, BuiltCharacter defender, BuildTrialSettings settings, bool startWithEnergy)
    {
        DummyResult result = Bot.HitDummy(attacker.Playing, settings, settings.FightMaxSec, defender.Character.Defense, startWithEnergy);
        return result.SecondsToDeal(defender.Character.MaxHp);
    }

    private static string Seconds(double? seconds) => seconds == null ? "không kịp" : $"{Number(seconds.Value, 1)} giây";

    public static string Number(double value, int decimals = 0) => value.ToString("N" + decimals, Vietnamese);
}
