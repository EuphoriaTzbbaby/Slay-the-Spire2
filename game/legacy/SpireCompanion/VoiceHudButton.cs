using Godot;

namespace SpireCompanion;

internal enum VoiceHudState { Off, Connecting, Listening, Unavailable }

/// <summary>A persistent microphone control. Its state describes capture, never inferred speech activity.</summary>
internal sealed class VoiceHudButton
{
    public Button Button { get; }
    private VoiceHudState? displayed;
    private readonly Dictionary<string, Texture2D?> icons = new();

    public VoiceHudButton(Theme theme, Action toggle)
    {
        Button = new Button
        {
            Name = "VoiceToggle", Theme = theme, CustomMinimumSize = new Vector2(48, 48),
            IconAlignment = HorizontalAlignment.Center, FocusMode = Control.FocusModeEnum.None,
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        Button.Pressed += toggle;
        foreach (var property in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" })
            Button.AddThemeColorOverride(property, Colors.White);
        SetState(VoiceHudState.Off);
    }

    public void SetState(VoiceHudState state)
    {
        if (displayed == state) return;
        displayed = state;
        var (fill, border, icon, text, tooltip) = state switch
        {
            VoiceHudState.Connecting => ("76551D", "E8B75C", "microphone-connecting", "…", "语音连接中，点击或 QQ 关闭"),
            VoiceHudState.Listening => ("166B50", "62D9A7", "microphone", "开", "语音持续监听中，点击或 QQ 关闭"),
            VoiceHudState.Unavailable => ("833F46", "F0A1A5", "microphone-muted", "!", "语音暂不可用，点击关闭后可重试；详情见聊天页"),
            _ => ("29323E", "7B8796", "microphone-muted", "关", "语音已关闭，点击或 QQ 开启持续对话")
        };
        Button.Icon = Icon(icon);
        Button.Text = Button.Icon == null ? text : "";
        Button.TooltipText = tooltip;
        var normal = UiStyle.Box(new Color(fill), new Color(border), 24, 8);
        normal.ShadowColor = new Color(0, 0, 0, 0.22f);
        normal.ShadowSize = 3;
        Button.AddThemeStyleboxOverride("normal", normal);
        Button.AddThemeStyleboxOverride("hover", UiStyle.Box(new Color(fill).Lightened(0.12f), new Color(border), 24, 8));
        Button.AddThemeStyleboxOverride("pressed", UiStyle.Box(new Color(fill).Darkened(0.12f), Colors.White, 24, 8));
        Button.AddThemeStyleboxOverride("focus", UiStyle.Box(new Color(fill), Colors.White, 24, 8));
        foreach (var property in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            Button.AddThemeColorOverride(property, Colors.White);
    }

    private Texture2D? Icon(string name)
    {
        if (icons.TryGetValue(name, out var cached)) return cached;
        using var resource = typeof(VoiceHudButton).Assembly.GetManifestResourceStream($"SpireCompanion.Assets.{name}.svg");
        if (resource == null) return icons[name] = null;
        using var reader = new StreamReader(resource);
        using var image = new Image();
        if (image.LoadSvgFromString(reader.ReadToEnd()) != Error.Ok) return icons[name] = null;
        return icons[name] = ImageTexture.CreateFromImage(image);
    }
}
