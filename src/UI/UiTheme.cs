using Godot;

namespace AppStein;

/// <summary>Dark stone-and-brass look. One palette, one Theme, built in code. Apply with UiTheme.Apply(control).</summary>
public static class UiTheme
{
    public static readonly Color Bg = new(0.055f, 0.048f, 0.045f);
    public static readonly Color Stone = new(0.115f, 0.102f, 0.095f);
    public static readonly Color StoneLight = new(0.17f, 0.15f, 0.135f);
    public static readonly Color Brass = new(0.72f, 0.55f, 0.22f);
    public static readonly Color BrassDark = new(0.40f, 0.30f, 0.12f);
    public static readonly Color Gold = new(1f, 0.80f, 0.28f);
    public static readonly Color Parchment = new(0.94f, 0.89f, 0.78f);
    public static readonly Color Dim = new(0.66f, 0.60f, 0.52f);
    public static readonly Color Danger = new(0.88f, 0.27f, 0.22f);
    public static readonly Color Good = new(0.50f, 0.82f, 0.42f);
    public static readonly Color GoblinGreen = new(0.52f, 0.82f, 0.36f);
    public static readonly Color Shade = new(0f, 0f, 0f, 0.55f);

    static Theme _theme;
    static Font _chunky;

    /// <summary>The default font made chunkier (embolden) so titles feel heavy without shipping a font file.</summary>
    public static Font Chunky
    {
        get
        {
            if (_chunky == null) _chunky = new FontVariation { BaseFont = ThemeDB.FallbackFont, VariationEmbolden = 0.55f };
            return _chunky;
        }
    }

    public static StyleBoxFlat Box(Color bg, Color? border = null, int radius = 5, int bw = 0, float mx = 14, float my = 10)
    {
        var sb = new StyleBoxFlat { BgColor = bg, ContentMarginLeft = mx, ContentMarginRight = mx, ContentMarginTop = my, ContentMarginBottom = my };
        sb.SetCornerRadiusAll(radius);
        if (border.HasValue && bw > 0) { sb.BorderColor = border.Value; sb.SetBorderWidthAll(bw); }
        return sb;
    }

    public static Theme Get()
    {
        if (_theme != null) return _theme;
        var t = new Theme();
        t.DefaultFont = Chunky;
        t.DefaultFontSize = 18;

        t.SetColor("font_color", "Label", Parchment);
        t.SetColor("font_outline_color", "Label", new Color(0, 0, 0, 0.9f));
        t.SetConstant("outline_size", "Label", 4);

        foreach (var kind in new[] { "Button", "OptionButton", "CheckBox" })
        {
            t.SetColor("font_color", kind, Parchment);
            t.SetColor("font_hover_color", kind, Gold);
            t.SetColor("font_pressed_color", kind, Gold);
            t.SetColor("font_focus_color", kind, Parchment);
            t.SetColor("font_disabled_color", kind, new Color(0.45f, 0.42f, 0.38f));
            t.SetFontSize("font_size", kind, 20);
        }
        t.SetStylebox("normal", "Button", Box(StoneLight, BrassDark, 5, 2, 16, 8));
        t.SetStylebox("hover", "Button", Box(new Color(0.24f, 0.20f, 0.14f), Brass, 5, 2, 16, 8));
        t.SetStylebox("pressed", "Button", Box(new Color(0.30f, 0.23f, 0.10f), Gold, 5, 2, 16, 8));
        t.SetStylebox("disabled", "Button", Box(new Color(0.09f, 0.085f, 0.08f), new Color(0.2f, 0.18f, 0.15f), 5, 2, 16, 8));
        t.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), Gold, 5, 2, 16, 8));
        t.SetStylebox("normal", "OptionButton", Box(StoneLight, BrassDark, 5, 2, 12, 6));
        t.SetStylebox("hover", "OptionButton", Box(new Color(0.24f, 0.20f, 0.14f), Brass, 5, 2, 12, 6));
        t.SetStylebox("pressed", "OptionButton", Box(new Color(0.30f, 0.23f, 0.10f), Gold, 5, 2, 12, 6));
        t.SetStylebox("focus", "OptionButton", Box(new Color(0, 0, 0, 0), Gold, 5, 2, 12, 6));

        t.SetStylebox("normal", "LineEdit", Box(new Color(0.045f, 0.04f, 0.038f), BrassDark, 4, 2, 10, 7));
        t.SetStylebox("focus", "LineEdit", Box(new Color(0.06f, 0.05f, 0.04f), Gold, 4, 2, 10, 7));
        t.SetStylebox("read_only", "LineEdit", Box(new Color(0.045f, 0.04f, 0.038f), BrassDark, 4, 2, 10, 7));
        t.SetColor("font_color", "LineEdit", Parchment);
        t.SetColor("font_placeholder_color", "LineEdit", new Color(0.5f, 0.46f, 0.4f));
        t.SetColor("caret_color", "LineEdit", Gold);
        t.SetColor("selection_color", "LineEdit", new Color(0.6f, 0.45f, 0.15f, 0.6f));
        t.SetFontSize("font_size", "LineEdit", 20);

        t.SetStylebox("panel", "PanelContainer", Box(new Color(Stone, 0.96f), Brass, 8, 2, 22, 18));
        t.SetStylebox("panel", "PopupMenu", Box(Stone, Brass, 4, 2, 8, 6));

        // sliders
        var track = Box(new Color(0.05f, 0.045f, 0.04f), BrassDark, 4, 1, 0, 0);
        var fill = Box(Brass, null, 4, 0, 0, 0);
        t.SetStylebox("slider", "HSlider", track);
        t.SetStylebox("grabber_area", "HSlider", fill);
        t.SetStylebox("grabber_area_highlight", "HSlider", Box(Gold, null, 4, 0, 0, 0));
        t.SetIcon("grabber", "HSlider", KnobIcon(Parchment));
        t.SetIcon("grabber_highlight", "HSlider", KnobIcon(Gold));
        t.SetIcon("grabber_disabled", "HSlider", KnobIcon(Dim));
        t.SetConstant("center_grabber", "HSlider", 0);

        _theme = t;
        return t;
    }

    static ImageTexture KnobIcon(Color c)
    {
        const int s = 22;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = new Vector2(x + 0.5f - s / 2f, y + 0.5f - s / 2f).Length();
                if (d < 9.5f) img.SetPixel(x, y, d > 7.2f ? BrassDark : c);
            }
        return ImageTexture.CreateFromImage(img);
    }

    public static T Apply<T>(T c) where T : Control { c.Theme = Get(); return c; }
}
