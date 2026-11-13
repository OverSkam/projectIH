using System;
using Godot;

namespace AppStein;

/// <summary>Small mesh / material helpers for the loot effects. Only features that exist in both renderers (no glow, no SDFGI, no volumetric fog).</summary>
public static class LootVis
{
    static GradientTexture2D _radial;
    static readonly System.Collections.Generic.Dictionary<string, StandardMaterial3D> _add = new();

    /// <summary>Soft radial falloff texture (white centre, transparent edge) for light halos.</summary>
    public static Texture2D Radial
    {
        get
        {
            if (_radial != null) return _radial;
            var g = new Gradient();
            g.SetColor(0, new Color(1, 1, 1, 1)); g.SetColor(1, new Color(1, 1, 1, 0));
            g.AddPoint(0.35f, new Color(1, 1, 1, 0.45f));
            _radial = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(0.5f, 0.5f), FillTo = new Vector2(1f, 0.5f), Width = 128, Height = 128 };
            return _radial;
        }
    }

    /// <summary>Unshaded additive material (light that only adds to the scene). Not cached when tweened: pass fresh = true.</summary>
    public static StandardMaterial3D Additive(Color c, bool fresh = false, Texture2D tex = null)
    {
        string key = c.ToHtml() + (tex != null ? "t" : "");
        if (!fresh && _add.TryGetValue(key, out var m0)) return m0;
        var m = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = c, CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = false,
        };
        if (tex != null) { m.AlbedoTexture = tex; }
        if (!fresh) _add[key] = m;
        return m;
    }

    /// <summary>A camera-facing glowing halo (a quad with the radial texture, additive).</summary>
    public static MeshInstance3D Halo(Color c, float size, float alpha, Vector3 pos)
    {
        var mat = Additive(new Color(c.R, c.G, c.B, alpha), true, Radial);
        mat.BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled;
        var mi = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(size, size) }, MaterialOverride = mat, Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        return mi;
    }

    public static void SetAlpha(MeshInstance3D mi, float a)
    {
        if (mi.MaterialOverride is StandardMaterial3D m) m.AlbedoColor = new Color(m.AlbedoColor.R, m.AlbedoColor.G, m.AlbedoColor.B, a);
    }

    /// <summary>Plays at a world position on every peer; no-ops headless.</summary>
    public static void Sound(string name, Vector3 pos, float db = 0f) => Sfx.Play(name, pos, db);

    /// <summary>One-shot particle burst (CPU particles work in both renderers). Frees itself.</summary>
    public static CpuParticles3D Sparks(Node parent, Vector3 pos, Color c, int amount, float speed, float life, float size, float gravity = 7f, float spreadDeg = 180f, Vector3? dir = null)
    {
        var p = new CpuParticles3D
        {
            Emitting = false, OneShot = true, Amount = amount, Lifetime = life, Explosiveness = 0.95f, Direction = dir ?? Vector3.Up, Spread = spreadDeg,
            InitialVelocityMin = speed * 0.45f, InitialVelocityMax = speed, Gravity = new Vector3(0, -gravity, 0), ScaleAmountMin = 0.6f, ScaleAmountMax = 1.2f,
            Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var mat = Additive(new Color(c.R, c.G, c.B, 1f), false);
        p.Mesh = new QuadMesh { Size = new Vector2(size, size), Material = null };
        var qm = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            AlbedoColor = c, AlbedoTexture = Radial, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = false,
        };
        ((QuadMesh)p.Mesh).Material = qm;
        parent.AddChild(p);
        p.Position = pos;
        p.Emitting = true;
        var t = p.GetTree().CreateTimer(life + 0.5f);
        t.Timeout += () => { if (GodotObject.IsInstanceValid(p)) p.QueueFree(); };
        return p;
    }

    public static Node3D World => Game.I?.World;
}
