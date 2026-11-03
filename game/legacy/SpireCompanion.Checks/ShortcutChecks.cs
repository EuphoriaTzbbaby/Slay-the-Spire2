using SpireCompanion.Core;

internal static class ShortcutChecks
{
    public static int Run()
    {
        var key = new DoubleTapShortcut();
        Require(!key.Update(true, 0));
        Require(!key.Update(true, 0.1));
        Require(!key.Update(false, 0.2));
        Require(key.Update(true, 0.3));
        Require(!key.Update(true, 0.4));
        Require(!key.Update(false, 0.5));
        Require(!key.Update(true, 0.6));
        Console.WriteLine("PASS QQ requires two distinct presses, fires once, and resets after triggering");

        key = new DoubleTapShortcut();
        Require(!key.Update(true, 0));
        Require(!key.Update(false, 0.1));
        Require(!key.Update(true, 0.6));
        Require(!key.Update(false, 0.7));
        Require(key.Update(true, 0.8));
        Console.WriteLine("PASS A slow pair does not trigger QQ; a fresh fast pair does");

        key = new DoubleTapShortcut();
        Require(!key.Update(true, 0));
        Require(!key.Update(false, 0.05));
        Require(!key.Update(true, 0.1, allowed: false));
        Require(!key.Update(true, 0.2));
        Require(!key.Update(false, 0.25));
        Require(!key.Update(true, 0.3));
        Require(!key.Update(false, 0.35));
        Require(key.Update(true, 0.4));
        Console.WriteLine("PASS Text entry, modifiers or focus loss discard partial QQ and do not count a held key");
        return 3;
    }

    private static void Require(bool condition)
    { if (!condition) throw new Exception("Shortcut assertion failed"); }
}
