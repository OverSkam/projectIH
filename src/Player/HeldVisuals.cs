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

    static readonly Color Glove = new(0.52f, 0.37f, 0.22f);
    static readonly Color WorkSleeve = new(0.24f, 0.30f, 0.40f);

    /// <summary>First-person right hand: a compact gloved fist at the tool origin (the item is built so its grip passes through the fist)
    /// and a short forearm leaving the screen at the bottom right.</summary>
    public static Node3D RightHandFp(Node3D tool)
    {
        var h = new Node3D { Name = "Hand", Scale = Vector3.One * 0.85f };
        Box(h, new Vector3(0.08f, 0.05f, 0.09f), new Vector3(0, 0.014f, 0.0f), Glove);                                  // back of the hand
        Box(h, new Vector3(0.085f, 0.04f, 0.03f), new Vector3(0, -0.012f, -0.055f), Glove.Darkened(0.12f));            // wrapped fingers
        Box(h, new Vector3(0.03f, 0.028f, 0.07f), new Vector3(-0.05f, 0.002f, -0.01f), Glove.Lightened(0.1f), 0f, new Vector3(0, 0.3f, 0)); // thumb
        Box(h, new Vector3(0.078f, 0.078f, 0.04f), new Vector3(0.004f, -0.004f, 0.07f), Glove.Darkened(0.4f));          // cuff
        Box(h, new Vector3(0.06f, 0.06f, 0.2f), new Vector3(0.03f, -0.085f, 0.17f), WorkSleeve, 0f, new Vector3(0.55f, -0.12f, 0)); // forearm, dives out of view at the bottom right
        tool.AddChild(h);
        return h;
    }

    static void Cyl(Node3D p, float r, float h, Vector3 pos, Color c, float emission = 0f, Vector3? rot = null)
    {
        var m = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = h, RadialSegments = 8 }, MaterialOverride = Util.Mat(c, emission), Position = pos };
        if (rot.HasValue) m.Rotation = rot.Value;
        p.AddChild(m);
    }

    /// <summary>First-person model of a held utility: its grip is the tool origin (inside the fist), nothing else is drawn.
    /// Returns the lantern light (or null) and the resting position/rotation of the tool node in head space.</summary>
    public static OmniLight3D BuildFp(Node3D tool, string id, Color col, out Vector3 pos, out Vector3 rest)
    {
        OmniLight3D light = null;
        var n = new Node3D(); tool.AddChild(n);
        switch (id)
        {
            case "pickaxe":
            {
                pos = new Vector3(0.30f, -0.31f, -0.50f); rest = new Vector3(0.5f, 0.35f, 0f);
                Box(n, new Vector3(0.04f, 0.04f, 0.78f), new Vector3(0, 0, -0.26f), col);                         // handle
                Box(n, new Vector3(0.048f, 0.048f, 0.12f), new Vector3(0, 0, 0.1f), col.Darkened(0.4f));          // wrapped end
                Box(n, new Vector3(0.08f, 0.09f, 0.09f), new Vector3(0, 0, -0.63f), Steel.Darkened(0.25f));       // socket
                foreach (float sx in new[] { 1f, -1f })
                {
                    Box(n, new Vector3(0.17f, 0.036f, 0.05f), new Vector3(0.12f * sx, -0.012f, -0.63f), Steel, 0f, new Vector3(0, 0, -0.3f * sx));
                    Box(n, new Vector3(0.07f, 0.03f, 0.04f), new Vector3(0.235f * sx, -0.05f, -0.63f), Steel.Lightened(0.15f), 0f, new Vector3(0, 0, -0.7f * sx));
                }
                break;
            }
            case "lantern":
            {
                pos = new Vector3(0.27f, -0.08f, -0.55f); rest = new Vector3(0, 0, 0);
                light = Lantern(n);
                break;
            }
            case "dynamite":
            {
                pos = new Vector3(0.28f, -0.27f, -0.50f); rest = new Vector3(-0.3f, 0.15f, 0f);
                foreach (var (x, z) in new[] { (-0.032f, 0f), (0.032f, 0f), (0f, -0.03f) }) Cyl(n, 0.03f, 0.26f, new Vector3(x, 0.03f, z), col);
                Box(n, new Vector3(0.11f, 0.03f, 0.1f), new Vector3(0, 0.1f, -0.01f), new Color(0.12f, 0.1f, 0.08f));   // tape band above the fist
                Box(n, new Vector3(0.008f, 0.1f, 0.008f), new Vector3(0, 0.22f, -0.03f), new Color(0.8f, 0.75f, 0.55f)); // fuse
                var tip = Util.SphereMesh(0.016f, new Color(1f, 0.6f, 0.15f), 2.5f); tip.Position = new Vector3(0, 0.275f, -0.03f); n.AddChild(tip);
                break;
            }
            case "rope":
            {
                pos = new Vector3(0.27f, -0.22f, -0.50f); rest = new Vector3(0, 0.2f, 0);
                n.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.065f, OuterRadius = 0.15f, Rings = 14, RingSegments = 6 }, MaterialOverride = Util.Mat(col), Position = new Vector3(0, -0.1f, 0), Rotation = new Vector3(Mathf.Pi / 2f, 0, 0.2f) });
                Box(n, new Vector3(0.025f, 0.12f, 0.025f), new Vector3(0.1f, -0.2f, 0.02f), col.Darkened(0.15f), 0f, new Vector3(0, 0, 0.3f)); // loose end
                break;
            }
            case "beartrap":
            {
                pos = new Vector3(0.27f, -0.28f, -0.50f); rest = new Vector3(0.6f, 0.2f, 0f);
                var iron = col.Darkened(0.1f);
                Cyl(n, 0.13f, 0.025f, new Vector3(0, 0, -0.14f), col.Darkened(0.35f));
                foreach (float sz in new[] { 1f, -1f })
                {
                    Box(n, new Vector3(0.28f, 0.02f, 0.03f), new Vector3(0, 0.025f, -0.14f + 0.06f * sz), iron);
                    for (int i = 0; i < 6; i++) Box(n, new Vector3(0.022f, 0.045f, 0.022f), new Vector3(-0.115f + i * 0.046f, 0.05f, -0.14f + 0.06f * sz), new Color(0.78f, 0.78f, 0.8f));
                }
                Box(n, new Vector3(0.05f, 0.02f, 0.05f), new Vector3(0, 0.03f, -0.14f), new Color(0.7f, 0.5f, 0.15f));  // trigger plate
                break;
            }
            case "map":
            {
                pos = new Vector3(0.24f, -0.26f, -0.52f); rest = new Vector3(0.05f, -0.2f, 0f); n.Scale = Vector3.One * 0.6f;
                var wood = new Color(0.45f, 0.3f, 0.15f);
                Box(n, new Vector3(0.3f, 0.22f, 0.008f), new Vector3(0, 0.14f, -0.02f), col, 0f, new Vector3(-0.2f, 0, 0));                 // sheet
                Box(n, new Vector3(0.31f, 0.022f, 0.022f), new Vector3(0, 0.255f, -0.05f), wood);                                            // top roller
                Box(n, new Vector3(0.31f, 0.022f, 0.022f), new Vector3(0, 0.025f, 0.0f), wood);                                              // bottom roller (in the fist)
                foreach (var (x, y, w, h) in new[] { (-0.07f, 0.17f, 0.09f, 0.05f), (0.06f, 0.13f, 0.07f, 0.06f), (-0.02f, 0.1f, 0.12f, 0.03f) })
                    Box(n, new Vector3(w, h, 0.01f), new Vector3(x, y, -0.03f), col.Darkened(0.45f), 0f, new Vector3(-0.2f, 0, 0));
                Box(n, new Vector3(0.035f, 0.035f, 0.012f), new Vector3(0.05f, 0.2f, -0.04f), new Color(0.8f, 0.15f, 0.1f), 0f, new Vector3(-0.2f, 0, 0.78f)); // red X
                break;
            }
            case "gasmask":
            {
                pos = new Vector3(0.25f, -0.12f, -0.52f); rest = new Vector3(0, 0.1f, 0);
                Box(n, new Vector3(0.2f, 0.015f, 0.015f), new Vector3(0, 0.02f, 0), new Color(0.15f, 0.12f, 0.1f));                              // strap held in the fist
                Box(n, new Vector3(0.19f, 0.15f, 0.07f), new Vector3(0, -0.1f, 0), col);                                                          // face piece
                foreach (float sx in new[] { -1f, 1f }) { var lens = Util.SphereMesh(0.032f, new Color(0.75f, 0.9f, 0.8f), 0.6f); lens.Position = new Vector3(0.045f * sx, -0.075f, 0.035f); n.AddChild(lens); }
                Cyl(n, 0.035f, 0.07f, new Vector3(0, -0.165f, 0.05f), new Color(0.15f, 0.15f, 0.15f), 0f, new Vector3(Mathf.Pi / 2f, 0, 0));
                break;
            }
            default:
            {
                pos = new Vector3(0.28f, -0.27f, -0.5f); rest = Vector3.Zero;
                Box(n, new Vector3(0.14f, 0.14f, 0.2f), new Vector3(0, -0.06f, -0.04f), col);
                break;
            }
        }
        return light;
    }

    public static Node3D LeftHandFp()
    {
        var h = new Node3D { Position = new Vector3(-0.3f, -0.36f, -0.5f), Rotation = new Vector3(0.05f, 0.35f, 0) };
        Box(h, new Vector3(0.1f, 0.09f, 0.11f), new Vector3(0, 0, 0), Leather);
        Box(h, new Vector3(0.07f, 0.07f, 0.4f), new Vector3(-0.03f, -0.06f, 0.25f), Sleeve, 0f, new Vector3(0.2f, 0.12f, 0));
        Box(h, new Vector3(0.1f, 0.1f, 0.05f), new Vector3(-0.012f, -0.035f, 0.085f), Leather.Darkened(0.35f), 0f, new Vector3(0.2f, 0.12f, 0));
        return h;
    }
}
