using Godot;

namespace AppStein;

/// <summary>Tiny helpers for building UI in code.</summary>
public static class UiKit
{
    public static Label MakeLabel(string text = "", int size = 18, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = new Label { Text = text, HorizontalAlignment = align, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color ?? Colors.White);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        l.AddThemeConstantOverride("outline_size", 5);
        return l;
    }

    public static Button MakeButton(string text, System.Action onPress)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(140, 36) };
        b.Pressed += onPress;
        return b;
    }

    public static PanelContainer MakePanel(Color? bg = null)
    {
        var p = new PanelContainer();
        var sb = new StyleBoxFlat { BgColor = bg ?? new Color(0, 0, 0, 0.75f), ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14 };
        sb.SetCornerRadiusAll(6);
        p.AddThemeStyleboxOverride("panel", sb);
        return p;
    }

    public static Control FullRect(Control c)
    {
        c.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return c;
    }
}
