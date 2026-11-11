using Godot;

namespace AppStein;

/// <summary>Meshes for held items and first-person hands. Used by Miner.SetHeld / Miner._Ready. Local space of the tool node:
/// the grip is at the origin; pickaxe handle runs along +Z with the head at -Z; the lantern hangs below (-Y).</summary>
public static class HeldVisuals
{
    static readonly Color Steel = new(0.62f, 0.64f, 0.70f);
    static readonly Color Brass = new(0.72f, 0.54f, 0.2f);
    static readonly Color Leather = new(0.30f, 0.21f, 0.13f);
    static readonly Color Sleeve = new(0.40f, 0.30f, 0.22f);

    static MeshInstance3D Box(Node3D p, Vector3 size, Vector3 pos, Color c, float emission = 0f, Vector3? rot = null)
    {
        var m = Util.BoxMesh(size, c, emission); m.Position = pos; if (rot.HasValue) m.Rotation = rot.Value; p.AddChild(m); return m;
    }

    public static void Pickaxe(Node3D toolRoot, Color handle)
    {
        var tool = new Node3D { Scale = Vector3.One * 0.75f }; toolRoot.AddChild(tool);
        Box(tool, new Vector3(0.05f, 0.05f, 0.85f), new Vector3(0, 0, 0.08f), handle);
        Box(tool, new Vector3(0.055f, 0.055f, 0.1f), new Vector3(0, 0, 0.5f), handle.Darkened(0.35f)); // wrapped end
        Box(tool, new Vector3(0.09f, 0.11f, 0.12f), new Vector3(0, 0, -0.33f), Steel.Darkened(0.2f)); // socket
        // two prongs bending forward: one pick, one chisel
        foreach (float sx in new[] { 1f, -1f })
        {
            Box(tool, new Vector3(0.14f, 0.045f, 0.06f), new Vector3(0.1f * sx, 0, -0.34f), Steel);
            Box(tool, new Vector3(0.12f, 0.04f, 0.05f), new Vector3(0.21f * sx, 0, -0.31f), Steel, 0f, new Vector3(0, 0.5f * sx, 0));
        }
    }

    static StandardMaterial3D GlassMat()
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = new Color(1f, 0.78f, 0.35f, 0.45f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            EmissionEnabled = true, Emission = new Color(1f, 0.7f, 0.25f), EmissionEnergyMultiplier = 0.55f, Roughness = 0.3f,
        };
        return m;
    }

    /// <summary>A small brass lantern with a glass cage and a tiny flame (no big emissive blob). Returns its light.</summary>
    public static OmniLight3D Lantern(Node3D toolRoot)
    {
        var tool = new Node3D { Scale = Vector3.One * 0.8f }; toolRoot.AddChild(tool);
        // ring handle
        Box(tool, new Vector3(0.015f, 0.12f, 0.015f), new Vector3(-0.045f, -0.04f, 0), Brass, 0f, new Vector3(0, 0, 0.25f));
        Box(tool, new Vector3(0.015f, 0.12f, 0.015f), new Vector3(0.045f, -0.04f, 0), Brass, 0f, new Vector3(0, 0, -0.25f));
        Box(tool, new Vector3(0.1f, 0.015f, 0.015f), new Vector3(0, 0.02f, 0), Brass);
        // cap, cage, base
        var cap = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.085f, Height = 0.06f, RadialSegments = 8, Rings = 0 }, MaterialOverride = Util.Mat(Brass) };
        cap.Position = new Vector3(0, -0.1f, 0); tool.AddChild(cap);
        Box(tool, new Vector3(0.11f, 0.2f, 0.11f), new Vector3(0, -0.23f, 0), Colors.White).MaterialOverride = GlassMat();
        foreach (var (x, z) in new[] { (-0.055f, -0.055f), (0.055f, -0.055f), (-0.055f, 0.055f), (0.055f, 0.055f) })
            Box(tool, new Vector3(0.014f, 0.21f, 0.014f), new Vector3(x, -0.23f, z), Brass);
        Box(tool, new Vector3(0.13f, 0.035f, 0.13f), new Vector3(0, -0.345f, 0), Brass.Darkened(0.2f));
        var flame = Util.SphereMesh(0.022f, new Color(1f, 0.85f, 0.35f), 1.6f); flame.Position = new Vector3(0, -0.27f, 0); flame.Scale = new Vector3(1, 1.6f, 1); tool.AddChild(flame);
        var light = new OmniLight3D { LightColor = new Color(1f, 0.82f, 0.5f), LightEnergy = 1.6f, OmniRange = 14f, OmniAttenuation = 1.2f, ShadowEnabled = false, Position = new Vector3(0, 0.45f, -0.25f) };
        toolRoot.AddChild(light);
        return light;
    }

    /// <summary>First-person right hand + forearm (child "Hand" of the tool node, survives SetHeld clearing).</summary>
    public static Node3D RightHandFp(Node3D tool)
    {
        var h = new Node3D { Name = "Hand" };
        Box(h, new Vector3(0.1f, 0.09f, 0.11f), new Vector3(0, 0.01f, 0.0f), Leather);        // glove
        Box(h, new Vector3(0.1f, 0.03f, 0.06f), new Vector3(0, 0.05f, 0.08f), Leather.Darkened(0.2f)); // knuckle/thumb
        Box(h, new Vector3(0.07f, 0.07f, 0.4f), new Vector3(0.03f, -0.06f, 0.25f), Sleeve, 0f, new Vector3(0.2f, -0.12f, 0));
        Box(h, new Vector3(0.1f, 0.1f, 0.05f), new Vector3(0.012f, -0.035f, 0.085f), Leather.Darkened(0.35f), 0f, new Vector3(0.2f, -0.12f, 0)); // cuff
        tool.AddChild(h);
        return h;
    }

    public static Node3D LeftHandFp()
    {
        var h = new Node3D { Position = new Vector3(-0.3f, -0.32f, -0.5f), Rotation = new Vector3(0.05f, 0.35f, 0) };
        Box(h, new Vector3(0.1f, 0.09f, 0.11f), new Vector3(0, 0, 0), Leather);
        Box(h, new Vector3(0.07f, 0.07f, 0.4f), new Vector3(-0.03f, -0.06f, 0.25f), Sleeve, 0f, new Vector3(0.2f, 0.12f, 0));
        Box(h, new Vector3(0.1f, 0.1f, 0.05f), new Vector3(-0.012f, -0.035f, 0.085f), Leather.Darkened(0.35f), 0f, new Vector3(0.2f, 0.12f, 0));
        return h;
    }
}
