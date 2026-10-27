using Godot;
using MegaCrit.Sts2.Core.Modding;
using System.Runtime.CompilerServices;

namespace SpireCompanion;

[ModInitializer(nameof(Initialize))]
public static class Mod
{
    public static void Initialize()
    {
        StartPanel();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartPanel()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        // A Func<> return value would be marshalled to Variant and throw: the panel is a Node, not a Variant.
        Callable.From(() => { _ = new CompanionPanel(tree); }).CallDeferred();
        GD.Print("[SpireCompanion] 初始化：默认暂停，F9 显示/隐藏开发面板。");
    }
}
