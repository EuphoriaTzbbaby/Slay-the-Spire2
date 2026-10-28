using System.Text.RegularExpressions;

namespace SpireCompanion.Core;

/// <summary>A lightweight per-run plan shared by chat and auto decisions.</summary>
public sealed partial class RunPlan
{
    public string Goal { get; private set; } = "";
    public List<string> Needs { get; } = [];
    public List<string> Rules { get; } = [];
    public string LastReview { get; private set; } = "";
    public string Posture { get; private set; } = "";
    public string Instructions { get; private set; } = "";
    public List<string> Preferences { get; } = [];
    public int Floor { get; set; }
    public string RunKey { get; set; } = "";

    public string Describe()
    {
        var parts = new List<string>();
        if (Goal.Length > 0) parts.Add($"目标：{Goal}");
        if (Needs.Count > 0) parts.Add($"需求：{string.Join("、", Needs)}");
        if (Rules.Count > 0) parts.Add($"规则：{string.Join("；", Rules)}");
        if (Posture.Length > 0) parts.Add($"姿态：{Posture}");
        if (Preferences.Count > 0) parts.Add($"玩家偏好：{string.Join("、", Preferences)}");
        if (Instructions.Length > 0) parts.Add($"指示：{Instructions}");
        if (LastReview.Length > 0) parts.Add($"上一条复盘：{LastReview}");
        return string.Join("；", parts);
    }

    public void Clear()
    {
        Goal = "";
        Needs.Clear();
        Rules.Clear();
        LastReview = "";
        Posture = "";
        Instructions = "";
        Preferences.Clear();
    }

    public bool AddPreference(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Preferences.Contains(name)) return false;
        Preferences.Add(name);
        return true;
    }

    public void SetPosture(string posture)
    {
        if (posture.Length > 0) Posture = posture;
    }

    public void SetInstructions(string instructions)
    {
        instructions = instructions.Trim();
        if (instructions.Length == 0) return;
        Instructions = instructions.Length <= 120 ? instructions : instructions[..120];
    }

    public void Restore(string goal, IEnumerable<string> needs, IEnumerable<string> rules, string review)
    {
        Goal = goal ?? "";
        Needs.Clear(); Needs.AddRange(needs.Where(value => !string.IsNullOrWhiteSpace(value)));
        Rules.Clear(); Rules.AddRange(rules.Where(value => !string.IsNullOrWhiteSpace(value)));
        LastReview = review ?? "";
    }

    public void SetReview(string text)
    {
        text = text.Replace("\n", " ").Trim();
        LastReview = text.Length <= 200 ? text : text[..200];
    }

    public bool UpdateFrom(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var before = Describe();
        var goal = GoalPattern().Match(text);
        if (goal.Success) Goal = goal.Groups[1].Value;
        foreach (Match rule in RulePattern().Matches(text))
        {
            var value = rule.Value.Trim('，', '。', '！', '？', ' ');
            if (value.Length > 0 && !Rules.Contains(value)) Rules.Add(value);
        }
        if (text.Contains("激进") || text.Contains("进攻") || text.Contains("贪")) Posture = "激进";
        else if (text.Contains("保守") || text.Contains("稳") || text.Contains("防守") || text.Contains("苟")) Posture = "防守";
        else if (text.Contains("平衡")) Posture = "平衡";
        if (text.Contains("这局") || text.Contains("本局") || text.Contains("记住") || text.Contains("以后"))
            SetInstructions(text);
        var need = NeedPattern().Match(text);
        if (need.Success)
        {
            var value = need.Groups[1].Value.Trim('，', '。', ' ');
            if (value.Length > 0 && !Needs.Contains(value)) Needs.Add(value);
        }
        return before != Describe();
    }

    [GeneratedRegex(@"(?:这局|本局|这盘|目标|打算)[:：]?\s*(?:走|玩|打|用)?\s*([\p{IsCJKUnifiedIdeographs}A-Za-z0-9]{1,8}?流)")]
    private static partial Regex GoalPattern();

    [GeneratedRegex(@"(?:优先|记得|尽量|不要|别)(?:拿|选|删|买|升级|打)[^，。！？\n]{0,12}")]
    private static partial Regex RulePattern();

    [GeneratedRegex(@"(?:缺|需要|补)(?:点|一些)?\s*([\p{IsCJKUnifiedIdeographs}]{2,6})")]
    private static partial Regex NeedPattern();
}
