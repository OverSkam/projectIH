using System;
using Godot;

namespace AppStein;

/// <summary>Top-down room layout from CaveInfo with only the holder's own position.</summary>
public partial class MapView : Control
{
    public override void _Process(double delta) { if (Visible) QueueRedraw(); }

    public override void _Draw()
    {
        var g = Game.I; var cave = g.World?.Cave;
        if (cave == null) return;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.85f, 0.78f, 0.55f, 0.9f));
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        void Ext(Aabb b) { minX = Math.Min(minX, b.Position.X); maxX = Math.Max(maxX, b.End.X); minZ = Math.Min(minZ, b.Position.Z); maxZ = Math.Max(maxZ, b.End.Z); }
        Ext(cave.CampZone); foreach (var r in cave.Rooms) Ext(r.Bounds);
        float sc = Math.Min((Size.X - 24) / (maxX - minX), (Size.Y - 24) / (maxZ - minZ));
        Vector2 P(Vector3 v) => new Vector2(12 + (v.X - minX) * sc, 12 + (v.Z - minZ) * sc);
        foreach (var (a, b) in cave.NavEdges) DrawLine(P(cave.NavNodes[a]), P(cave.NavNodes[b]), new Color(0.35f, 0.28f, 0.15f), 6f);
        void Box(Aabb b, Color c) { var p0 = P(b.Position); DrawRect(new Rect2(p0, new Vector2(b.Size.X * sc, b.Size.Z * sc)), c); }
        Box(cave.CampZone, new Color(0.9f, 0.55f, 0.2f, 0.85f));
        foreach (var r in cave.Rooms) Box(r.Bounds, new Color(0.35f, 0.3f, 0.25f, 0.85f));
        var font = ThemeDB.FallbackFont;
        DrawString(font, P(cave.CampZone.GetCenter()) - new Vector2(18, 0), "CAMP", HorizontalAlignment.Left, -1, 14, Colors.Black);
        var me = g.LocalMiner;
        if (me != null)
        {
            var pp = P(me.GlobalPosition);
            DrawCircle(pp, 6f, new Color(0.1f, 0.9f, 0.2f));
            float yaw = me.Rotation.Y;
            DrawLine(pp, pp + new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw)) * 16f, new Color(0.1f, 0.9f, 0.2f), 3f);
        }
        DrawString(font, new Vector2(10, Size.Y - 6), "MAP - you are the green dot", HorizontalAlignment.Left, -1, 14, Colors.Black);
    }
}
