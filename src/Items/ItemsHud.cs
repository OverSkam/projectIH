using System;
using System.Text;
using Godot;

namespace AppStein;

/// <summary>Own CanvasLayer for item UI: stein panel with ability hint, notices, map overlay, owl tint. Shows only the local player's private data.</summary>
public partial class ItemsHud : CanvasLayer
{
    Control _root;
    Label _notice, _hint;
    ColorRect[] _swatch = new ColorRect[Inventory.SteinSlots];
    Label[] _stein = new Label[Inventory.SteinSlots];
    PanelContainer[] _slotPanel = new PanelContainer[Inventory.SteinSlots];
    ColorRect _tint;
    MapView _map;
    OmniLight3D _owl;

    public override void _Ready()
    {
        Layer = 6;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(_root);
        AddChild(_root);
        _tint = new ColorRect { Color = new Color(0.4f, 0.6f, 1f, 0.07f), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiKit.FullRect(_tint); _root.AddChild(_tint);

        var box = new VBoxContainer();
        box.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        box.OffsetLeft = -300; box.OffsetTop = -190; box.OffsetRight = -16; box.OffsetBottom = -16;
        box.GrowHorizontal = Control.GrowDirection.Begin; box.GrowVertical = Control.GrowDirection.Begin;
        _root.AddChild(box);
        for (int i = 0; i < Inventory.SteinSlots; i++)
        {
            var pc = UiKit.MakePanel(new Color(0, 0, 0, 0.6f));
            var h = new HBoxContainer();
            _swatch[i] = new ColorRect { CustomMinimumSize = new Vector2(22, 22), Color = new Color(0.2f, 0.2f, 0.2f) };
            _stein[i] = UiKit.MakeLabel("", 17);
            h.AddChild(_swatch[i]); h.AddChild(_stein[i]);
            pc.AddChild(h); box.AddChild(pc); _slotPanel[i] = pc;
        }
        _hint = UiKit.MakeLabel("", 15, new Color(0.8f, 0.9f, 1f), HorizontalAlignment.Right);
        box.AddChild(_hint);

        _notice = UiKit.MakeLabel("", 22, new Color(1f, 0.95f, 0.6f), HorizontalAlignment.Center);
        _notice.SetAnchorsPreset(Control.LayoutPreset.CenterTop); _notice.OffsetTop = 70; _notice.GrowHorizontal = Control.GrowDirection.Both;
        _root.AddChild(_notice);

        _map = new MapView { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _map.SetAnchorsPreset(Control.LayoutPreset.Center);
        _map.CustomMinimumSize = new Vector2(420, 420);
        _map.OffsetLeft = -210; _map.OffsetRight = 210; _map.OffsetTop = -210; _map.OffsetBottom = 210;
        _root.AddChild(_map);
    }

    public override void _Process(double delta)
    {
        var g = Game.I;
        bool show = g.Phase == GamePhase.Round && g.LocalMiner != null;
        _root.Visible = show;
        if (!show) { _owl = null; return; }
        double now = Time.GetTicksMsec() / 1000.0;
        var inv = g.OwnInv;
        for (int i = 0; i < Inventory.SteinSlots; i++)
        {
            var d = i < inv.Steins.Count ? Steins.Get(inv.Steins[i]) : null;
            _swatch[i].Color = d?.Color ?? new Color(0.2f, 0.2f, 0.2f);
            string sel = inv.SteinSel == i ? "> " : "  ";
            _stein[i].Text = d == null ? sel + "(empty stein slot)" : sel + d.Name;
        }
        var sb = new StringBuilder();
        if (g.OwnAbility != "")
        {
            double cd = g.OwnAbilityReadyAt - now, act = g.OwnEtherealUntil - now;
            string st = act > 0 ? $"ACTIVE {act:0.0}s" : cd > 0 ? $"cooldown {Math.Ceiling(cd)}s" : "READY";
            sb.Append($"[Q] {char.ToUpper(g.OwnAbility[0])}{g.OwnAbility.Substring(1)}: {st}\n");
        }
        sb.Append("[F] pick stein slot   [RMB] use item   [T] drop ore");
        _hint.Text = sb.ToString();

        _notice.Text = now - g.ItemNoticeAt < 4 ? g.ItemNotice : "";
        _tint.Visible = g.OwnDark;
        _map.Visible = inv.Held == "map" && Input.IsActionPressed("use_utility");

        // Owl stein: faint light that only exists on the local miner (never replicated).
        var m = g.LocalMiner;
        if (g.OwnDark)
        {
            if (_owl == null || !IsInstanceValid(_owl) || _owl.GetParent() != m)
            {
                _owl = new OmniLight3D { LightColor = new Color(0.55f, 0.7f, 1f), LightEnergy = 0.7f, OmniRange = 30f, OmniAttenuation = 0.6f, Position = new Vector3(0, 1.6f, 0) };
                m.AddChild(_owl);
            }
        }
        else if (_owl != null && IsInstanceValid(_owl)) { _owl.QueueFree(); _owl = null; }
    }
}

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
