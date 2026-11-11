using Godot;

namespace AppStein;

/// <summary>A tiny miner helmet in a player's colour (lobby list, scoreboard, results).</summary>
public partial class HelmetIcon : Control
{
    public Color Tint = Colors.Yellow;
    public bool Dead;

    public HelmetIcon(Color tint, float size = 30f) { Tint = tint; MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(size, size * 0.85f); }

    Vector2 P(float x, float y) => new(x * Size.X, y * Size.Y);

    public override void _Draw()
    {
        var c = Dead ? Tint.Lerp(new Color(0.3f, 0.3f, 0.3f), 0.7f) : Tint;
        // dome
        DrawColoredPolygon(new[] { P(.12f, .72f), P(.14f, .45f), P(.3f, .2f), P(.5f, .12f), P(.7f, .2f), P(.86f, .45f), P(.88f, .72f) }, c);
        DrawColoredPolygon(new[] { P(.5f, .12f), P(.7f, .2f), P(.86f, .45f), P(.88f, .72f), P(.5f, .72f) }, c.Darkened(0.2f));
        DrawRect(new Rect2(P(.44f, .1f), new Vector2(.12f * Size.X, .62f * Size.Y)), c.Lightened(0.2f)); // ridge
        // brim
        DrawRect(new Rect2(P(.04f, .7f), new Vector2(.92f * Size.X, .13f * Size.Y)), c.Darkened(0.3f));
        // lamp
        DrawRect(new Rect2(P(.38f, .38f), new Vector2(.24f * Size.X, .2f * Size.Y)), new Color(0.2f, 0.2f, 0.24f));
        DrawRect(new Rect2(P(.42f, .43f), new Vector2(.16f * Size.X, .1f * Size.Y)), Dead ? new Color(0.35f, 0.35f, 0.3f) : new Color(1f, 0.95f, 0.65f));
        DrawPolyline(new[] { P(.12f, .72f), P(.14f, .45f), P(.3f, .2f), P(.5f, .12f), P(.7f, .2f), P(.86f, .45f), P(.88f, .72f) }, new Color(0, 0, 0, 0.6f), 1.5f, true);
    }
}
