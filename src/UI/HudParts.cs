using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Thin translucent status bar of the minimal HUD (health, oil, stamina, hold progress). No frame, no numbers unless Text is set.</summary>
public partial class ThinBar : Control
{
    public float Max = 1f, Target = 1f;
    public Color Fill = Colors.White;
    public string Text = "";
    public bool Pulsing;
    float _shown = 1f, _flash, _pulse, _trail = 1f;

    public ThinBar(float w, float h) { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(w, h); }
    public void Flash() { _flash = 1f; }
    public void Snap() { _shown = _trail = Target; }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        _shown = Mathf.MoveToward(_shown, Target, Mathf.Max(0.3f * Max, Mathf.Abs(Target - _shown) * 6f) * dt);
        _trail = _trail < _shown ? _shown : Mathf.MoveToward(_trail, _shown, 0.28f * Max * dt);
        _flash = Mathf.Max(0f, _flash - dt * 3f);
        _pulse += dt;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, new Color(0.02f, 0.02f, 0.02f, 0.55f));
        float fMax = Mathf.Max(0.0001f, Max);
        if (_trail > _shown + 0.002f * Max) DrawRect(new Rect2(0, 0, Size.X * Mathf.Clamp(_trail / fMax, 0, 1), Size.Y), new Color(1f, 0.9f, 0.75f, 0.55f));
        float w = Size.X * Mathf.Clamp(_shown / fMax, 0, 1);
        var fill = Fill;
        if (Pulsing) fill = Fill.Lerp(Colors.White, 0.25f + 0.25f * Mathf.Sin(_pulse * 9f));
        if (w > 0.5f) DrawRect(new Rect2(0, 0, w, Size.Y), fill);
        if (_flash > 0f) DrawRect(r, new Color(1f, 1f, 1f, _flash * 0.65f));
        if (Text != "")
        {
            var font = UiTheme.Chunky; int fs = Mathf.RoundToInt(Size.Y + 3);
            var sz = font.GetStringSize(Text, HorizontalAlignment.Left, -1, fs);
            DrawString(font, new Vector2(Size.X + 6, (Size.Y + sz.Y) / 2f - 3), Text, HorizontalAlignment.Left, -1, fs, new Color(1f, 0.9f, 0.85f, 0.95f));
        }
    }
}

/// <summary>Pocket-watch dial of the round: no digits. A bright arc (the time left) shrinks as time passes and a single hand marks its end; small pips under it show the round.</summary>
public partial class DialClock : Control
{
    public float Frac = 1f;
    public int Round = 1, TotalRounds = 1;
    public float Urgency;                      // 0..1 (pulse in the last minutes)

    const float S = 1.3f;
    public DialClock() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(64 * S, 74 * S); }

    public override void _Process(double delta) { QueueRedraw(); }

    public override void _Draw()
    {
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One * S);
        var c = new Vector2(Size.X / 2f / S, 35f); float r = 24f;
        var brass = new Color(0.72f, 0.55f, 0.22f, 0.9f);
        // crown and ring
        DrawRect(new Rect2(c.X - 4, c.Y - r - 9, 8, 7), new Color(brass, 0.85f));
        DrawRect(new Rect2(c.X - 6, c.Y - r - 4, 12, 3), new Color(brass, 0.85f));
        DrawCircle(c, r + 2.5f, new Color(0.02f, 0.02f, 0.02f, 0.5f));
        DrawArc(c, r + 1.5f, 0, Mathf.Tau, 40, brass, 2.5f, true);
        DrawCircle(c, r, new Color(0.055f, 0.048f, 0.04f, 0.72f));
        // ticks
        for (int i = 0; i < 12; i++)
        {
            float a = -Mathf.Pi / 2f + i * Mathf.Tau / 12f; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            bool big = i % 3 == 0;
            DrawLine(c + d * (r - (big ? 6.5f : 4f)), c + d * (r - 1.5f), new Color(0.94f, 0.89f, 0.78f, big ? 0.75f : 0.4f), big ? 2f : 1.2f);
        }
        // the time left: bright arc from the hand clockwise up to the top
        float f = Mathf.Clamp(Frac, 0f, 1f);
        Color col = f > 0.34f ? new Color(1f, 0.8f, 0.28f) : f > 0.17f ? new Color(1f, 0.58f, 0.2f) : new Color(0.95f, 0.3f, 0.2f);
        float handA = -Mathf.Pi / 2f + (1f - f) * Mathf.Tau;
        if (f > 0.003f) DrawArc(c, r - 9.5f, handA, -Mathf.Pi / 2f + Mathf.Tau, Mathf.Max(4, (int)(48 * f)), new Color(col, 0.9f), 7f, true);
        // the single hand
        var hd = new Vector2(Mathf.Cos(handA), Mathf.Sin(handA));
        DrawLine(c - hd * 4f, c + hd * (r - 3f), new Color(0, 0, 0, 0.6f), 4.5f, true);
        DrawLine(c - hd * 4f, c + hd * (r - 3f), new Color(0.97f, 0.93f, 0.82f), 2.4f, true);
        DrawCircle(c, 3.6f, brass);
        // round pips (no digits)
        int n = Mathf.Clamp(TotalRounds, 1, 9);
        float px = c.X - (n - 1) * 4.5f;
        for (int i = 0; i < n; i++) DrawCircle(new Vector2(px + i * 9f, c.Y + r + 9f), 2.3f, i < Round ? new Color(1f, 0.8f, 0.28f, 0.95f) : new Color(0.5f, 0.45f, 0.38f, 0.5f));
    }
}

/// <summary>The last minute: a rope fuse across the top of the screen burning down to a powder keg on the right, with a sparking tip. Frac = fraction of the minute left.</summary>
public partial class FuseBar : Control
{
    public float Frac = 1f, Urgent;            // Urgent 0..1 in the last 10 seconds
    class Spark { public Vector2 P, V; public float Life, Max; public Color C; }
    readonly List<Spark> _sparks = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 77 };
    float _t, _emit;

    const float S = 1.3f;
    public FuseBar() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(540 * S, 56 * S); }

    float L => Size.X / S - 54f;
    public Vector2 TipPos => new(10f + L * (1f - Mathf.Clamp(Frac, 0f, 1f)), 22f + 1.5f * Mathf.Sin(_t * 17f));

    public override void _Process(double delta)
    {
        float dt = (float)delta; _t += dt;
        var tip = TipPos;
        _emit += dt * (26f + 90f * Urgent) * (Frac > 0.001f ? 1f : 0.2f);
        while (_emit >= 1f)
        {
            _emit -= 1f;
            if (_sparks.Count > 90) break;
            float a = _rng.RandfRange(-Mathf.Pi * 0.95f, -Mathf.Pi * 0.05f);
            float sp = _rng.RandfRange(40f, 130f + 90f * Urgent);
            _sparks.Add(new Spark { P = tip, V = new Vector2(Mathf.Cos(a) * sp, Mathf.Sin(a) * sp), Max = _rng.RandfRange(0.25f, 0.65f), C = _rng.Randf() < 0.3f ? new Color(1f, 0.95f, 0.7f) : new Color(1f, 0.6f, 0.15f) });
            _sparks[^1].Life = _sparks[^1].Max;
        }
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var s = _sparks[i]; s.Life -= dt;
            if (s.Life <= 0) { _sparks.RemoveAt(i); continue; }
            s.V += new Vector2(0, 260f) * dt; s.P += s.V * dt;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One * S);
        float y = 22f, x0 = 10f, x1 = 10f + L;
        var tip = TipPos;
        // the burnt part: a faint charred trace
        DrawLine(new Vector2(x0, y), new Vector2(tip.X, y), new Color(0.05f, 0.04f, 0.04f, 0.4f), 3f, true);
        // rope: twisted strands from the tip to the keg
        float xs = Mathf.Max(tip.X + 3f, x0);
        DrawLine(new Vector2(xs, y + 0.5f), new Vector2(x1, y + 0.5f), new Color(0, 0, 0, 0.55f), 8f, true);
        for (float x = xs; x < x1; x += 6f)
        {
            var a = new Vector2(x, y - 2.6f); var b = new Vector2(Mathf.Min(x + 6f, x1), y + 2.6f);
            DrawLine(a, b, new Color(0.78f, 0.66f, 0.43f), 3.2f, true);
            DrawLine(new Vector2(a.X, b.Y), new Vector2(b.X, a.Y), new Color(0.55f, 0.43f, 0.26f), 2.4f, true);
        }
        // a short blackened stretch just behind the flame
        DrawLine(new Vector2(Mathf.Max(x0, tip.X - 0.5f), y), new Vector2(Mathf.Min(x1, tip.X + 16f), y), new Color(0.12f, 0.08f, 0.05f, 0.9f), 5f, true);
        // powder keg at the end
        var k = new Vector2(x1 + 22f, y + 3f);
        DrawCircle(k, 15f, new Color(0, 0, 0, 0.45f));
        DrawCircle(k, 13f, new Color(0.30f, 0.20f, 0.12f));
        DrawArc(k, 13f, 0, Mathf.Tau, 24, new Color(0.14f, 0.12f, 0.12f), 2.5f, true);
        DrawLine(k + new Vector2(-12, -3), k + new Vector2(12, -3), new Color(0.14f, 0.12f, 0.12f), 2f);
        DrawLine(k + new Vector2(-12, 5), k + new Vector2(12, 5), new Color(0.14f, 0.12f, 0.12f), 2f);
        DrawCircle(k + new Vector2(-4, -6), 3f, new Color(1f, 1f, 1f, 0.12f));
        // sparks
        foreach (var s in _sparks)
        {
            float a = Mathf.Clamp(s.Life / s.Max, 0f, 1f);
            DrawLine(s.P, s.P - s.V * 0.045f, new Color(s.C, a), 2.8f);
        }
        // the burning tip: glow, flame, white-hot core (bigger and brighter in the last 10 seconds)
        float g = 1f + 0.3f * Mathf.Sin(_t * 31f) + 0.7f * Urgent;
        DrawCircle(tip, 17f * g, new Color(1f, 0.45f, 0.1f, 0.14f + 0.1f * Urgent));
        DrawCircle(tip, 11f * g, new Color(1f, 0.55f, 0.12f, 0.35f));
        DrawCircle(tip, 7f * g, new Color(1f, 0.8f, 0.3f, 0.9f));
        DrawCircle(tip, 3.6f * g, new Color(1f, 1f, 0.9f));
    }
}

/// <summary>The sack icon of the HUD: one sack that visibly bulges in four steps with the burden, tinted when it slows you, with a small row of weight pips beneath.</summary>
public partial class SackIcon : Control
{
    public float Burden, Speed = 1f;
    public int Stones;

    public SackIcon() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(52, 58); }

    /// <summary>0 = empty / limp ... 4 = bursting.</summary>
    public static int Level(float burden) => burden <= 0.01f ? 0 : burden <= 5f ? 1 : burden <= 12f ? 2 : burden <= 22f ? 3 : 4;

    public override void _Draw()
    {
        int lv = Level(Burden);
        float w = Size.X, cx = w / 2f, baseY = 44f;
        Color tint = Speed > 0.85f ? new Color(0.62f, 0.44f, 0.24f) : Speed > 0.6f ? new Color(0.78f, 0.5f, 0.2f) : new Color(0.8f, 0.32f, 0.22f);
        float[] rw = { 11f, 13.5f, 16.5f, 19.5f, 22.5f };     // half width of the belly
        float[] rh = { 12f, 16f, 20f, 24f, 28f };             // height of the belly
        float bw = rw[lv], bh = rh[lv];
        // belly
        var pts = new List<Vector2>();
        for (int i = 0; i <= 16; i++)
        {
            float a = Mathf.Pi * i / 16f;                      // bottom (0) to top (pi) on the right side first
            pts.Add(new Vector2(cx + Mathf.Sin(a) * bw, baseY - (1f - Mathf.Cos(a)) * 0.5f * bh));
        }
        for (int i = 15; i >= 1; i--)
        {
            float a = Mathf.Pi * i / 16f;
            pts.Add(new Vector2(cx - Mathf.Sin(a) * bw, baseY - (1f - Mathf.Cos(a)) * 0.5f * bh));
        }
        var poly = pts.ToArray();
        DrawColoredPolygon(OffsetAll(poly, new Vector2(1.5f, 2f)), new Color(0, 0, 0, 0.35f));
        DrawColoredPolygon(poly, tint);
        DrawColoredPolygon(Shrink(poly, new Vector2(cx, baseY - bh * 0.5f), 0.62f, new Vector2(-bw * 0.12f, -bh * 0.1f)), tint.Lightened(0.18f));
        // neck, tie cord and a floppy top
        float topY = baseY - bh;
        DrawColoredPolygon(new[] { new Vector2(cx - 6, topY + 3), new Vector2(cx + 6, topY + 3), new Vector2(cx + 4, topY - 6), new Vector2(cx - 4, topY - 6) }, tint.Darkened(0.12f));
        DrawLine(new Vector2(cx - 5.5f, topY - 1), new Vector2(cx + 5.5f, topY - 1), new Color(0.22f, 0.14f, 0.07f), 2.2f);
        DrawColoredPolygon(new[] { new Vector2(cx - 4, topY - 6), new Vector2(cx - 9, topY - 11), new Vector2(cx - 2, topY - 8) }, tint.Darkened(0.05f));
        DrawColoredPolygon(new[] { new Vector2(cx + 4, topY - 6), new Vector2(cx + 9, topY - 10), new Vector2(cx + 2, topY - 8) }, tint.Darkened(0.05f));
        // stitches
        DrawLine(new Vector2(cx + bw * 0.25f, baseY - bh * 0.7f), new Vector2(cx + bw * 0.45f, baseY - bh * 0.25f), new Color(0.2f, 0.12f, 0.06f, 0.7f), 1.4f);
        // weight pips: how heavy (also how slow); amber/red when it slows you noticeably
        int pips = Mathf.Clamp(Mathf.CeilToInt(Burden / 6f), 0, 5);
        if (Burden > 0.01f)
            for (int i = 0; i < 5; i++)
            {
                var pc = i < pips ? (Speed > 0.85f ? new Color(0.94f, 0.89f, 0.78f, 0.95f) : Speed > 0.6f ? new Color(1f, 0.72f, 0.28f) : new Color(0.95f, 0.35f, 0.25f)) : new Color(0.4f, 0.36f, 0.3f, 0.5f);
                DrawRect(new Rect2(cx - 14.5f + i * 6.2f, 52f, 4.4f, 3f), pc);
            }
    }

    static Vector2[] OffsetAll(Vector2[] p, Vector2 d) { var r = new Vector2[p.Length]; for (int i = 0; i < p.Length; i++) r[i] = p[i] + d; return r; }
    static Vector2[] Shrink(Vector2[] p, Vector2 c, float k, Vector2 shift) { var r = new Vector2[p.Length]; for (int i = 0; i < p.Length; i++) r[i] = c + (p[i] - c) * k + shift; return r; }
}
