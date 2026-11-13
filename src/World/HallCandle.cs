using Godot;

namespace AppStein;

/// <summary>
/// The round candle (v2.2): a big wax candle on a stone stump beside the Goblin's throne that burns down with the round (same fraction as the HUD dial / fuse), visible to everyone in the Hall.
/// Full at the start, a stub when the fuse is out. Pure dressing: built on every peer, reads Game.TimeLeft / Game.RoundTotal.
/// </summary>
public partial class HallCandle : Node3D
{
    public const float FullHeight = 3.6f, Radius = 0.46f;
    Node3D _wax, _flameRoot, _pool;
    OmniLight3D _light;
    MeshInstance3D _flame, _core;
    StandardMaterial3D _flameMat;
    float _t, _fraction = 1f;
    readonly float _seed = GD.Randf() * 10f;
    public float Fraction => _fraction;
    public float WaxHeight => Mathf.Max(0.18f, FullHeight * _fraction);

    public override void _Ready()
    {
        var stone = new Color(0.27f, 0.26f, 0.27f);
        var gm = new GMesh { Variation = 0.1f };
        // stone stump, stepped, with a drip tray
        gm.Tube(new Vector3(0, 0, 0), new Vector3(0, 0.45f, 0), 1.25f, 1.1f, stone, 10, 1f, false, true);
        gm.Tube(new Vector3(0, 0.45f, 0), new Vector3(0, 0.62f, 0), 0.95f, 0.9f, stone.Lightened(0.25f), 10, 1f, false, true);
        for (int i = 0; i < 6; i++) { float a = i * Mathf.Tau / 6f; gm.Box(new Vector3(Mathf.Cos(a) * 1.0f, 0.7f, Mathf.Sin(a) * 1.0f), new Vector3(0.35f, 0.18f, 0.28f), new Color(0.82f, 0.78f, 0.62f), new Vector3(0, -a, 0)); }   // old wax lumps
        gm.Instance(this, null, "Stump");
        // wax column (scaled in Y as it burns), warm cream, vertical drips
        _wax = new Node3D { Position = new Vector3(0, 0.62f, 0) };
        AddChild(_wax);
        var wm = new GMesh { Variation = 0.05f };
        var cream = new Color(0.92f, 0.86f, 0.68f);
        wm.Tube(new Vector3(0, 0, 0), new Vector3(0, 1f, 0), Radius, Radius * 0.93f, cream, 12, 1f, false, true);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.26f + 0.4f, h = 0.35f + 0.5f * ((i * 37) % 10) / 10f;
            wm.Tube(new Vector3(Mathf.Cos(a) * Radius * 0.96f, 1f, Mathf.Sin(a) * Radius * 0.96f), new Vector3(Mathf.Cos(a) * Radius * 1.03f, 1f - h * 0.3f, Mathf.Sin(a) * Radius * 1.03f), 0.06f, 0.075f, cream.Darkened(0.04f), 5, 1f, false, true);
        }
        var wi = wm.Instance(_wax, null, "Wax");
        wi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        // molten pool and wick on top
        _pool = new Node3D(); AddChild(_pool);
        _pool.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = Radius * 0.88f, BottomRadius = Radius * 0.88f, Height = 0.03f, RadialSegments = 12, Rings = 1 }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1f, 0.82f, 0.45f), EmissionEnabled = true, Emission = new Color(1f, 0.6f, 0.2f), EmissionEnergyMultiplier = 0.9f }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        _pool.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.05f, 0.26f, 0.05f) }, MaterialOverride = Util.Mat(new Color(0.08f, 0.06f, 0.05f)), Position = new Vector3(0, 0.12f, 0) });
        // flame: a bright teardrop (emissive) with a blue-white core and a warm light that flickers
        _flameRoot = new Node3D(); _pool.AddChild(_flameRoot);
        _flameMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(1f, 0.62f, 0.15f, 0.9f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        _flame = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.2f, Height = 0.7f, RadialSegments = 8, Rings = 4 }, MaterialOverride = _flameMat, Position = new Vector3(0, 0.55f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _flameRoot.AddChild(_flame);
        _core = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.11f, Height = 0.4f, RadialSegments = 8, Rings = 4 }, MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.95f, 0.75f) }, Position = new Vector3(0, 0.43f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        _flameRoot.AddChild(_core);
        _light = new OmniLight3D { LightColor = new Color(1f, 0.66f, 0.28f), LightEnergy = 2.4f, OmniRange = 13f, OmniAttenuation = 1.1f, ShadowEnabled = false };
        _flameRoot.AddChild(_light); _light.Position = new Vector3(0, 0.9f, 0);
        // the stump blocks the way (a solid cylinder)
        var body = new StaticBody3D { Name = "CandleBody", CollisionLayer = Layers.World, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 1.25f, Height = 1.2f }, Position = new Vector3(0, 0.6f, 0) });
        AddChild(body);
        Apply(1f);
    }

    void Apply(float f)
    {
        _fraction = Mathf.Clamp(f, 0f, 1f);
        float h = WaxHeight;
        _wax.Scale = new Vector3(1f, h, 1f);
        _pool.Position = new Vector3(0, 0.62f + h, 0);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        var g = Game.I;
        if (g == null) return;
        float f = g.Phase == GamePhase.Round ? (g.RoundTotal > 1f ? g.TimeLeft / g.RoundTotal : 1f) : (g.Phase == GamePhase.Weighing ? 0f : 1f);
        Apply(Mathf.MoveToward(_fraction, Mathf.Clamp(f, 0f, 1f), (float)delta * 0.5f + Mathf.Abs(f - _fraction) * (float)delta * 3f));
        // the flame gutters harder as it burns low, and is a short ember when it is out (the Weighing)
        bool out_ = g.Phase != GamePhase.Round;
        float k = out_ ? 0.25f : 1f;
        float fl = 1f + 0.12f * Mathf.Sin(_t * 11f + _seed) + 0.08f * Mathf.Sin(_t * 23.7f + _seed * 2f) + 0.05f * Mathf.Sin(_t * 41f);
        float urgency = _fraction < 0.17f ? 1f + 0.25f * Mathf.Sin(_t * 30f) : 1f;
        _flameRoot.Scale = new Vector3(0.9f + 0.1f * fl, fl * k, 0.9f + 0.1f * fl) * (0.8f + 0.5f * Mathf.Clamp(_fraction * 3f, 0f, 1f)) * urgency;
        _flameRoot.Rotation = new Vector3(0.05f * Mathf.Sin(_t * 3.3f + _seed), 0, 0.05f * Mathf.Sin(_t * 2.7f));
        _light.LightEnergy = (2.0f + 0.5f * fl) * (out_ ? 0.2f : 1f);
    }
}
