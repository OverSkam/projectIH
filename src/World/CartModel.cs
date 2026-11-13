using Godot;

namespace AppStein;

/// <summary>
/// The tribute minecart of one miner (v2.2): a low-poly ore cart on a short piece of rail, 1.6 m long (local Z, the open end towards -Z faces the Goblin) x 1.0 m wide x 1.0 m high,
/// with trim and a hanging lantern in the miner's colour and a brass name plate on the outer end. Built on every peer by HallCeremony; carts stand empty during the round.
/// </summary>
public static class CartModel
{
    public const float Length = 1.6f, Width = 1.0f, Height = 1.0f;
    /// <summary>Height of the cart's inner floor above the ground (where the sack lies).</summary>
    public const float InnerFloor = 0.5f;

    static Color K(Color c, float k) => new(Mathf.Min(1f, c.R * k), Mathf.Min(1f, c.G * k), Mathf.Min(1f, c.B * k), 1f);
    static readonly Color Rust = new(0.36f, 0.24f, 0.16f), Iron = new(0.17f, 0.17f, 0.19f), Wood = new(0.33f, 0.22f, 0.13f), Brass = new(0.78f, 0.6f, 0.2f);

    /// <summary>Builds the cart (and its rail, collision and lantern light) under <paramref name="parent"/>, local origin on the ground at the cart centre.</summary>
    public static Node3D Build(Node3D parent, Color trim, string name)
    {
        var root = new Node3D { Name = "Model" };
        parent.AddChild(root);
        var m = new GMesh { Variation = 0.09f };
        var trimC = trim; var trimDark = K(trim, 0.7f); trimDark.A = 1f;

        // rail: two steel rails on wooden sleepers, a little longer than the cart, with buffer stops
        for (float z = -1.2f; z <= 1.21f; z += 0.48f) m.Box(new Vector3(0, 0.03f, z), new Vector3(1.15f, 0.06f, 0.15f), Wood);
        foreach (float sx in new[] { -0.34f, 0.34f })
        {
            m.Box(new Vector3(sx, 0.085f, 0), new Vector3(0.07f, 0.06f, 2.7f), K(Iron, 1.3f));
            m.Box(new Vector3(sx, 0.13f, 0), new Vector3(0.04f, 0.03f, 2.7f), K(Iron, 1.7f));
        }
        m.Box(new Vector3(0, 0.2f, -1.38f), new Vector3(0.8f, 0.2f, 0.12f), Rust);
        m.Box(new Vector3(0, 0.2f, 1.38f), new Vector3(0.8f, 0.2f, 0.12f), Rust);

        // wheels and axles
        foreach (float sz in new[] { -0.5f, 0.5f })
        {
            m.Tube(new Vector3(-0.42f, 0.27f, sz), new Vector3(0.42f, 0.27f, sz), 0.045f, 0.045f, Iron, 6);
            foreach (float sx in new[] { -0.36f, 0.36f })
            {
                m.Tube(new Vector3(sx - 0.03f, 0.27f, sz), new Vector3(sx + 0.03f, 0.27f, sz), 0.16f, 0.16f, K(Iron, 1.2f), 10);
                m.Tube(new Vector3(sx - 0.045f, 0.27f, sz), new Vector3(sx + 0.045f, 0.27f, sz), 0.07f, 0.07f, K(Brass, 0.8f), 6);
            }
        }
        // chassis frame
        m.Box(new Vector3(0, 0.4f, 0), new Vector3(0.96f, 0.1f, 1.62f), Iron);
        m.Box(new Vector3(0, 0.47f, 0), new Vector3(0.7f, 0.06f, 1.2f), K(Iron, 0.8f));
        // couplings
        m.Box(new Vector3(0, 0.42f, -0.95f), new Vector3(0.1f, 0.1f, 0.3f), K(Iron, 1.4f));
        m.Box(new Vector3(0, 0.42f, 0.95f), new Vector3(0.1f, 0.1f, 0.3f), K(Iron, 1.4f));

        // bucket: a tapered iron box (bottom 0.86 x 1.4, rim 1.0 x 1.6), open at the top
        float yb = 0.45f, yt = Height;
        Vector3 B(float sx, float sz) => new(0.43f * sx, yb, 0.7f * sz);
        Vector3 T(float sx, float sz) => new(0.5f * sx, yt, 0.8f * sz);
        // outer walls (hint = outward)
        m.Quad(B(-1, -1), B(1, -1), T(1, -1), T(-1, -1), Rust, Vector3.Forward);
        m.Quad(B(1, 1), B(-1, 1), T(-1, 1), T(1, 1), Rust, Vector3.Back);
        m.Quad(B(-1, 1), B(-1, -1), T(-1, -1), T(-1, 1), K(Rust, 0.95f), Vector3.Left);
        m.Quad(B(1, -1), B(1, 1), T(1, 1), T(1, -1), K(Rust, 0.95f), Vector3.Right);
        // inner walls (hint = inward) and floor
        Vector3 Bi(float sx, float sz) => new(0.38f * sx, InnerFloor, 0.65f * sz);
        Vector3 Ti(float sx, float sz) => new(0.45f * sx, yt - 0.01f, 0.75f * sz);
        var inner = K(Rust, 0.5f); inner.A = 1f;
        m.Quad(Bi(-1, -1), Bi(1, -1), Ti(1, -1), Ti(-1, -1), inner, Vector3.Back);
        m.Quad(Bi(1, 1), Bi(-1, 1), Ti(-1, 1), Ti(1, 1), inner, Vector3.Forward);
        m.Quad(Bi(-1, 1), Bi(-1, -1), Ti(-1, -1), Ti(-1, 1), K(inner, 0.9f), Vector3.Right);
        m.Quad(Bi(1, -1), Bi(1, 1), Ti(1, 1), Ti(1, -1), K(inner, 0.9f), Vector3.Left);
        m.Quad(Bi(-1, -1), Bi(1, -1), Bi(1, 1), Bi(-1, 1), K(inner, 0.6f), Vector3.Up);
        // rim in the miner's colour (all four sides), riveted iron bands and ribs
        m.Box(new Vector3(0, yt, -0.8f), new Vector3(1.04f, 0.07f, 0.09f), trimC);
        m.Box(new Vector3(0, yt, 0.8f), new Vector3(1.04f, 0.07f, 0.09f), trimC);
        m.Box(new Vector3(-0.5f, yt, 0), new Vector3(0.09f, 0.07f, 1.64f), trimC);
        m.Box(new Vector3(0.5f, yt, 0), new Vector3(0.09f, 0.07f, 1.64f), trimC);
        foreach (float sx in new[] { -1f, 1f })
        {
            m.Box(new Vector3(0.46f * sx, 0.78f, 0), new Vector3(0.03f, 0.05f, 1.46f), Iron);
            for (float z = -0.55f; z <= 0.56f; z += 0.55f) m.Box(new Vector3(0.47f * sx, 0.72f, z), new Vector3(0.04f, 0.5f, 0.08f), K(Iron, 1.1f));
            m.Box(new Vector3(0.44f * sx, 0.58f, 0), new Vector3(0.03f, 0.12f, 1.3f), trimDark);                       // painted stripe
        }
        foreach (float sz in new[] { -1f, 1f })
        {
            m.Box(new Vector3(0, 0.78f, 0.77f * sz), new Vector3(0.9f, 0.05f, 0.03f), Iron);
            m.Box(new Vector3(-0.3f, 0.72f, 0.775f * sz), new Vector3(0.08f, 0.5f, 0.04f), K(Iron, 1.1f));
            m.Box(new Vector3(0.3f, 0.72f, 0.775f * sz), new Vector3(0.08f, 0.5f, 0.04f), K(Iron, 1.1f));
        }
        // the outer end (towards the entrance): brass name plate in a painted frame
        m.Box(new Vector3(0, 0.66f, 0.8f), new Vector3(0.74f, 0.22f, 0.025f), trimDark);
        m.Box(new Vector3(0, 0.66f, 0.815f), new Vector3(0.68f, 0.17f, 0.02f), Brass);
        // lantern post on the rear corner: a pole, an arm and the hanging lantern frame
        m.Box(new Vector3(-0.5f, 1.2f, 0.78f), new Vector3(0.05f, 0.45f, 0.05f), Iron);
        m.Box(new Vector3(-0.5f, 1.43f, 0.78f), new Vector3(0.05f, 0.05f, 0.05f), Iron);
        m.Box(new Vector3(-0.4f, 1.43f, 0.78f), new Vector3(0.22f, 0.04f, 0.04f), Iron);
        m.Box(new Vector3(-0.3f, 1.3f, 0.78f), new Vector3(0.02f, 0.2f, 0.02f), Iron);
        m.Box(new Vector3(-0.3f, 1.2f, 0.78f), new Vector3(0.2f, 0.03f, 0.2f), trimDark);
        m.Box(new Vector3(-0.3f, 1.42f, 0.78f), new Vector3(0.22f, 0.04f, 0.22f), trimC);
        m.Instance(root, null, "Cart");

        // the lantern glass: bright, in the miner's colour (a legal light source, small)
        var glow = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = trim.Lightened(0.35f), EmissionEnabled = true, Emission = trim, EmissionEnergyMultiplier = 1.5f };
        root.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.18f, 0.14f) }, MaterialOverride = glow, Position = new Vector3(-0.3f, 1.3f, 0.78f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        root.AddChild(new OmniLight3D { Position = new Vector3(-0.3f, 1.35f, 0.78f), LightColor = trim.Lerp(Colors.White, 0.35f), LightEnergy = 0.9f, OmniRange = 4.8f, OmniAttenuation = 1.3f, ShadowEnabled = false });

        // name on the plate
        int len = Mathf.Max(1, name.Length);
        root.AddChild(new Label3D
        {
            Text = name, PixelSize = 0.0017f * Mathf.Min(1f, 7f / len), FontSize = 110, Modulate = new Color(0.12f, 0.07f, 0.02f), OutlineSize = 0, Font = UiTheme.Chunky,
            Position = new Vector3(0, 0.66f, 0.83f), DoubleSided = false, TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        });

        // collision: the bucket and chassis are solid (miners can not walk through, may stand on the rim)
        var body = new StaticBody3D { Name = "CartBody", CollisionLayer = Layers.World, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Width, Height, Length) }, Position = new Vector3(0, Height * 0.5f, 0) });
        parent.AddChild(body);
        return root;
    }
}
