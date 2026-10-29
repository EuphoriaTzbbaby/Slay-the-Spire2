namespace SpireCompanion.Core;

/// <summary>One character's mechanics and drafting priorities. Keep one implementation per file so
/// two people editing different characters never touch the same lines.</summary>
public interface ICharacterProfile
{
    bool Matches(string character);
    string Guidance { get; }
}

public static class CharacterGuidance
{
    private static readonly ICharacterProfile[] Profiles =
    [
        new IroncladProfile(),
        new SilentProfile(),
        new DefectProfile()
    ];

    public static string For(string character)
    {
        var name = character ?? "";
        foreach (var profile in Profiles)
            if (profile.Matches(name)) return profile.Guidance;
        return "";
    }
}
