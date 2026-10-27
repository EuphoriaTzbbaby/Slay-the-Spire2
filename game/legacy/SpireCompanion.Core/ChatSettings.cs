using System.Text.Json;

namespace SpireCompanion.Core;

public sealed record ChatSettings(string Endpoint = "https://api.deepseek.com", string Model = "deepseek-flash", string TalkMode = "normal", int TimeoutSeconds = 45, bool RememberApiKey = true)
{
    public Uri RequestUri()
    {
        if (!Uri.TryCreate(Endpoint.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("请填写有效的 HTTP(S) 模型地址，不要在地址中填写密钥或查询参数。");
        if (string.IsNullOrWhiteSpace(Model)) throw new ArgumentException("请填写模型名称。");
        if (TalkMode is not ("quiet" or "normal" or "chatty")) throw new ArgumentException("发言模式无效。");
        if (TimeoutSeconds is < 5 or > 180) throw new ArgumentException("请求超时应在 5 到 180 秒之间。");
        var path = uri.AbsoluteUri.TrimEnd('/');
        return new Uri(path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase) ? path : path + "/chat/completions");
    }
    public static ChatSettings Load(string path)
    {
        var value = File.Exists(path) ? JsonSerializer.Deserialize<ChatSettings>(File.ReadAllText(path)) ?? new() : new();
        // Migrate previously blank fields; preserve explicitly configured nonempty providers.
        return value with { Endpoint = string.IsNullOrWhiteSpace(value.Endpoint) ? "https://api.deepseek.com" : value.Endpoint,
            Model = string.IsNullOrWhiteSpace(value.Model) ? "deepseek-flash" : value.Model };
    }
    public void Save(string path)
    {
        _ = RequestUri();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
