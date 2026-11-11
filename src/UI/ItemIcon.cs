using Godot;

namespace AppStein;

/// <summary>Draws an inventory icon in code. Data-driven: kind = "ore" | "stein" | "util", id looked up in the Def registries
/// (colour, rarity, name). Unknown utilities get a generic tile with their initial.</summary>
public partial class ItemIcon : Control
{
    public string Kind = "", Id = "";
    public bool Empty => string.IsNullOrEmpty(Id);

    public ItemIcon() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(40, 40); }

    public void Set(string kind, string id)
    {
        if (Kind == kind && Id == id) return;
        Kind = kind; Id = id ?? ""; QueueRedraw();
    }

    Vector2 P(float x, float y) => new(x * Size.X, y * Size.Y);
    static Color Lt(Color c, float a) => c.Lerp(Colors.White, a);
    static Color Dk(Color c, float a) => c.Lerp(Colors.Black, a);

    public override void _Draw()
    {
        if (Empty) return;
        switch (Kind)
        {
            case "ore": DrawOre(Ores.Get(Id)); break;
            case "stein": DrawStein(Steins.Get(Id)); break;
            default: DrawUtil(Id, Utilities.Get(Id)); break;
        }
    }

    void DrawOre(OreDef d)
    {
        var c = d?.Color ?? Colors.Gray;
        if (d != null && d.Glow > 0.2f) DrawCircle(P(.5f, .52f), Size.X * 0.46f, new Color(c, 0.18f));
        var outline = Lt(Dk(c, 0.5f), 0.25f);
        // faceted gem silhouette
        var o = new[] { P(.5f, .08f), P(.86f, .32f), P(.78f, .84f), P(.5f, .94f), P(.22f, .84f), P(.14f, .32f) };
        DrawColoredPolygon(o, c);
        DrawColoredPolygon(new[] { P(.5f, .08f), P(.86f, .32f), P(.5f, .42f), P(.14f, .32f) }, Lt(c, 0.35f));
        DrawColoredPolygon(new[] { P(.5f, .42f), P(.86f, .32f), P(.78f, .84f), P(.5f, .94f) }, Dk(c, 0.3f));
        DrawColoredPolygon(new[] { P(.14f, .32f), P(.5f, .42f), P(.5f, .94f), P(.22f, .84f) }, Dk(c, 0.1f));
        var line = new Vector2[o.Length + 1]; o.CopyTo(line, 0); line[o.Length] = o[0];
        DrawPolyline(line, outline, 2f, true);
        DrawLine(P(.14f, .32f), P(.5f, .42f), new Color(outline, 0.6f), 1.5f);
        DrawLine(P(.86f, .32f), P(.5f, .42f), new Color(outline, 0.6f), 1.5f);
        DrawLine(P(.5f, .42f), P(.5f, .94f), new Color(outline, 0.6f), 1.5f);
    }

    static Color RarityColor(Rarity r) => r switch
    {
        Rarity.Rare => new Color(0.35f, 0.65f, 1f),
        Rarity.Legendary => new Color(1f, 0.55f, 0.15f),
        _ => new Color(0.7f, 0.66f, 0.58f),
    };

    void DrawStein(SteinDef d)
    {
        var c = d?.Color ?? Colors.White;
        var rar = RarityColor(d?.Rarity ?? Rarity.Common);
        var ctr = P(.5f, .5f); float r = Size.X * 0.36f;
        DrawCircle(ctr, r + Size.X * 0.1f, new Color(rar, 0.28f));
        DrawArc(ctr, r + Size.X * 0.07f, 0, Mathf.Tau, 28, rar, 2.5f, true);
        // faceted stone (octagon) with highlight
        var pts = new Vector2[8];
        for (int i = 0; i < 8; i++) { float a = i * Mathf.Tau / 8 + 0.2f; pts[i] = ctr + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
        DrawColoredPolygon(pts, c);
        DrawColoredPolygon(new[] { pts[5], pts[6], pts[7], pts[0], ctr }, Lt(c, 0.28f));
        DrawColoredPolygon(new[] { pts[1], pts[2], pts[3], pts[4], ctr }, Dk(c, 0.28f));
        var closed = new Vector2[9]; pts.CopyTo(closed, 0); closed[8] = pts[0];
        DrawPolyline(closed, Dk(c, 0.6f), 1.5f, true);
        DrawCircle(ctr + new Vector2(-r * 0.3f, -r * 0.35f), r * 0.14f, new Color(1, 1, 1, 0.85f));
    }

    void DrawUtil(string id, UtilityDef d)
    {
        var c = d?.Color ?? new Color(0.6f, 0.6f, 0.6f);
        var steel = new Color(0.72f, 0.74f, 0.8f);
        var wood = new Color(0.6f, 0.42f, 0.24f);
        float w = Mathf.Max(2.5f, Size.X * 0.07f);
        switch (id)
        {
            case "pickaxe":
                DrawLine(P(.26f, .88f), P(.76f, .22f), wood, w * 1.3f, true);
                DrawPolyline(new[] { P(.2f, .36f), P(.38f, .16f), P(.62f, .1f), P(.86f, .2f), P(.94f, .4f) }, steel, w * 1.5f, true);
                DrawColoredPolygon(new[] { P(.6f, .12f), P(.88f, .22f), P(.8f, .34f), P(.66f, .26f) }, Dk(steel, 0.15f));
                break;
            case "lantern":
                DrawArc(P(.5f, .22f), Size.X * 0.14f, Mathf.Pi, Mathf.Tau, 10, wood, w, true);
                DrawRect(new Rect2(P(.3f, .24f), new Vector2(.4f * Size.X, .08f * Size.Y)), c);
                DrawRect(new Rect2(P(.3f, .78f), new Vector2(.4f * Size.X, .08f * Size.Y)), c);
                DrawRect(new Rect2(P(.34f, .32f), new Vector2(.32f * Size.X, .46f * Size.Y)), new Color(1f, 0.82f, 0.4f, 0.35f));
                DrawRect(new Rect2(P(.34f, .32f), new Vector2(.32f * Size.X, .46f * Size.Y)), c, false, 2f);
                DrawCircle(P(.5f, .6f), Size.X * 0.09f, new Color(1f, 0.85f, 0.3f));
                DrawCircle(P(.5f, .6f), Size.X * 0.15f, new Color(1f, 0.7f, 0.2f, 0.3f));
                break;
            case "rope":
                for (int i = 0; i < 3; i++) DrawArc(P(.5f, .55f), Size.X * (0.14f + i * 0.09f), 0, Mathf.Tau, 20, c, w, true);
                DrawLine(P(.5f, .55f - .14f), P(.82f, .12f), c, w, true);
                break;
            case "gasmask":
                DrawCircle(P(.5f, .45f), Size.X * 0.3f, new Color(0.25f, 0.3f, 0.25f));
                DrawCircle(P(.38f, .4f), Size.X * 0.09f, new Color(0.7f, 0.9f, 0.8f));
                DrawCircle(P(.62f, .4f), Size.X * 0.09f, new Color(0.7f, 0.9f, 0.8f));
                DrawRect(new Rect2(P(.4f, .55f), new Vector2(.2f * Size.X, .3f * Size.Y)), c);
                break;
            case "map":
                DrawColoredPolygon(new[] { P(.14f, .2f), P(.4f, .14f), P(.6f, .2f), P(.86f, .14f), P(.86f, .8f), P(.6f, .86f), P(.4f, .8f), P(.14f, .86f) }, c);
                DrawLine(P(.4f, .14f), P(.4f, .8f), Dk(c, 0.35f), 1.5f);
                DrawLine(P(.6f, .2f), P(.6f, .86f), Dk(c, 0.35f), 1.5f);
                DrawLine(P(.3f, .6f), P(.5f, .4f), Colors.DarkRed, 2f); DrawLine(P(.5f, .6f), P(.3f, .4f), Colors.DarkRed, 2f);
                break;
            case "dynamite":
                for (int i = 0; i < 3; i++) DrawRect(new Rect2(P(.2f + i * .2f, .3f), new Vector2(.15f * Size.X, .55f * Size.Y)), i == 1 ? Lt(c, 0.12f) : c);
                DrawRect(new Rect2(P(.2f, .52f), new Vector2(.55f * Size.X, .08f * Size.Y)), new Color(0.15f, 0.12f, 0.1f));
                DrawPolyline(new[] { P(.5f, .3f), P(.55f, .18f), P(.68f, .12f) }, new Color(0.8f, 0.7f, 0.5f), 2f, true);
                DrawCircle(P(.7f, .11f), Size.X * 0.06f, new Color(1f, 0.7f, 0.2f));
                break;
            case "beartrap":
                DrawArc(P(.5f, .6f), Size.X * 0.34f, Mathf.Pi, Mathf.Tau, 14, steel, w * 1.2f, true);
                DrawArc(P(.5f, .6f), Size.X * 0.34f, 0, Mathf.Pi, 14, Dk(steel, 0.3f), w * 1.2f, true);
                for (int i = 0; i < 5; i++) { float x = .22f + i * .14f; DrawColoredPolygon(new[] { P(x, .6f), P(x + .07f, .4f), P(x + .14f, .6f) }, steel); }
                DrawRect(new Rect2(P(.1f, .72f), new Vector2(.8f * Size.X, .08f * Size.Y)), c);
                break;
            default:
                DrawRect(new Rect2(P(.14f, .14f), new Vector2(.72f * Size.X, .72f * Size.Y)), Dk(c, 0.25f));
                DrawRect(new Rect2(P(.14f, .14f), new Vector2(.72f * Size.X, .72f * Size.Y)), Lt(c, 0.2f), false, 2f);
                string ch = (d?.Name ?? id).Length > 0 ? (d?.Name ?? id).Substring(0, 1).ToUpper() : "?";
                var font = UiTheme.Chunky;
                int fs = (int)(Size.Y * 0.5f);
                var sz = font.GetStringSize(ch, HorizontalAlignment.Left, -1, fs);
                DrawString(font, new Vector2((Size.X - sz.X) / 2f, Size.Y / 2f + sz.Y * 0.3f), ch, HorizontalAlignment.Left, -1, fs, Colors.White);
                break;
        }
    }
}
