using Godot;

namespace AppStein;

/// <summary>
/// Third-person low-poly miner model (what OTHER players see): helmet in the player's colour with a lamp, backpack/sack, boots.
/// Faces -Z like the Miner node. Procedural walk / crouch / swing animation. Hidden for the local player (first person).
/// </summary>
public partial class MinerRig : Node3D
{
    public Node3D RightHand;
    Node3D _torso, _head, _armR, _armL, _legR, _legL;
    float _phase, _amp, _crouch, _swingT = 1f, _bob;

    static readonly Color Jacket = new(0.40f, 0.30f, 0.22f);
    static readonly Color Trousers = new(0.20f, 0.22f, 0.30f);
    static readonly Color BootC = new(0.16f, 0.11f, 0.08f);
    static readonly Color SkinC = new(0.88f, 0.68f, 0.52f);
    static readonly Color Sack = new(0.50f, 0.38f, 0.22f);

    public MinerRig(Color helmet) { Build(helmet); }

    static MeshInstance3D Box(Node3D p, Vector3 size, Vector3 pos, Color c, float emission = 0f)
    {
        var m = Util.BoxMesh(size, c, emission); m.Position = pos; p.AddChild(m); return m;
    }

    static Node3D Pivot(Node3D p, Vector3 pos) { var n = new Node3D { Position = pos }; p.AddChild(n); return n; }

    void Build(Color helmet)
    {
        // legs
        _legL = Pivot(this, new Vector3(-0.12f, 0.85f, 0)); _legR = Pivot(this, new Vector3(0.12f, 0.85f, 0));
        foreach (var leg in new[] { _legL, _legR })
        {
            Box(leg, new Vector3(0.18f, 0.64f, 0.2f), new Vector3(0, -0.31f, 0), Trousers);
            Box(leg, new Vector3(0.21f, 0.2f, 0.34f), new Vector3(0, -0.74f, -0.04f), BootC);
            Box(leg, new Vector3(0.22f, 0.04f, 0.35f), new Vector3(0, -0.83f, -0.04f), new Color(0.08f, 0.06f, 0.05f));
        }
        // torso
        _torso = Pivot(this, new Vector3(0, 0.85f, 0));
        Box(_torso, new Vector3(0.52f, 0.62f, 0.3f), new Vector3(0, 0.31f, 0), Jacket);
        Box(_torso, new Vector3(0.54f, 0.07f, 0.32f), new Vector3(0, 0.02f, 0), new Color(0.25f, 0.17f, 0.1f)); // belt
        Box(_torso, new Vector3(0.54f, 0.05f, 0.32f), new Vector3(0, 0.38f, 0), new Color(0.8f, 0.8f, 0.7f)); // reflective strip
        Box(_torso, new Vector3(0.07f, 0.55f, 0.04f), new Vector3(-0.16f, 0.32f, -0.16f), new Color(0.25f, 0.17f, 0.1f)); // straps
        Box(_torso, new Vector3(0.07f, 0.55f, 0.04f), new Vector3(0.16f, 0.32f, -0.16f), new Color(0.25f, 0.17f, 0.1f));
        // backpack / sack
        Box(_torso, new Vector3(0.42f, 0.5f, 0.22f), new Vector3(0, 0.34f, 0.26f), Sack);
        Box(_torso, new Vector3(0.3f, 0.12f, 0.2f), new Vector3(0, 0.66f, 0.26f), new Color(0.42f, 0.3f, 0.17f));
        var roll = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 0.46f, RadialSegments = 8, Rings = 0 }, MaterialOverride = Util.Mat(new Color(0.3f, 0.35f, 0.25f)) };
        roll.Position = new Vector3(0, 0.74f, 0.26f); roll.Rotation = new Vector3(0, 0, Mathf.Pi / 2f); _torso.AddChild(roll);
        // arms
        _armL = Pivot(_torso, new Vector3(-0.34f, 0.56f, 0)); _armR = Pivot(_torso, new Vector3(0.34f, 0.56f, 0));
        foreach (var arm in new[] { _armL, _armR })
        {
            Box(arm, new Vector3(0.15f, 0.42f, 0.15f), new Vector3(0, -0.2f, 0), Jacket);
            Box(arm, new Vector3(0.12f, 0.16f, 0.12f), new Vector3(0, -0.5f, 0), SkinC);
            Box(arm, new Vector3(0.14f, 0.1f, 0.14f), new Vector3(0, -0.45f, 0), new Color(0.3f, 0.22f, 0.14f)); // glove cuff
        }
        RightHand = Pivot(_armR, new Vector3(0, -0.56f, 0));
        // head
        _head = Pivot(_torso, new Vector3(0, 0.7f, 0));
        var headM = Util.SphereMesh(0.19f, SkinC); headM.Position = new Vector3(0, 0.16f, 0); _head.AddChild(headM);
        Box(_head, new Vector3(0.05f, 0.04f, 0.04f), new Vector3(-0.07f, 0.19f, -0.17f), new Color(0.1f, 0.08f, 0.06f));
        Box(_head, new Vector3(0.05f, 0.04f, 0.04f), new Vector3(0.07f, 0.19f, -0.17f), new Color(0.1f, 0.08f, 0.06f));
        // helmet in the player colour + brim + lamp
        var dome = Util.SphereMesh(0.235f, helmet); dome.Scale = new Vector3(1f, 0.78f, 1.08f); dome.Position = new Vector3(0, 0.26f, 0.0f); _head.AddChild(dome);
        Box(_head, new Vector3(0.5f, 0.045f, 0.5f), new Vector3(0, 0.2f, -0.02f), helmet.Darkened(0.2f));
        Box(_head, new Vector3(0.08f, 0.3f, 0.3f), new Vector3(0, 0.32f, 0.0f), helmet.Lightened(0.15f)); // ridge
        Box(_head, new Vector3(0.14f, 0.1f, 0.1f), new Vector3(0, 0.3f, -0.26f), new Color(0.25f, 0.25f, 0.28f));
        Box(_head, new Vector3(0.1f, 0.07f, 0.03f), new Vector3(0, 0.3f, -0.315f), new Color(1f, 0.95f, 0.7f), 2.2f);
    }

    public void Swing() { _swingT = 0f; }

    /// <summary>Downed: lies face down on the floor (the whole rig tips forward), visible to everyone.</summary>
    public void SetDowned(bool d)
    {
        var tw = CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(this, "rotation", d ? new Vector3(-1.5f, 0, 0) : Vector3.Zero, 0.35f);
        tw.TweenProperty(this, "position", d ? new Vector3(0, 0.22f, 0.75f) : Vector3.Zero, 0.35f);
    }

    /// <summary>speed = horizontal m/s, crouch/sprint from state flags.</summary>
    public void Animate(float dt, float speed, bool crouch, bool sprint, bool air)
    {
        float targetAmp = air ? 0.25f : Mathf.Clamp(speed / 5f, 0f, 1.3f);
        _amp = Mathf.Lerp(_amp, targetAmp, Mathf.Min(1f, dt * 10f));
        _phase += dt * (speed * 1.7f + 1f);
        _crouch = Mathf.Lerp(_crouch, crouch ? 1f : 0f, Mathf.Min(1f, dt * 10f));
        float s = Mathf.Sin(_phase);
        float legAmp = 0.75f * _amp;

        _legL.Rotation = new Vector3(s * legAmp, 0, 0);
        _legR.Rotation = new Vector3(-s * legAmp, 0, 0);
        float hip = Mathf.Lerp(0.85f, 0.6f, _crouch);
        _legL.Position = new Vector3(-0.12f, hip, 0); _legR.Position = new Vector3(0.12f, hip, 0);
        float ls = Mathf.Lerp(1f, 0.72f, _crouch);
        _legL.Scale = new Vector3(1, ls, 1); _legR.Scale = new Vector3(1, ls, 1);
        _bob = Mathf.Abs(s) * 0.03f * _amp;
        _torso.Position = new Vector3(0, hip + _bob, 0);
        _torso.Rotation = new Vector3(-0.45f * _crouch - (sprint ? 0.18f : 0f), 0, 0);
        _head.Rotation = new Vector3(0.3f * _crouch, 0, 0);

        _armL.Rotation = new Vector3(-s * 0.7f * _amp + 0.1f, 0, 0.06f);
        float rArm = s * 0.7f * _amp + 0.45f; // holding the tool a little forward
        if (_swingT < 1f)
        {
            _swingT = Mathf.Min(1f, _swingT + dt / 0.42f);
            float e = 1f - Mathf.Pow(1f - _swingT, 2.5f);
            rArm = Mathf.Lerp(2.5f, 0.2f, e);
        }
        _armR.Rotation = new Vector3(rArm, 0, -0.06f);
    }
}
