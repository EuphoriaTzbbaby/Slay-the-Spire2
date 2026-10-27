using Godot;

namespace SpireCompanion;

/// <summary>
/// Cream-and-honey palette taken from the project deck, kept in one place so the
/// window, the chat panel and the settings panel cannot drift apart.
/// </summary>
internal static class UiStyle
{
    public static readonly Color Background = new("FFF6E3");
    public static readonly Color CardColor = new("FFFDF7");
    public static readonly Color Border = new("F0C070");
    public static readonly Color Accent = new("F59A23");
    public static readonly Color AccentDeep = new("D97706");
    public static readonly Color Text = new("5A3416");
    public static readonly Color Muted = new("9A7B57");
    public static readonly Color Good = new("4CAF50");

    public static StyleBoxFlat Box(Color fill, Color line, int radius = 12, int padding = 12) => new()
    {
        BgColor = fill,
        BorderColor = line,
        BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
        CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius,
        CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
        ContentMarginLeft = padding, ContentMarginRight = padding,
        ContentMarginTop = padding, ContentMarginBottom = padding
    };

    public static void Apply(Theme theme)
    {
        theme.DefaultFontSize = 18;
        var field = Box(Colors.White, new Color("E8C892"), 10, 9);
        var focus = Box(Colors.White, Accent, 10, 9);
        var readOnly = Box(new Color("FBF3E2"), new Color("E8C892"), 10, 9);
        foreach (var type in new[] { "LineEdit", "TextEdit" })
        {
            theme.SetStylebox("normal", type, field);
            theme.SetStylebox("focus", type, focus);
            theme.SetStylebox("read_only", type, readOnly);
            theme.SetColor("font_color", type, Text);
            theme.SetColor("font_placeholder_color", type, new Color("B99A72"));
            theme.SetColor("font_uneditable_color", type, Muted);
            theme.SetColor("caret_color", type, Text);
        }
        theme.SetStylebox("normal", "OptionButton", field);
        theme.SetStylebox("focus", "OptionButton", focus);
        theme.SetStylebox("hover", "OptionButton", focus);
        theme.SetStylebox("pressed", "OptionButton", Box(new Color("FFF1D6"), Accent, 10, 9));
        theme.SetStylebox("disabled", "OptionButton", Box(new Color("F6EBD8"), new Color("E0CBA6"), 10, 9));
        foreach (var property in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            theme.SetColor(property, "OptionButton", Text);
        theme.SetColor("font_disabled_color", "OptionButton", Muted);

        theme.SetStylebox("normal", "Button", Box(new Color("FFE1A6"), new Color("E8AE4B"), 10, 10));
        theme.SetStylebox("hover", "Button", Box(new Color("FFD08A"), AccentDeep, 10, 10));
        theme.SetStylebox("pressed", "Button", Box(Accent, AccentDeep, 10, 10));
        theme.SetStylebox("focus", "Button", Box(new Color("FFE1A6"), Accent, 10, 10));
        theme.SetStylebox("disabled", "Button", Box(new Color("F3E7D2"), new Color("E0CBA6"), 10, 10));
        theme.SetColor("font_color", "Button", Text);
        theme.SetColor("font_hover_color", "Button", Text);
        theme.SetColor("font_pressed_color", "Button", Colors.White);
        theme.SetColor("font_disabled_color", "Button", Muted);

        theme.SetStylebox("tab_selected", "TabBar", Box(Accent, AccentDeep, 10, 10));
        theme.SetStylebox("tab_unselected", "TabBar", Box(new Color("FFEBC4"), new Color("E8C892"), 10, 10));
        theme.SetStylebox("tab_hovered", "TabBar", Box(new Color("FFDD9E"), AccentDeep, 10, 10));
        theme.SetStylebox("tab_disabled", "TabBar", Box(new Color("F6EBD8"), new Color("E0CBA6"), 10, 10));
        theme.SetColor("font_selected_color", "TabBar", Colors.White);
        theme.SetColor("font_unselected_color", "TabBar", new Color("8A6A45"));
        theme.SetColor("font_hovered_color", "TabBar", Text);
        theme.SetColor("font_disabled_color", "TabBar", Muted);

        // OptionButton draws a theme arrow: tint it, or a light arrow vanishes on the white field.
        foreach (var property in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" })
            theme.SetColor(property, "OptionButton", Text);
        theme.SetColor("icon_disabled_color", "OptionButton", Muted);

        // Toggles keep the game's icons, so give them a soft pill for contrast on cream.
        var chip = Box(new Color("FFF1D6"), new Color("EFD3A0"), 8, 6);
        theme.SetStylebox("normal", "CheckButton", chip);
        theme.SetStylebox("pressed", "CheckButton", chip);
        theme.SetStylebox("focus", "CheckButton", chip);
        theme.SetStylebox("hover", "CheckButton", Box(new Color("FFE9BF"), new Color("E8AE4B"), 8, 6));
        theme.SetStylebox("disabled", "CheckButton", Box(new Color("F6EBD8"), new Color("E0CBA6"), 8, 6));
        foreach (var property in new[] { "font_color", "font_hover_color", "font_pressed_color" })
            theme.SetColor(property, "CheckButton", Text);
        theme.SetColor("font_disabled_color", "CheckButton", Muted);

        theme.SetColor("font_color", "Label", Text);
        theme.SetColor("default_color", "RichTextLabel", Text);
        theme.SetStylebox("panel", "TabContainer", Box(Background, new Color(0, 0, 0, 0), 0, 0));
    }

    /// <summary>Emphasised action: orange fill, white text. Use sparingly, one per area.</summary>
    public static void Primary(Button button)
    {
        button.AddThemeStyleboxOverride("normal", Box(Accent, AccentDeep, 10, 12));
        button.AddThemeStyleboxOverride("hover", Box(new Color("FFB347"), AccentDeep, 10, 12));
        button.AddThemeStyleboxOverride("pressed", Box(AccentDeep, AccentDeep, 10, 12));
        button.AddThemeStyleboxOverride("focus", Box(Accent, AccentDeep, 10, 12));
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
    }

    /// <summary>Adds a rounded white card and returns the container to fill.</summary>
    public static VBoxContainer Card(VBoxContainer parent, string title)
    {
        var card = new PanelContainer();
        card.AddThemeStyleboxOverride("panel", Box(CardColor, new Color("EFD3A0"), 16, 12));
        parent.AddChild(card);
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 6);
        card.AddChild(body);
        if (title.Length > 0)
        {
            var label = new Label { Text = title };
            label.AddThemeFontSizeOverride("font_size", 20);
            label.AddThemeColorOverride("font_color", AccentDeep);
            body.AddChild(label);
        }
        return body;
    }

    public static Label Hint(string text) => new()
    {
        Text = text,
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        Modulate = new Color(1, 1, 1, 0.82f)
    };
}
