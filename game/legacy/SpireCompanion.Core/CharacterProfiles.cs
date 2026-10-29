using System.Reflection;
using System.Text.Json;

namespace SpireCompanion.Core;

/// <summary>A character guide is data, not code: add a JSON file under Characters/ and it loads.</summary>
public sealed record CharacterProfileData(
    string Id, string[] Names, string[] Mechanics, string[] Draft, string[] Combat, string[] Shop);

public static class CharacterCatalog
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private static readonly Lazy<CharacterProfileData[]> BuiltIn = new(LoadBuiltIn);

    /// <summary>Optional user directory; files here override built-in profiles with the same id.</summary>
    public static string? UserDirectory { get; set; }

    public static IReadOnlyList<CharacterProfileData> All()
    {
        var result = new List<CharacterProfileData>(BuiltIn.Value);
        if (!string.IsNullOrWhiteSpace(UserDirectory) && Directory.Exists(UserDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(UserDirectory, "*.json").OrderBy(path => path))
            {
                try
                {
                    var data = Parse(File.ReadAllText(file));
                    if (data == null) continue;
                    result.RemoveAll(item => SameId(item, data.Id));
                    result.Add(data);
                }
                catch (Exception error) { Console.Error.WriteLine($"[SpireCompanion] character file {file}: {error.Message}"); }
            }
        }
        return result;
    }

    public static CharacterProfileData? Parse(string json) => JsonSerializer.Deserialize<CharacterProfileData>(json, Options);

    private static CharacterProfileData[] LoadBuiltIn()
    {
        var result = new List<CharacterProfileData>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            IEnumerable<string> names;
            try { names = assembly.GetManifestResourceNames(); }
            catch { continue; }
            foreach (var name in names.Where(item => item.Contains(".Characters.", StringComparison.Ordinal) && item.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    using var stream = assembly.GetManifestResourceStream(name);
                    if (stream == null) continue;
                    using var reader = new StreamReader(stream);
                    var data = Parse(reader.ReadToEnd());
                    if (data != null && result.All(item => !SameId(item, data.Id))) result.Add(data);
                }
                catch (Exception error) { Console.Error.WriteLine($"[SpireCompanion] character resource {name}: {error.Message}"); }
            }
        }
        return result.OrderBy(item => item.Id).ToArray();
    }

    private static bool SameId(CharacterProfileData left, string id) =>
        string.Equals(left.Id, id, StringComparison.OrdinalIgnoreCase);
}

public static class CharacterGuidance
{
    public static string For(string character)
    {
        var name = character ?? "";
        foreach (var profile in CharacterCatalog.All())
        {
            if (profile.Names.Any(candidate => name.Contains(candidate, StringComparison.OrdinalIgnoreCase)))
                return Render(profile);
        }
        return "";
    }

    private static string Render(CharacterProfileData profile) =>
        $"角色要点（{profile.Id}）：机制：{Join(profile.Mechanics)}；选牌：{Join(profile.Draft)}；" +
        $"战斗：{Join(profile.Combat)}；商店：{Join(profile.Shop)}。";

    private static string Join(IEnumerable<string> values) => string.Join("、", values.Where(value => !string.IsNullOrWhiteSpace(value)));
}
