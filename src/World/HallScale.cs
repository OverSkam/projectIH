using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>
/// The giant two-pan balance Scale in front of the Goblin, and the shaft beside it (a stone well with a faint red glow from below).
/// Built on every peer from CaveInfo.ScalePos / ShaftPos; driven by Game.Weigh (the replicated ceremony state). Contract: <see cref="Show"/> a name and a weight
/// (the beam tilts with the weight, the left pan's pile grows, a big number floats above), <see cref="Idle"/> when nothing is on the Scale.
/// The right pan carries the Goblin's own fixed counterweights.
/// </summary>
public partial class HallScale : Node3D
{
    const float BeamY = 2.9f, ArmLen = 3.0f, Drop = 1.9f, MaxTilt = 0.24f, PostX = 5.9f;
    Node3D _beam;
    Node3D _panL, _panR;
    readonly List<Node3D> _pile = new();
    readonly Node3D[] _chainL = new Node3D[3], _chainR = new Node3D[3];
    readonly Vector3[] _rimL = new Vector3[3], _rimR = new Vector3[3];
    Label3D _num, _name;
    OmniLight3D _light, _shaftLight;
    MeshInstance3D _shaftGlow;
    float _tilt, _tiltTarget, _shown, _shownTarget, _t;
    string _lastName = "";

    static readonly Color[] Ores = { new(0.25f, 0.25f, 0.27f), new(0.62f, 0.38f, 0.22f), new(0.75f, 0.78f, 0.82f), new(0.95f, 0.78f, 0.2f), new(0.85f, 0.15f, 0.2f), new(0.5f, 0.9f, 1f) };

    public override void _Ready()
    {
        var cave = Game.I.World.Cave;
        Position = cave.ScalePos;
        var iron = new Color(0.20f, 0.20f, 0.23f); var brass = new Color(0.62f, 0.46f, 0.18f); var stone = new Color(0.30f, 0.30f, 0.29f);

        // a gantry instead of a centre pillar (so the Goblin's face stays visible behind the Scale): low platform, two posts, a top bar, the beam hangs from one chain
        var fix = new GMesh { Variation = 0.08f };
        fix.Tube(new Vector3(0, 0, 0), new Vector3(0, 0.25f, 0), 2.2f, 2.0f, stone, 12);
        fix.Tube(new Vector3(0, 0.25f, 0), new Vector3(0, 0.3f, 0), 1.9f, 1.9f, stone * 1.15f, 12);
        foreach (float sx in new[] { -PostX, PostX })
        {
            fix.Tube(new Vector3(sx, 0, 0), new Vector3(sx, 0.7f, 0), 0.95f, 0.75f, stone, 8);
            fix.Tube(new Vector3(sx, 0.7f, 0), new Vector3(sx, 3.4f, 0), 0.42f, 0.3f, iron, 8);
            fix.Ellipsoid(new Vector3(sx, 3.5f, 0), new Vector3(0.5f, 0.5f, 0.5f), brass, 8, 5);
        }
        fix.Box(new Vector3(0, 3.45f, 0), new Vector3(PostX * 2f, 0.35f, 0.35f), iron * 1.1f);
        for (float y = BeamY + 0.3f; y < 3.3f; y += 0.36f)
            fix.Box(new Vector3(0, y, 0), (int)(y * 3f) % 2 == 0 ? new Vector3(0.09f, 0.3f, 0.2f) : new Vector3(0.2f, 0.3f, 0.09f), iron);
        fix.Ellipsoid(new Vector3(0, BeamY, 0), new Vector3(0.3f, 0.3f, 0.3f), brass, 8, 4);
        fix.Instance(this, null, "Gantry");
        var body = new StaticBody3D { Name = "ScaleBody", CollisionLayer = Layers.World, CollisionMask = 0 };
        foreach (float sx in new[] { -PostX, PostX }) body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.9f, Height = 3f }, Position = new Vector3(sx, 1.5f, 0) });
        AddChild(body);

        // beam (rotates to tilt) with a gold ornament and two end knobs
        _beam = new Node3D { Position = new Vector3(0, BeamY, 0), Name = "Beam" };
        AddChild(_beam);
        var bm = new GMesh { Variation = 0.06f };
        bm.Box(Vector3.Zero, new Vector3(ArmLen * 2f + 0.4f, 0.32f, 0.3f), brass);
        bm.Tube(new Vector3(-ArmLen - 0.2f, 0, 0), new Vector3(-ArmLen - 0.5f, 0, 0), 0.22f, 0.12f, brass * 1.2f, 6);
        bm.Tube(new Vector3(ArmLen + 0.2f, 0, 0), new Vector3(ArmLen + 0.5f, 0, 0), 0.22f, 0.12f, brass * 1.2f, 6);
        bm.Box(new Vector3(0, 0.32f, 0), new Vector3(0.5f, 0.5f, 0.38f), brass * 1.15f, new Vector3(0, 0, 0.785f));
        bm.Instance(_beam);

        _panL = MakePan(false); _panR = MakePan(true);
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.Tau / 3f + 0.5f;
            _rimL[i] = _rimR[i] = new Vector3(Mathf.Cos(a) * 1.35f, 0.15f, Mathf.Sin(a) * 1.35f);
            _chainL[i] = MakeChain(); _chainR[i] = MakeChain();
        }

        // big number floating above the beam, and the name under it
        _num = new Label3D
        {
            Text = "", PixelSize = 0.0075f, FontSize = 200, Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Position = new Vector3(0, 4.3f, 0),
            Modulate = new Color(1f, 0.88f, 0.4f), OutlineSize = 36, OutlineModulate = new Color(0, 0, 0, 0.95f), Font = UiTheme.Chunky,
        };
        AddChild(_num);
        _name = new Label3D
        {
            Text = "", PixelSize = 0.0042f, FontSize = 160, Billboard = BaseMaterial3D.BillboardModeEnum.FixedY, Position = new Vector3(0, 3.85f, 0),
            Modulate = new Color(0.95f, 0.92f, 0.8f), OutlineSize = 28, OutlineModulate = new Color(0, 0, 0, 0.95f), Font = UiTheme.Chunky,
        };
        AddChild(_name);
        _light = new OmniLight3D { Position = new Vector3(0, 4.2f, 2.0f), LightColor = new Color(0.85f, 0.92f, 0.55f), LightEnergy = 0.6f, OmniRange = 13f, OmniAttenuation = 1.1f };
        AddChild(_light);
        BuildShaft(cave);
    }

    Node3D MakePan(bool right)
    {
        var pan = new Node3D { Name = right ? "PanR" : "PanL" };
        AddChild(pan);
        var brass = new Color(0.62f, 0.46f, 0.18f);
        var m = new GMesh { Variation = 0.08f };
        m.Tube(new Vector3(0, -0.45f, 0), new Vector3(0, 0, 0), 0.9f, 1.55f, brass * 0.85f, 12);          // shallow bowl
        m.Tube(new Vector3(0, -0.02f, 0), new Vector3(0, 0.08f, 0), 1.6f, 1.6f, brass * 1.15f, 12);        // rim
        m.Instance(pan);
        if (right)
        {
            // the Goblin's counterweights: a neat heap of iron blocks and gold bars
            var w = new GMesh { Variation = 0.1f };
            for (int i = 0; i < 9; i++)
            {
                float a = i * 2.4f, r = 0.15f + (i % 3) * 0.32f, h = 0.12f + (i / 3) * 0.3f;
                w.Box(new Vector3(Mathf.Cos(a) * r, 0.2f + h, Mathf.Sin(a) * r), new Vector3(0.55f, 0.36f, 0.4f), i % 4 == 0 ? new Color(0.9f, 0.72f, 0.18f) : new Color(0.22f, 0.22f, 0.25f), new Vector3(0, a, 0));
            }
            w.Instance(pan);
        }
        else
        {
            // the pile: up to 36 little stones in a heap; shown one by one as the weight grows
            var rng = new Rnd(1234);
            for (int i = 0; i < 36; i++)
            {
                float a = i * 2.39996f, r = Mathf.Sqrt(i + 1f) * 0.2f, h = Mathf.Max(0f, 0.9f - r * 0.5f);
                var st = new GMesh { Variation = 0.12f };
                st.Ellipsoid(Vector3.Zero, new Vector3(0.2f + 0.1f * rng.F(), 0.16f + 0.08f * rng.F(), 0.2f + 0.1f * rng.F()), Ores[i % Ores.Length], 6, 4);
                var n = new Node3D { Position = new Vector3(Mathf.Cos(a) * Mathf.Min(r, 1.25f), 0.2f + h * 0.55f, Mathf.Sin(a) * Mathf.Min(r, 1.25f)), Visible = false };
                st.Instance(n); pan.AddChild(n); _pile.Add(n);
            }
        }
        return pan;
    }

    Node3D MakeChain()
    {
        var n = new Node3D();
        AddChild(n);
        var m = new GMesh { Variation = 0.05f };
        m.Tube(Vector3.Zero, new Vector3(0, 0, -1f), 0.045f, 0.045f, new Color(0.16f, 0.16f, 0.19f), 4, 1f, false, false);
        m.Instance(n);
        return n;
    }

    static void Link(Node3D rod, Vector3 a, Vector3 b)
    {
        var d = b - a; float len = d.Length(); if (len < 1e-3f) return;
        rod.Transform = new Transform3D(Basis.LookingAt(d / len, Mathf.Abs(d.Y / len) > 0.95f ? Vector3.Right : Vector3.Up) * Basis.FromScale(new Vector3(1, 1, len)), a);
    }

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

    /// <summary>Contract: show a pile (name + weight) on the Scale.</summary>
    public void Show(string name, int weight)
    {
        _num.Text = weight.ToString(); _name.Text = name; _lastName = name;
        _tiltTarget = MaxTilt * Mathf.Clamp(weight / 30f, 0f, 1f) * (weight > 0 ? 1f : 0f);
        _shownTarget = Mathf.Min(36f, weight * 0.9f + (weight > 0 ? 1f : 0f));
    }

    /// <summary>Nothing on the Scale.</summary>
    public void Idle() { _num.Text = ""; _name.Text = ""; _tiltTarget = 0f; _shownTarget = 0f; }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.1f); _t += dt;
        var g = Game.I; var w = g.Weigh;
        if (g.Phase == GamePhase.Weighing && w != null && w.Stage == "reveal" && w.CurPeer >= 0)
            Show(Net.I.NameOf(w.CurPeer), w.ShownWeight(Time.GetTicksMsec() / 1000.0));
        else Idle();
        float sway = Mathf.Sin(_t * 0.9f) * 0.008f;
        _tilt = Mathf.Lerp(_tilt, _tiltTarget, Mathf.Min(1f, dt * 2.5f));
        float ang = _tilt + sway;
        _beam.Rotation = new Vector3(0, 0, ang);
        var pivot = new Vector3(0, BeamY, 0);
        var endL = pivot + new Vector3(-Mathf.Cos(ang) * ArmLen, -Mathf.Sin(ang) * ArmLen, 0);
        var endR = pivot + new Vector3(Mathf.Cos(ang) * ArmLen, Mathf.Sin(ang) * ArmLen, 0);
        _panL.Position = endL + new Vector3(0, -Drop, 0); _panR.Position = endR + new Vector3(0, -Drop, 0);
        for (int i = 0; i < 3; i++)
        {
            Link(_chainL[i], endL, _panL.Position + _rimL[i]);
            Link(_chainR[i], endR, _panR.Position + _rimR[i]);
        }
        // the pile grows stone by stone
        _shown = Mathf.MoveToward(_shown, _shownTarget, dt * 26f);
        for (int i = 0; i < _pile.Count; i++)
        {
            float k = Mathf.Clamp(_shown - i, 0f, 1f);
            _pile[i].Visible = k > 0.02f; _pile[i].Scale = Vector3.One * k;
        }
        _shaftLight.LightEnergy = 1.3f * (0.85f + 0.15f * Mathf.Sin(_t * 2.1f) + 0.06f * Mathf.Sin(_t * 7.7f));
        ((StandardMaterial3D)_shaftGlow.MaterialOverride).AlbedoColor = new Color(0.9f, 0.1f, 0.04f, 0.2f + 0.12f * Mathf.Sin(_t * 1.6f));
    }
}
