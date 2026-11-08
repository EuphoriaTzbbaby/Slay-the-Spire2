namespace SpireCompanion.Core;

public static class ReplyFreshness
{
    // Hover links, animated positions and disposable UI nodes aren't new native offers.
    // Keep all gameplay fields and the screen identity, so real changes still invalidate advice.
    private static GameSnapshot Semantic(GameSnapshot snapshot) => snapshot.Room is { } room &&
        room.Options.Any(option => !option.Id.StartsWith("ui:", StringComparison.Ordinal))
        ? snapshot with { Room = room with { Options = room.Options.Where(option => !option.Id.StartsWith("ui:", StringComparison.Ordinal)).ToArray() } }
        : snapshot;

    public static bool Same(GameSnapshot before, GameSnapshot after) => Semantic(before).Fingerprint == Semantic(after).Fingerprint;
}
