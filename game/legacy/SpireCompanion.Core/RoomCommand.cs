using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpireCompanion.Core;

/// <summary>A player's current-screen instruction, never a grant for continuous autoplay.</summary>
public sealed record RoomCommand(GameSnapshot Snapshot, string[] Ids)
{
    public static readonly IReadOnlySet<string> Kinds = new HashSet<string>
        { "Reward", "Rest", "Shop", "Select", "Event", "Map", "Relic", "Rewards", "Treasure" };

    public static bool CanOffer(GameSnapshot snapshot) => snapshot.CombatTurn == "无" &&
        snapshot.Enemies.Length == 0 && !snapshot.CanEndTurn && snapshot.Room is { } room &&
        Kinds.Contains(room.Kind) && room.Options.Any(option => !option.Locked);

    public static RoomCommand Parse(GameSnapshot snapshot, string arguments)
    {
        if (!CanOffer(snapshot)) throw new ArgumentException("当前不是可执行的战斗外界面。");
        using var json = JsonDocument.Parse(arguments);
        var data = json.RootElement;
        string[] ids = data.TryGetProperty("ids", out var list)
            ? list.EnumerateArray().Select(item => item.GetString() ?? "").ToArray()
            : [data.GetProperty("id").GetString() ?? ""];
        if (data.TryGetProperty("id", out var first) && (ids.Length == 0 || first.GetString() != ids[0]))
            throw new ArgumentException("id 必须与 ids 的第一项一致。");
        if (ids.Length is < 1 or > 10 || ids.Distinct().Count() != ids.Length ||
            ids.Any(id => !snapshot.Room!.Options.Any(option => option.Id == id && !option.Locked)))
            throw new ArgumentException("只能选择当前界面可用且不重复的选项。");
        var room = snapshot.Room!;
        var required = room.Kind == "Select" && !ids.Contains("confirm") ? Math.Max(1, room.Required) : 1;
        if (ids.Length != required) throw new ArgumentException($"这次需要选择 {required} 项。");
        return new(snapshot, ids);
    }

    public string? ValidateCurrent(GameSnapshot current, bool available)
    {
        if (!available || !CanOffer(current)) return "当前游戏不可操作，或已进入战斗。";
        if (Snapshot.Fingerprint != current.Fingerprint) return "界面或选项已变化，这条指令未执行；请根据新界面重新下达指令。";
        return null;
    }

    /// <summary>Only explicit imperatives expose the action tool. Advice and quoted examples remain read-only.</summary>
    public static bool IsRequested(string text)
    {
        if (Regex.IsMatch(text, "(?:只|仅).{0,5}(?:分析|建议|解释|回答)|(?:不要|别|先别|不用).{0,4}(?:操作|执行|点击|帮我|替我|选|拿|买)|(?:帮我|替我)\\s*(?:别|不要|不用)|假如|假设|举例|这句话|翻译|复述|以后|下次")) return false;
        const string verbs = "(?:选择|选|挑|拿|买|购买|升级|删|移除|变换|休息|锻造|走|前进|离开|跳过|领取|打开|关闭|确认|点击|点|决定|处理|执行)";
        const string questions = "怎么|如何|应该|要不要|值不值得|为什么|选哪|买哪|买什么|选什么|走哪|去哪里|好不好";
        if (Regex.IsMatch(text, "(?:帮我|替我|你来|我要你|让你|请你).{0,40}" + verbs) &&
            !Regex.IsMatch(text, questions)) return true;
        return Regex.IsMatch(text, "(?:^|[，。；;！!\\n])\\s*(?:(?:这里|这次|现在|直接|先|就|请|给我|你)\\s*)*" + verbs) &&
            !Regex.IsMatch(text, "[？?]|吗|呢|怎么样|哪个好|" + questions);
    }
}

/// <summary>One dispatch per message; keep its receipt even when the model reply is cancelled.</summary>
public sealed class RoomCommandSession(GameSnapshot snapshot,
    Func<RoomCommand, Action<string>, CancellationToken, Task<ActionOutcome>> execute)
{
    private int attempted;
    private string receipt = "";
    public bool HasSubmitted => Volatile.Read(ref receipt).Length > 0;
    public string Receipt => Volatile.Read(ref receipt);

    public async Task<string> ExecuteAsync(string arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RoomCommand command;
        try { command = RoomCommand.Parse(snapshot, arguments); }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return JsonSerializer.Serialize(new { submitted = false, message = "选项参数无效，未执行。" }); }
        if (Interlocked.Exchange(ref attempted, 1) != 0)
            return JsonSerializer.Serialize(new { submitted = false, message = "本条指令已处理，不重复点击，也不继续操作后续界面。", receipt = Receipt });
        var outcome = await execute(command, value => Volatile.Write(ref receipt, value), token).ConfigureAwait(false);
        if (outcome.Submitted) Volatile.Write(ref receipt, outcome.Message);
        return JsonSerializer.Serialize(new { submitted = outcome.Submitted, message = outcome.Message });
    }
}
