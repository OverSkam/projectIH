using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>
/// The look of the dark (v2.1), built only from things both renderers have (Forward+ and Compatibility/opengl3), no volumetric fog:
///  - depth fog + a faint height fog (World environment),
///  - layered haze: a few slow-moving, lit, translucent fog cards per room and tunnel (the lantern lights them, so the beam visibly catches haze),
///  - drifting dust motes around the local camera (lit billboards: only visible where a light reaches them),
///  - "blindness" instead of pitch black: with the lantern off or out of oil the local player gets a very weak, short-range, desaturated light plus much denser fog,
///    so dim shapes show within about 2.5 m and it is smoothly black beyond. That light exists only on the local client (it is never replicated).
/// </summary>
public static class Atmosphere
{
    public const float FogLit = 0.052f, FogDark = 0.42f;
    static ImageTexture _soft, _dot;

    /// <summary>Small round soft dot for the dust motes.</summary>
    public static Texture2D SoftDot()
    {
        if (_dot != null) return _dot;
        const int N = 16;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float a = Mathf.Clamp(1f - Mathf.Sqrt(dx * dx + dy * dy), 0f, 1f);
                img.SetPixel(x, y, new Color(1, 1, 1, Mathf.Min(1f, a * 1.6f)));
            }
        _dot = ImageTexture.CreateFromImage(img);
        return _dot;
    }

    /// <summary>Soft round alpha blob (white, alpha falls off to the edge) used by the fog cards.</summary>
    static Texture2D Soft()
    {
        if (_soft != null) return _soft;
        const int N = 64;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp(1f - d, 0f, 1f); a = a * a * (3f - 2f * a);
                img.SetPixel(x, y, new Color(1, 1, 1, a));
            }
        _soft = ImageTexture.CreateFromImage(img);
        return _soft;
    }

    /// <summary>Fog cards in every room and tunnel of the cave (visuals only, deterministic per cave, built on every peer).</summary>
    public static void BuildFogCards(Node3D parent, CaveInfo cave)
    {
        var mgr = new FogDrift { Name = "FogCards" };
        parent.AddChild(mgr);
        var mat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoTexture = Soft(), AlbedoColor = new Color(0.66f, 0.70f, 0.80f, 0.30f), Roughness = 1f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha, DistanceFadeMinDistance = 0.35f, DistanceFadeMaxDistance = 1.8f,
        };
        var quad = new QuadMesh { Size = Vector2.One };
        var rng = new Rnd(cave.Rooms.Count * 7919L + (long)(cave.Rooms.Count > 0 ? cave.Rooms[^1].Center.X : 0) + 11);
        void Card(Vector3 pos, Vector2 size, bool horizontal, float yaw, float alpha)
        {
            var mi = new MeshInstance3D { Mesh = quad, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 26f, Position = pos, Scale = new Vector3(size.X, size.Y, 1f) };
            mi.Rotation = horizontal ? new Vector3(-Mathf.Pi / 2f, yaw, 0f) : new Vector3(0f, yaw, 0f);
            mi.SetInstanceShaderParameter("dummy", 0f);
            parent.AddChild(mi);
            mgr.Add(mi, pos, yaw, rng.F() * 100f, horizontal);
        }
        foreach (var r in cave.Rooms)
        {
            bool hall = r.Name == "camp";
            int n = hall ? 7 : 5;
            float spread = hall ? 8.5f : 4.5f;
            for (int i = 0; i < n; i++)
            {
                bool horizontal = i % 2 == 1;
                var p = r.Center + new Vector3(rng.Range(-spread, spread), horizontal ? rng.Range(0.35f, 1.5f) : rng.Range(0.9f, 1.8f), rng.Range(-spread, spread));
                float w = hall ? rng.Range(6f, 10f) : rng.Range(3.8f, 6.5f);
                Card(p, new Vector2(w, horizontal ? w * rng.Range(0.6f, 1f) : rng.Range(1.6f, 2.4f)), horizontal, rng.Range(0f, Mathf.Pi), 1f);
            }
        }
        var mc = ModularCave.Last;
        if (mc != null)
            foreach (var t in mc.Tunnels)
            {
                var c = new Vector3(t.X, 0, t.Z);
                float yaw = t.AlongX ? Mathf.Pi / 2f : 0f;
                Card(c + new Vector3(0, rng.Range(0.55f, 1.2f), 0), new Vector2(t.Hw * 2f + 0.5f, 3.4f), true, t.AlongX ? 0f : Mathf.Pi / 2f, 1f);
                Card(c + new Vector3(0, 1.1f, 0), new Vector2(t.Hw * 2f + 0.8f, 1.3f), false, yaw, 1f);
            }
    }

    /// <summary>Environment of a cave world: black background, no ambient light, depth fog + faint height fog (denser near the floor).</summary>
    public static Godot.Environment MakeEnvironment() => new()
    {
        BackgroundMode = Godot.Environment.BGMode.Color,
        BackgroundColor = Colors.Black,
        AmbientLightSource = Godot.Environment.AmbientSource.Color,
        AmbientLightColor = Colors.Black,
        AmbientLightEnergy = 0f,
        TonemapMode = Godot.Environment.ToneMapper.Filmic,
        FogEnabled = true,
        FogLightColor = new Color(0.010f, 0.012f, 0.020f),
        FogDensity = FogLit,
        FogHeight = 1.6f,
        FogHeightDensity = 0.10f,
        AdjustmentEnabled = true,
        AdjustmentSaturation = 1f,
    };
}

/// <summary>Moves all fog cards in one place (a slow drift, never more than about half a metre).</summary>
public partial class FogDrift : Node
{
    readonly List<(MeshInstance3D node, Vector3 basePos, float yaw, float phase, bool flat)> _cards = new();
    float _t;
    public void Add(MeshInstance3D n, Vector3 p, float yaw, float phase, bool flat) => _cards.Add((n, p, yaw, phase, flat));
    public override void _Process(double delta)
    {
        _t += (float)delta;
        foreach (var (n, b, yaw, ph, flat) in _cards)
        {
            n.Position = b + new Vector3(Mathf.Sin(_t * 0.11f + ph) * 0.55f, Mathf.Sin(_t * 0.07f + ph * 1.7f) * 0.12f, Mathf.Cos(_t * 0.09f + ph * 0.6f) * 0.55f);
            float y = yaw + Mathf.Sin(_t * 0.05f + ph) * 0.25f;
            n.Rotation = flat ? new Vector3(-Mathf.Pi / 2f, y, 0f) : new Vector3(Mathf.Sin(_t * 0.04f + ph) * 0.06f, y, 0f);
        }
    }
}

/// <summary>
/// Local-only atmosphere around a camera: drifting dust motes and the "blindness" rig. Child of the local miner's camera (or the debug camera).
/// <see cref="Lit"/> tells whether the local light (lantern) is burning.
/// </summary>
public partial class LocalAtmosphere : Node3D
{
    public Func<bool> Lit = () => true;
    CpuParticles3D _dust;
    OmniLight3D _blind;
    float _dbg;
    float _dark;   // 0 = lit, 1 = fully dark (smoothed)
    Godot.Environment _env;

    public override void _Ready()
    {
        _env = Game.I?.World?.Env;
        // dust motes: tiny lit billboards in a box around the viewer; they stay where they are when the viewer moves on (world space)
        var mat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoTexture = Atmosphere.SoftDot(), AlbedoColor = new Color(0.95f, 0.9f, 0.78f, 0.55f), Roughness = 1f, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha, DistanceFadeMinDistance = 0.3f, DistanceFadeMaxDistance = 0.9f,
        };
        _dust = new CpuParticles3D
        {
            Amount = 1400, Lifetime = 18f, Preprocess = 18f, LocalCoords = false, TopLevel = true, Emitting = true,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(7f, 2.2f, 7f),
            Direction = Vector3.Up, Spread = 180f, InitialVelocityMin = 0.01f, InitialVelocityMax = 0.09f, Gravity = new Vector3(0, 0.012f, 0),
            Mesh = new QuadMesh { Size = new Vector2(0.07f, 0.07f) }, MaterialOverride = mat, ScaleAmountMin = 0.5f, ScaleAmountMax = 1.6f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = Vector3.Zero,
        };
        AddChild(_dust);
        // blindness: a weak short-range neutral light that exists only on this client
        _blind = new OmniLight3D { LightColor = new Color(0.74f, 0.77f, 0.84f), LightEnergy = 0f, OmniRange = 3.6f, OmniAttenuation = 1.0f, ShadowEnabled = false, Visible = false };
        AddChild(_blind);
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, 0.1f);
        var cam = GetParent<Node3D>();
        if (cam == null) return;
        _dust.GlobalPosition = cam.GlobalPosition + new Vector3(0, 0.3f, 0);
        _dust.GlobalRotation = Vector3.Zero;
        var cave = Game.I?.World?.Cave;
        bool inHall = cave != null && cave.InHall(cam.GlobalPosition);
        bool dark = !Lit() && !inHall;
        _dark = Mathf.MoveToward(_dark, dark ? 1f : 0f, dt * (dark ? 1.6f : 3.5f));
        _blind.Visible = _dark > 0.01f;
        if (Util.HasArg("atmodebug") && (_dbg -= dt) <= 0) { _dbg = 1f; Util.Log($"ATMO dark={_dark:0.00} lit={Lit()} hall={inHall} light={_blind.LightEnergy:0.00} vis={_blind.Visible} env={_env != null} fog={_env?.FogDensity}"); }
        _blind.LightEnergy = 1.1f * _dark;
        if (_env != null)
        {
            _env.FogDensity = Mathf.Lerp(Atmosphere.FogLit, Atmosphere.FogDark, _dark);
            _env.AdjustmentSaturation = Mathf.Lerp(1f, 0.25f, _dark);
        }
    }
}
