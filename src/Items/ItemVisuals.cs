using System;
using Godot;

namespace AppStein;

/// <summary>Spins its children slowly (dropped steins, pedestal orbs).</summary>
public partial class SpinNode : Node3D
{
    public float Speed = 0.9f, Bob = 0.04f;
    float _t, _y;
    bool _init;
    public override void _Process(double delta)
    {
        if (!_init) { _y = Position.Y; _init = true; }
        _t += (float)delta;
        Rotation = new Vector3(0, _t * Speed, 0);
        Position = new Vector3(Position.X, _y + Mathf.Sin(_t * 2f) * Bob, Position.Z);
    }
}

/// <summary>Distinct world models for every stein, held-utility models and miner attachments.</summary>
public static class ItemVisuals
{
    static MeshInstance3D Mi(Mesh m, Color c, float em = 1f, Vector3? pos = null, Vector3? rot = null)
    {
        var mi = new MeshInstance3D { Mesh = m, MaterialOverride = Util.Mat(c, em) };
        if (pos.HasValue) mi.Position = pos.Value;
        if (rot.HasValue) mi.Rotation = rot.Value;
        return mi;
    }
    static Mesh Cyl(float r, float h) => new CylinderMesh { TopRadius = r, BottomRadius = r, Height = h, RadialSegments = 10 };
    static Mesh Sph(float r) => new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 10, Rings = 5 };
    static Mesh Box(float x, float y, float z) => new BoxMesh { Size = new Vector3(x, y, z) };

    /// <summary>A spinning stein model about 0.4 m tall, origin at its centre.</summary>
    public static Node3D Stein(string id)
    {
        var def = Steins.Get(id);
        var c = def?.Color ?? Colors.White;
        var root = new SpinNode();
        switch (id)
        {
            case "infinity":
            {
                // six differently coloured stones fused around a white core
                var cols = new[] { new Color(0.95f, 0.15f, 0.15f), new Color(1f, 0.55f, 0.1f), new Color(1f, 0.95f, 0.2f), new Color(0.2f, 0.9f, 0.3f), new Color(0.2f, 0.5f, 1f), new Color(0.7f, 0.25f, 0.95f) };
                var dirs = new[] { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward };
                for (int i = 0; i < 6; i++) root.AddChild(Mi(Sph(0.11f), cols[i], 1.6f, dirs[i] * 0.13f));
                root.AddChild(Mi(Sph(0.1f), new Color(1, 1, 1), 2.5f));
                break;
            }
            case "regen":
                root.AddChild(Mi(Sph(0.18f), c, 1.2f));
                root.AddChild(Mi(Box(0.05f, 0.3f, 0.05f), Colors.White, 1.5f));
                root.AddChild(Mi(Box(0.3f, 0.05f, 0.05f), Colors.White, 1.5f));
                break;
            case "swift":
                root.AddChild(Mi(new PrismMesh { Size = new Vector3(0.3f, 0.45f, 0.18f) }, c, 1.3f));
                break;
            case "stoneskin":
                root.AddChild(Mi(Box(0.3f, 0.3f, 0.3f), c, 0.6f, null, new Vector3(0.3f, 0.5f, 0.1f)));
                root.AddChild(Mi(Box(0.18f, 0.18f, 0.18f), new Color(0.4f, 0.4f, 0.42f), 0.4f, new Vector3(0.12f, 0.14f, 0.1f), new Vector3(0.6f, 0.2f, 0.4f)));
                break;
            case "packmule":
                root.AddChild(Mi(Cyl(0.15f, 0.32f), c, 1.0f));
                root.AddChild(Mi(Box(0.34f, 0.05f, 0.34f), new Color(0.4f, 0.25f, 0.1f), 0.5f));
                break;
            case "strongarm":
                root.AddChild(Mi(new CapsuleMesh { Radius = 0.12f, Height = 0.4f, RadialSegments = 8, Rings = 2 }, c, 1.2f, null, new Vector3(0, 0, Mathf.Pi / 2)));
                root.AddChild(Mi(Sph(0.15f), c, 1.2f, new Vector3(0.2f, 0, 0)));
                break;
            case "owl":
                root.AddChild(Mi(Sph(0.18f), c, 1.0f));
                root.AddChild(Mi(Sph(0.06f), Colors.White, 2f, new Vector3(-0.07f, 0.04f, -0.15f)));
                root.AddChild(Mi(Sph(0.06f), Colors.White, 2f, new Vector3(0.07f, 0.04f, -0.15f)));
                root.AddChild(Mi(Sph(0.03f), Colors.Black, 0f, new Vector3(-0.07f, 0.04f, -0.2f)));
                root.AddChild(Mi(Sph(0.03f), Colors.Black, 0f, new Vector3(0.07f, 0.04f, -0.2f)));
                break;
            case "feather":
                root.AddChild(Mi(new PrismMesh { Size = new Vector3(0.1f, 0.5f, 0.03f) }, c, 1.6f, null, new Vector3(0, 0, 0.5f)));
                break;
            case "hush":
                root.AddChild(Mi(new TorusMesh { InnerRadius = 0.08f, OuterRadius = 0.2f, Rings = 10, RingSegments = 6 }, c, 1.2f, null, new Vector3(Mathf.Pi / 2.4f, 0, 0)));
                break;
            case "luck":
                root.AddChild(Mi(Box(0.24f, 0.24f, 0.24f), c, 1.6f, null, new Vector3(0.7854f, 0.7854f, 0)));
                break;
            case "midas":
                for (int i = 0; i < 4; i++) root.AddChild(Mi(Cyl(0.18f, 0.05f), c, 1.8f, new Vector3(0, -0.1f + i * 0.07f, 0), new Vector3(0, i * 0.4f, 0)));
                break;
            case "ethereal":
            {
                var m = new StandardMaterial3D { AlbedoColor = new Color(c, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 1.5f };
                root.AddChild(new MeshInstance3D { Mesh = Sph(0.2f), MaterialOverride = m });
                root.AddChild(Mi(Sph(0.07f), Colors.White, 3f));
                break;
            }
            case "blood":
                root.AddChild(Mi(Sph(0.16f), c, 1.4f, new Vector3(0, -0.05f, 0)));
                root.AddChild(Mi(new PrismMesh { Size = new Vector3(0.22f, 0.24f, 0.22f) }, c, 1.4f, new Vector3(0, 0.17f, 0)));
                break;
            default:
                root.AddChild(Mi(Sph(0.18f), c, 1.0f));
                break;
        }
        root.AddChild(new OmniLight3D { LightColor = id == "infinity" ? new Color(1f, 1f, 0.9f) : c, LightEnergy = id == "infinity" ? 1.4f : 0.8f, OmniRange = 3.2f });
        return root;
    }

    /// <summary>Held utility model (first-person tool node). Pickaxe and lantern are built by Miner.SetHeld.</summary>
    public static Node3D Held(string id, Color col)
    {
        var n = new Node3D();
        switch (id)
        {
            case "rope":
                n.AddChild(Mi(new TorusMesh { InnerRadius = 0.07f, OuterRadius = 0.15f, Rings = 12, RingSegments = 6 }, col, 0f, null, new Vector3(Mathf.Pi / 2f, 0, 0)));
                break;
            case "dynamite":
                for (int i = 0; i < 3; i++) n.AddChild(Mi(Cyl(0.035f, 0.28f), col, 0.2f, new Vector3(-0.05f + i * 0.05f, 0, 0), new Vector3(0.4f, 0, 0)));
                n.AddChild(Mi(Box(0.2f, 0.04f, 0.04f), new Color(0.15f, 0.12f, 0.1f), 0f));
                break;
            case "map":
                n.AddChild(Mi(Box(0.3f, 0.02f, 0.22f), col, 0f, null, new Vector3(-0.5f, 0, 0)));
                n.AddChild(Mi(Box(0.1f, 0.03f, 0.1f), new Color(0.8f, 0.2f, 0.1f), 0f, new Vector3(0.03f, 0.02f, 0)));
                break;
            case "gasmask":
                n.AddChild(Mi(Box(0.2f, 0.16f, 0.1f), col, 0f));
                n.AddChild(Mi(Cyl(0.04f, 0.1f), new Color(0.15f, 0.15f, 0.15f), 0f, new Vector3(0, -0.1f, -0.03f), new Vector3(Mathf.Pi / 2f, 0, 0)));
                break;
            case "beartrap":
                n.AddChild(Mi(Cyl(0.14f, 0.03f), col, 0f));
                n.AddChild(Mi(Box(0.3f, 0.02f, 0.02f), new Color(0.6f, 0.6f, 0.62f), 0f, new Vector3(0, 0.03f, 0)));
                break;
            default:
                n.AddChild(Util.BoxMesh(new Vector3(0.2f, 0.2f, 0.3f), col));
                break;
        }
        return n;
    }

    /// <summary>Visible gas mask on a miner's face (public).</summary>
    public static Node3D Mask()
    {
        var n = new Node3D { Position = new Vector3(0, 1.58f, -0.18f) };
        n.AddChild(Mi(Box(0.26f, 0.16f, 0.1f), new Color(0.2f, 0.32f, 0.2f), 0f));
        n.AddChild(Mi(Cyl(0.05f, 0.12f), new Color(0.12f, 0.12f, 0.12f), 0f, new Vector3(0, -0.1f, -0.06f), new Vector3(Mathf.Pi / 2f, 0, 0)));
        n.AddChild(Mi(Sph(0.035f), new Color(0.7f, 0.9f, 0.8f), 0.5f, new Vector3(-0.07f, 0.03f, -0.055f)));
        n.AddChild(Mi(Sph(0.035f), new Color(0.7f, 0.9f, 0.8f), 0.5f, new Vector3(0.07f, 0.03f, -0.055f)));
        return n;
    }
}

/// <summary>Short-lived local effects (explosion flash, pebble).</summary>
public static class ItemFx
{
    public static void Explosion(Node3D world, Vector3 pos, float radius)
    {
        Sfx.Play(SfxNames.Explosion, pos);
        var n = new Node3D { Position = pos };
        world.AddChild(n);
        n.GlobalPosition = pos;
        var ball = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 5 }, MaterialOverride = Util.Mat(new Color(1f, 0.55f, 0.15f), 4f), Scale = Vector3.One * 0.3f };
        n.AddChild(ball);
        var light = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.25f), LightEnergy = 14f, OmniRange = radius * 2.5f };
        n.AddChild(light);
        var tw = n.CreateTween().SetParallel(true);
        tw.TweenProperty(ball, "scale", Vector3.One * radius * 0.55f, 0.35f);
        tw.TweenProperty(light, "light_energy", 0f, 0.7f);
        tw.Chain().TweenCallback(Callable.From(n.QueueFree));
    }

    public static void Pebble(Node3D world, Vector3 pos)
    {
        Sfx.Play(SfxNames.GhostPebble, pos);
        var n = new Node3D { Position = pos };
        world.AddChild(n);
        n.GlobalPosition = pos;
        n.AddChild(Util.SphereMesh(0.05f, new Color(0.6f, 0.6f, 0.65f), 0.5f));
        var light = new OmniLight3D { LightColor = new Color(0.6f, 0.7f, 1f), LightEnergy = 1.5f, OmniRange = 4f };
        n.AddChild(light);
        var tw = n.CreateTween();
        tw.TweenProperty(light, "light_energy", 0f, 0.6f);
        tw.TweenCallback(Callable.From(n.QueueFree));
    }
}
