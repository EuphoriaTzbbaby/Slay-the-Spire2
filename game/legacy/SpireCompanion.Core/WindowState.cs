using System.Text.Json;

namespace SpireCompanion.Core;

/// <summary>
/// Small window preferences that must survive a restart. Only non-secret UI flags live here;
/// a broken file falls back to defaults instead of blocking the panel.
/// </summary>
public sealed record WindowState(bool AutoEnabled = false, bool SolverAuto = false, bool VoiceEnabled = false)
{
    public static WindowState Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            return JsonSerializer.Deserialize<WindowState>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, path, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Preferences are not worth interrupting play for.
        }
    }
}
