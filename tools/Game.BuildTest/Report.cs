using System.Text;
using Game.Rules;

namespace Game.BuildTest;

/// <summary>Writes the trials as a Markdown report in Vietnamese, the project's document language.</summary>
public static class Report
{
    private static readonly (SkillSlot Slot, string Name)[] Skills =
    {
        (SkillSlot.Ranged, "Kiếm khí"),
        (SkillSlot.Support, "Hồi máu"),
        (SkillSlot.OptionA, "Chém tiến"),
        (SkillSlot.OptionB, "Sóng kiếm"),
        (SkillSlot.Ultimate, "Tuyệt kỹ"),
    };

    public static string Write(BuildTestConfig test, IReadOnlyList<SetResult> sets, string date)
    {
        var text = new StringBuilder();
        BuildTrialSettings settings = test.Trials;

        text.AppendLine("# T040: đo các build");
        text.AppendLine();
        text.AppendLine($"{date} · sinh bởi `tools/measure-builds.ps1` từ `config/progression.json` và `config/build-test.json`. Sửa hai file đó rồi chạy lại để có số mới; đừng sửa tay file này.");
        text.AppendLine();
        text.AppendLine($"Mỗi bộ build cùng ngân sách được so riêng. Một bộ đạt khi mỗi build đứng đầu ít nhất {settings.MinTrialsLedByEachBuild} bài{(settings.OneBuildMayLeadEveryTrial ? string.Empty : " và không build nào đứng đầu mọi bài")}. Chỉ tính đứng đầu khi hơn hạng nhì ít nhất {Trials.Number(settings.MinLeadPercent)}%; gần hơn thế là hòa, không ai được tính.");
        text.AppendLine();
        text.AppendLine("## Tóm tắt");
        text.AppendLine();
        text.AppendLine("| Bộ | Kết luận | Số bài đứng đầu |");
        text.AppendLine("| --- | --- | --- |");

        foreach (SetResult set in sets)
        {
            string led = string.Join("; ", set.Builds.Select(build => $"{build.Plan.Name}: {set.Verdict.TrialsLed[build.Plan.Id]}/{set.Trials.Count}"));
            text.AppendLine($"| {set.Set.Name} | {(set.Verdict.Passed ? "ĐẠT" : "CHƯA ĐẠT")} | {led} |");
        }

        text.AppendLine();
        text.AppendLine("Build đứng đầu từng bài (trong ngoặc là mức hơn hạng nhì):");
        text.AppendLine();
        text.AppendLine("| Bài | " + string.Join(" | ", sets.Select(set => set.Set.Name)) + " |");
        text.AppendLine("| --- | " + string.Join(" | ", sets.Select(_ => "---")) + " |");

        foreach (Trial first in sets[0].Trials)
        {
            IEnumerable<string> cells = sets.Select(set => Leader(set, set.Trials.Single(trial => trial.Id == first.Id)));
            text.AppendLine($"| {first.Name} | " + string.Join(" | ", cells) + " |");
        }

        foreach (SetResult set in sets)
        {
            WriteSet(text, set);
        }

        return text.ToString();
    }

    private static void WriteSet(StringBuilder text, SetResult set)
    {
        Dictionary<string, string> names = set.Builds.ToDictionary(build => build.Plan.Id, build => build.Plan.Name);

        text.AppendLine();
        text.AppendLine($"## Bộ: {set.Set.Name}");
        text.AppendLine();
        text.AppendLine($"Kết luận: **{(set.Verdict.Passed ? "ĐẠT" : "CHƯA ĐẠT")}**.");

        foreach (string reason in set.Verdict.Reasons)
        {
            text.AppendLine($"- {reason}");
        }

        text.AppendLine();
        text.AppendLine("| Build | TN chỉ số | TN kỹ năng | Tổng | Tỷ lệ chỉ số/kỹ năng | Công | Sinh lực | Thủ | Năng lượng | " + string.Join(" | ", Skills.Select(skill => skill.Name)) + " |");
        text.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- | " + string.Join(" | ", Skills.Select(_ => "---")) + " |");

        foreach (BuiltCharacter build in set.Builds)
        {
            CharacterConfig character = build.Character;
            double statShare = build.Cost == 0 ? 0d : 100d * build.StatCost / build.Cost;
            text.AppendLine(
                $"| {build.Plan.Name} | {Trials.Number(build.StatCost)} | {Trials.Number(build.SkillCost)} | {Trials.Number(build.Cost)} | {Trials.Number(statShare)}/{Trials.Number(100d - statShare)} "
                + $"| {Trials.Number((double)character.Attack)} | {Trials.Number(character.MaxHp)} | {Trials.Number((double)character.Defense)} | {Trials.Number(character.MaxEnergy)} | "
                + string.Join(" | ", Skills.Select(skill => $"cấp {character.LevelOf(skill.Slot)}")) + " |");
        }

        foreach (Trial trial in set.Trials)
        {
            text.AppendLine();
            text.AppendLine($"### {trial.Name}");
            text.AppendLine();
            text.AppendLine(trial.Note);
            text.AppendLine();
            text.AppendLine("| Build | " + string.Join(" | ", trial.Columns) + " |");
            text.AppendLine("| --- | " + string.Join(" | ", trial.Columns.Select(_ => "---")) + " |");

            foreach (TrialRow row in trial.Rows)
            {
                string mark = row.BuildId == trial.LeaderId ? " ★" : string.Empty;
                text.AppendLine($"| {names[row.BuildId]}{mark} | " + string.Join(" | ", row.Cells) + " |");
            }
        }
    }

    private static string Leader(SetResult set, Trial trial)
    {
        string who = trial.LeaderId == null ? "hòa" : set.Builds.Single(build => build.Plan.Id == trial.LeaderId).Plan.Name;
        return $"{who} ({Lead(trial)})";
    }

    /// <summary>How far ahead the best build is, in words: a percent, a different outcome, or level.</summary>
    private static string Lead(Trial trial)
    {
        double lead = trial.LeadPercent;

        if (lead == 0d)
        {
            return "bằng nhau";
        }

        return double.IsPositiveInfinity(lead) ? "khác hẳn kết quả" : $"{Trials.Number(lead, 1)}%";
    }
}
