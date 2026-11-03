namespace SpireCompanion.Core;

/// <summary>Two distinct presses, with held/repeated keys and blocked input ignored.</summary>
public sealed class DoubleTapShortcut(double intervalSeconds = 0.45)
{
    private bool wasDown;
    private double? firstTap;

    public bool Update(bool down, double now, bool allowed = true)
    {
        var pressed = down && !wasDown;
        wasDown = down;
        if (!allowed) { firstTap = null; return false; }
        if (!pressed) return false;
        if (firstTap is { } previous && now >= previous && now - previous <= intervalSeconds)
        {
            firstTap = null;
            return true;
        }
        firstTap = now;
        return false;
    }
}
