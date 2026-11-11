using Godot;

namespace AppStein;

/// <summary>Tiny helpers for building UI in code (theme in UiTheme).</summary>
public static class UiKit
{
    public static Label MakeLabel(string text = "", int size = 18, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left, int outline = 4)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? UiTheme.Parchment);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.9f));
        l.AddThemeConstantOverride("outline_size", outline);
        return l;
    }

    /// <summary>Big display heading (chunky, brass-gold, thick outline).</summary>
    public static Label MakeTitle(string text, int size = 44, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Center)
        => MakeLabel(text, size, color ?? UiTheme.Gold, align, 10);

    public static Button MakeButton(string text, System.Action onPress, int minWidth = 160, bool primary = false)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(minWidth, 44), FocusMode = Control.FocusModeEnum.None };
        b.Pressed += () => { Sfx.Play(SfxNames.UiClick); onPress(); };
        b.MouseEntered += () => Sfx.Play(SfxNames.UiHover);
        if (primary)
        {
            b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(0.42f, 0.31f, 0.1f), UiTheme.Gold, 5, 2, 16, 8));
            b.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(0.55f, 0.41f, 0.13f), UiTheme.Gold, 5, 2, 16, 8));
            b.AddThemeColorOverride("font_color", UiTheme.Gold);
        }
        return b;
    }

    public static PanelContainer MakePanel(Color? bg = null)
    {
        var p = new PanelContainer();
        if (bg.HasValue) p.AddThemeStyleboxOverride("panel", UiTheme.Box(bg.Value, UiTheme.Brass, 8, 2, 22, 18));
        return p;
    }

    public static Control FullRect(Control c)
    {
        c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return c;
    }

    public static Control Spacer(float h) => new Control { CustomMinimumSize = new Vector2(0, h), MouseFilter = Control.MouseFilterEnum.Ignore };

    public static ColorRect Rule(Color? c = null, float h = 2) => new ColorRect { Color = c ?? UiTheme.BrassDark, CustomMinimumSize = new Vector2(0, h), MouseFilter = Control.MouseFilterEnum.Ignore };

    public static VBoxContainer VBox(int sep = 8) { var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", sep); return v; }
    public static HBoxContainer HBox(int sep = 8) { var v = new HBoxContainer(); v.AddThemeConstantOverride("separation", sep); return v; }

    public static string Money(int v) => "$" + v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
}
