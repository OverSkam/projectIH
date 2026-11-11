using Godot;

namespace AppStein;

/// <summary>Horizontal bar that really fills/empties (smoothed), shows a lagging "damage trail" and flashes on demand.</summary>
public partial class FillBar : Control
{
    public float Max = 100f, Target = 100f;
    public Color Fill = UiTheme.Danger, Trail = new Color(1f, 0.9f, 0.7f);
    public string Text = "";
    public bool ShowTrail = true;
    public int FontSize = 16;
    float _shown = 100f, _trail = 100f, _flash, _pulse;
    public bool Pulsing;

    public FillBar() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(240, 24); }

    public void Flash() { _flash = 1f; }
    public void Snap() { _shown = _trail = Target; }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _shown = Mathf.MoveToward(_shown, Target, Mathf.Max(30f, Mathf.Abs(Target - _shown) * 6f) * dt * (Max / 100f));
        if (_trail < _shown) _trail = _shown;
        else _trail = Mathf.MoveToward(_trail, _shown, 28f * dt * (Max / 100f)) ;
        _flash = Mathf.Max(0f, _flash - dt * 2.8f);
        _pulse += dt;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, new Color(0.04f, 0.035f, 0.03f, 0.9f));
        float inset = 2f;
        var inner = new Rect2(inset, inset, Size.X - inset * 2, Size.Y - inset * 2);
        float fMax = Mathf.Max(1f, Max);
        if (ShowTrail && _trail > _shown + 0.5f)
            DrawRect(new Rect2(inner.Position, new Vector2(inner.Size.X * Mathf.Clamp(_trail / fMax, 0, 1), inner.Size.Y)), new Color(Trail, 0.85f));
        float w = inner.Size.X * Mathf.Clamp(_shown / fMax, 0, 1);
        var fill = Fill;
        if (Pulsing) fill = Fill.Lerp(Colors.White, 0.25f + 0.2f * Mathf.Sin(_pulse * 8f));
        if (w > 0.5f)
        {
            DrawRect(new Rect2(inner.Position, new Vector2(w, inner.Size.Y)), fill);
            DrawRect(new Rect2(inner.Position, new Vector2(w, inner.Size.Y * 0.4f)), new Color(1, 1, 1, 0.16f)); // top sheen
            DrawRect(new Rect2(inner.Position + new Vector2(0, inner.Size.Y * 0.8f), new Vector2(w, inner.Size.Y * 0.2f)), new Color(0, 0, 0, 0.18f));
        }
        if (_flash > 0f) DrawRect(inner, new Color(1f, 1f, 1f, _flash * 0.7f));
        DrawRect(r, UiTheme.Brass, false, 2f);
        if (Text != "")
        {
            var font = UiTheme.Chunky;
            var sz = font.GetStringSize(Text, HorizontalAlignment.Left, -1, FontSize);
            var pos = new Vector2((Size.X - sz.X) / 2f, (Size.Y + sz.Y) / 2f - 4f);
            DrawString(font, pos + new Vector2(1.5f, 1.5f), Text, HorizontalAlignment.Left, -1, FontSize, new Color(0, 0, 0, 0.9f));
            DrawString(font, pos, Text, HorizontalAlignment.Left, -1, FontSize, Colors.White);
        }
    }
}
