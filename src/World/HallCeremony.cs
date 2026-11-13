using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>
/// The Hall's ceremony dressing (v2.2): one minecart per miner on the arc of <see cref="CaveInfo.WeighSpots"/> (empty during the round), the round candle, the sacks that drop into the carts
/// at the weighing, the weight number floating above the Goblin's palm, and the shaft he throws miners into. There is no giant Scale any more.
/// Built on every peer from the replicated state (Game.RoundPeers, Game.Weigh). The Goblin's right hand (GoblinRig) reaches for the current sack, lifts it to the palm and
/// publishes <see cref="PalmWorld"/>; this node moves the sack with it and counts the number up. Sack size scales a little with the tribute (a size class, never the number).
/// </summary>
public partial class HallCeremony : Node3D
{
    public static HallCeremony I;
    /// <summary>World position of the Goblin's palm centre this frame (published by GoblinRig).</summary>
    public static Vector3 PalmWorld;
    public static bool PalmValid;

    class Sack
    {
        public int Peer; public Node3D Node; public Vector3 Floor; public float Size = 1f; public float Delay, Fall = -1f, Vy, Deflate;
        public bool Held;
    }
    readonly Dictionary<int, Sack> _sacks = new();
    readonly Dictionary<int, Vector3> _ringPos = new();
    Label3D _num, _name;
    OmniLight3D _shaftLight, _numLight;
    MeshInstance3D _shaftGlow;
    float _t;
    string _lastGrab = "", _lastNumber = "";
    public Vector3 FloorOf(int peer) => _sacks.TryGetValue(peer, out var s) ? s.Floor : Vector3.Zero;
    public Vector3 SackFloor(int peer) => _sacks.TryGetValue(peer, out var s) ? s.Floor : (_ringPos.TryGetValue(peer, out var r) ? new Vector3(r.X, r.Y - 0.3f + CartModel.InnerFloor + 0.28f, r.Z) : Vector3.Zero);
    public float SackSize(int peer) => _sacks.TryGetValue(peer, out var s) ? s.Size : 1f;

    public override void _Ready()
    {
        I = this;
        var cave = Game.I.World.Cave;
        Position = cave.ScalePos;
        BuildShaft(cave);
        BuildCarts(cave);
        _num = new Label3D
        {
            Text = "", PixelSize = 0.014f, FontSize = 220, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, TopLevel = true, Visible = false, NoDepthTest = true,
            Modulate = new Color(1f, 0.9f, 0.45f), OutlineSize = 44, OutlineModulate = new Color(0, 0, 0, 0.95f), Font = UiTheme.Chunky, RenderPriority = 3, OutlineRenderPriority = 2,
        };
        AddChild(_num);
        _name = new Label3D
        {
            Text = "", PixelSize = 0.0065f, FontSize = 160, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, TopLevel = true, Visible = false, NoDepthTest = true,
            Modulate = new Color(0.95f, 0.92f, 0.8f), OutlineSize = 30, OutlineModulate = new Color(0, 0, 0, 0.95f), Font = UiTheme.Chunky, RenderPriority = 3, OutlineRenderPriority = 2,
        };
        AddChild(_name);
        _numLight = new OmniLight3D { TopLevel = true, Visible = false, LightColor = new Color(1f, 0.86f, 0.45f), LightEnergy = 2.2f, OmniRange = 7f, OmniAttenuation = 1.1f };
        AddChild(_numLight);
    }

    public override void _ExitTree() { if (I == this) { I = null; PalmValid = false; } }

    // ------------------------------------------------------------------ minecarts
    /// <summary>One minecart per miner of this round (on the arc of weigh spots, long axis pointing at the arc's centre), trimmed in the miner's colour, with their name on a brass plate. They stand empty during the round.</summary>
    void BuildCarts(CaveInfo cave)
    {
        var peers = Game.I.RoundPeers;
        if (peers == null || peers.Length == 0 || cave.WeighSpots.Count == 0) return;
        // screenshots: --debugcarts=8 draws carts for that many miners (names/colours invented)
        int fakeN = int.TryParse(Util.Arg("debugcarts", "0"), out var fn) ? Mathf.Clamp(fn, 0, 8) : 0;
        string[] fakeNames = { "Alice", "Bob", "Cara", "Dmitri", "Eve", "Fritz", "Gus", "Hannah" };
        int total = fakeN > peers.Length ? fakeN : peers.Length;
        for (int i = 0; i < total; i++)
        {
            int peer = i < peers.Length ? peers[i] : 9000 + i;
            var spot = cave.RingSpot(i, total);
            _ringPos[peer] = spot;
            var cart = new Node3D { Name = "Cart" + peer, TopLevel = true };
            AddChild(cart);
            var toC = (cave.ScalePos - spot) with { Y = 0 };
            toC = toC.LengthSquared() < 0.01f ? Vector3.Forward : toC.Normalized();
            cart.GlobalPosition = new Vector3(spot.X, spot.Y - 0.3f, spot.Z);
            cart.Rotation = new Vector3(0, Mathf.Atan2(-toC.X, -toC.Z), 0);   // local -Z (the open end) faces the Goblin
            CartModel.Build(cart, i < peers.Length ? PlayerColors.Of(peer) : PlayerColors.Palette[i % 8], i < peers.Length && fakeN == 0 ? Net.I.NameOf(peer) : fakeNames[i % 8]);
        }
        AddChild(new HallCandle { Name = "Candle", TopLevel = true, Position = cave.GoblinSeat + new Vector3(7.0f, 0, -0.3f) });
    }

    /// <summary>Centre of this miner's cart on the floor (zero when the peer has none).</summary>
    public Vector3 CartPos(int peer) => _ringPos.TryGetValue(peer, out var r) ? new Vector3(r.X, r.Y - 0.3f, r.Z) : Vector3.Zero;
    public bool HasCart(int peer) => _ringPos.ContainsKey(peer);

    // ------------------------------------------------------------------ sacks (the weighing)
    static Node3D MakeSackModel()
    {
        var root = new Node3D();
        var brown = new Color(0.50f, 0.34f, 0.17f); var dark = new Color(0.30f, 0.20f, 0.10f);
        var m = new GMesh { Variation = 0.12f };
        m.Ellipsoid(new Vector3(0, 0.42f, 0), new Vector3(0.52f, 0.46f, 0.46f), brown, 10, 6);
        m.Ellipsoid(new Vector3(0.05f, 0.38f, 0.04f), new Vector3(0.5f, 0.42f, 0.5f), brown * 0.92f, 8, 5);
        m.Tube(new Vector3(0, 0.78f, 0), new Vector3(0, 1.02f, 0), 0.22f, 0.14f, brown * 0.85f, 8, 1f, false, false);
        m.Tube(new Vector3(0, 0.86f, 0), new Vector3(0, 0.92f, 0), 0.26f, 0.26f, dark, 8, 1f, false, false);   // the cord
        m.Ellipsoid(new Vector3(0, 1.05f, 0), new Vector3(0.3f, 0.12f, 0.3f), brown * 0.8f, 8, 3);
        m.Ellipsoid(new Vector3(0.22f, 0.9f, 0.05f), new Vector3(0.1f, 0.18f, 0.06f), dark, 5, 3);
        m.Instance(root, null, "Sack");
        return root;
    }

    /// <summary>The ceremony starts: every placed miner's sack drops from above in front of their ring. sizes[i] is a size class 0..4 (public, vague).</summary>
    public void BeginSacks(int[] peers, Vector3[] spots, int[] sizes)
    {
        foreach (var s in _sacks.Values) s.Node.QueueFree();
        _sacks.Clear(); _lastGrab = ""; _lastNumber = "";
        Util.Pass("sacks_dropped", $"count={peers.Length}");
        for (int i = 0; i < peers.Length; i++)
        {
            // the sack falls straight into the miner's minecart and settles on its floor
            var cart = _ringPos.TryGetValue(peers[i], out var cp) ? new Vector3(cp.X, cp.Y - 0.3f, cp.Z) : new Vector3(spots[i].X, spots[i].Y - 0.3f, spots[i].Z);
            var floor = cart + new Vector3(0, CartModel.InnerFloor + 0.28f, 0);
            int cls = i < sizes.Length ? Mathf.Clamp(sizes[i], 0, 4) : 1;
            var node = MakeSackModel();
            float size = 0.52f + 0.1f * cls;
            node.Scale = Vector3.One * size;
            node.TopLevel = true;
            AddChild(node);
            node.GlobalPosition = floor + Vector3.Up * 8f;
            node.Rotation = new Vector3(0, (peers[i] * 0.7f) % 6.28f, 0);
            node.Visible = false;
            _sacks[peers[i]] = new Sack { Peer = peers[i], Node = node, Floor = floor, Size = size, Delay = 0.25f + 0.18f * i, Fall = 0f };
        }
    }

    /// <summary>The Goblin's right hand carries this sack right now (set by the rig's timeline: reach, lift, hold).</summary>
    public bool Holds(int peer) { return _sacks.TryGetValue(peer, out var s) && s.Held; }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.1f); _t += dt;
        var g = Game.I; var w = g.Weigh;
        bool weighing = g.Phase == GamePhase.Weighing && w != null;
        if (!weighing && _sacks.Count > 0) { foreach (var s in _sacks.Values) s.Node.QueueFree(); _sacks.Clear(); }
        double now = Time.GetTicksMsec() / 1000.0;
        int holder = -1; bool grabbed = false;
        if (weighing && w.CurPeer >= 0 && (w.Stage == "reveal" || w.Stage == "verdict" || w.Stage == "throw"))
        {
            holder = w.CurPeer;
            grabbed = w.Stage != "reveal" || now - w.CurAt >= w.GrabDelay;
        }
        if (holder >= 0 && grabbed && _lastGrab != $"{w.Round}:{holder}") { _lastGrab = $"{w.Round}:{holder}"; Util.Pass("sack_in_palm", $"index={w.Revealed.Count - 1} name={Net.I.NameOf(holder)}"); }
        bool deflate = weighing && (w.Stage == "throw" || w.Stage == "remain");
        foreach (var s in _sacks.Values)
        {
            s.Held = s.Peer == holder && grabbed;
            if (s.Delay > 0f) { s.Delay -= dt; if (s.Delay <= 0f) { s.Node.Visible = true; s.Vy = 0f; } else continue; }
            if (deflate) s.Deflate = Mathf.Min(1f, s.Deflate + dt * 1.6f);
            float sy = s.Size * (1f - 0.7f * s.Deflate), sxz = s.Size * (1f + 0.22f * s.Deflate);
            s.Node.Scale = new Vector3(sxz, sy, sxz);
            if (s.Held && PalmValid)
            {
                var target = PalmWorld + new Vector3(0, 0.04f, 0);
                s.Node.GlobalPosition = s.Node.GlobalPosition.Lerp(target, Mathf.Min(1f, dt * 16f));
                s.Vy = 0f;
            }
            else
            {
                // falls back into its cart (also when the Goblin lets go of it)
                var p = s.Node.GlobalPosition;
                if (p.Y > s.Floor.Y + 0.001f || s.Vy > 0f)
                {
                    s.Vy -= 24f * dt; p.Y += s.Vy * dt;
                    p.X = Mathf.Lerp(p.X, s.Floor.X, Mathf.Min(1f, dt * 6f)); p.Z = Mathf.Lerp(p.Z, s.Floor.Z, Mathf.Min(1f, dt * 6f));
                    if (p.Y <= s.Floor.Y) { p.Y = s.Floor.Y; s.Vy = s.Vy < -4f ? -s.Vy * 0.28f : 0f; if (s.Vy > 0f) s.Fall = 1f; else s.Fall = 0f; }
                    s.Node.GlobalPosition = p;
                }
                else if (s.Fall > 0.5f) { s.Fall = 0f; }
            }
        }

        // the number above the palm (counts up; large and readable)
        bool show = weighing && holder >= 0 && PalmValid && _sacks.ContainsKey(holder) && _sacks[holder].Held && w.Stage != "remain";
        _num.Visible = show; _name.Visible = show; _numLight.Visible = show;
        if (show)
        {
            var sack = _sacks[holder];
            _num.GlobalPosition = PalmWorld + new Vector3(0, 1.7f + sack.Size * 1.2f, 0);
            _name.GlobalPosition = PalmWorld + new Vector3(0, 0.55f + sack.Size * 1.2f, 0);
            _numLight.GlobalPosition = PalmWorld + new Vector3(0, 1.5f, 0.8f);
            int shown = w.Stage == "reveal" ? w.ShownWeight(now) : w.CurWeight;
            _num.Text = shown.ToString();
            _name.Text = Net.I.NameOf(holder);
            if (w.Stage == "reveal" && shown == w.CurWeight && _lastNumber != $"{w.Round}:{holder}") { _lastNumber = $"{w.Round}:{holder}"; Util.Pass("palm_number", $"index={w.Revealed.Count - 1} name={Net.I.NameOf(holder)} weight={shown}"); }
            bool counting = w.Stage == "reveal" && now - w.CurAt - w.CountDelay < w.CountSeconds;
            _num.Modulate = counting ? new Color(1f, 0.9f, 0.45f) : (w.CurWeight == 0 ? new Color(1f, 0.5f, 0.45f) : new Color(1f, 1f, 0.75f));
            float pop = counting ? 1f : 1f + 0.12f * Mathf.Max(0f, 1f - (float)(now - w.CurAt - w.CountDelay - w.CountSeconds) * 3f);
            _num.Scale = Vector3.One * pop;
        }
        _shaftLight.LightEnergy = 1.3f * (0.85f + 0.15f * Mathf.Sin(_t * 2.1f) + 0.06f * Mathf.Sin(_t * 7.7f));
        ((StandardMaterial3D)_shaftGlow.MaterialOverride).AlbedoColor = new Color(0.9f, 0.1f, 0.04f, 0.2f + 0.12f * Mathf.Sin(_t * 1.6f));
    }

    // ------------------------------------------------------------------ the shaft
    void BuildShaft(CaveInfo cave)
    {
        var shaft = new Node3D { Name = "Shaft", TopLevel = true };
        AddChild(shaft);
        shaft.GlobalPosition = cave.ShaftPos;
        var stone = new Color(0.27f, 0.26f, 0.26f);
        var m = new GMesh { Variation = 0.1f };
        // wellhead: a parapet ring of chunky stones around a black pit
        const int N = 12; const float R = 1.75f, H = 1.1f;
        for (int i = 0; i < N; i++)
        {
            float a = i * Mathf.Tau / N;
            m.Box(new Vector3(Mathf.Cos(a) * R, H * 0.5f, Mathf.Sin(a) * R), new Vector3(0.62f, H, 1.15f), stone * (0.9f + 0.2f * ((i * 7) % 5) / 4f), new Vector3(0, -a, 0));
        }
        m.Tube(new Vector3(0, H, 0), new Vector3(0, H + 0.12f, 0), 2.35f, 2.25f, stone * 1.25f, N, 1f, false, false);   // lip
        for (int i = 0; i < N; i++)
        {
            float a = (i + 0.5f) * Mathf.Tau / N;
            m.Box(new Vector3(Mathf.Cos(a) * 2.28f, 0.1f, Mathf.Sin(a) * 2.28f), new Vector3(0.5f, 0.08f, 0.35f), new Color(0.35f, 0.07f, 0.05f), new Vector3(0, -a, 0));   // dull red floor studs
        }
        m.Instance(shaft, null, "Well");
        var pit = new StandardMaterial3D { AlbedoColor = new Color(0.01f, 0.005f, 0.005f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        shaft.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.3f, BottomRadius = 1.3f, Height = 0.03f, RadialSegments = 20, Rings = 1 }, MaterialOverride = pit, Position = new Vector3(0, 0.04f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        var glowMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0.9f, 0.1f, 0.04f, 0.3f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _shaftGlow = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.2f, BottomRadius = 0.4f, Height = 1.0f, RadialSegments = 16, Rings = 1 }, MaterialOverride = glowMat, Position = new Vector3(0, 0.55f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        shaft.AddChild(_shaftGlow);
        _shaftLight = new OmniLight3D { Position = new Vector3(0, 0.5f, 0), LightColor = new Color(1f, 0.14f, 0.06f), LightEnergy = 1.3f, OmniRange = 6.5f, OmniAttenuation = 1.2f };
        shaft.AddChild(_shaftLight);
        // nobody falls in: the parapet is solid up to head height
        var body = new StaticBody3D { Name = "ShaftBody", CollisionLayer = Layers.World, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 2.2f, Height = 1.8f }, Position = new Vector3(0, 0.9f, 0) });
        shaft.AddChild(body);
    }
}
