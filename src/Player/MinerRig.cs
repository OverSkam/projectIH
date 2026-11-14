using System.Collections.Generic;
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
    // v2.3: the sack on the back (coarse step 0..4 by burden), shove lunge, dragging pose, trudge
    Node3D _sackRoot, _sackBody; MeshInstance3D _sackTie; readonly List<(MeshInstance3D m, Vector3 dir, float size)> _lumps = new();
    int _sackStep; float _sackK = -1f, _sackTarget; float _trudge, _trudgeTarget;
    float _shoveT = 1f, _dragK; bool _dragging;
    public int SackStep => _sackStep;
    public bool Trudging => _trudgeTarget > 0.2f;
    /// <summary>Sack size by step: width, height, depth (m), centre height above the hip and distance behind the spine. Step 4 is wider than the miner and hangs to the thighs.</summary>
    static readonly (float w, float h, float d, float y, float z)[] SackDims =
    {
        (0.26f, 0.30f, 0.10f, 0.40f, 0.19f),
        (0.40f, 0.46f, 0.22f, 0.32f, 0.25f),
        (0.62f, 0.68f, 0.36f, 0.24f, 0.32f),
        (0.82f, 0.90f, 0.50f, 0.10f, 0.40f),
        (1.02f, 1.12f, 0.64f, -0.04f, 0.48f),
    };
    static readonly float[] StepTrudge = { 0f, 0.1f, 0.3f, 0.65f, 1f };

    static readonly Color Jacket = new(0.40f, 0.30f, 0.22f);
    static readonly Color Trousers = new(0.20f, 0.22f, 0.30f);
    static readonly Color BootC = new(0.16f, 0.11f, 0.08f);
    static readonly Color SkinC = new(0.88f, 0.68f, 0.52f);
    static readonly Color Sack = new(0.62f, 0.49f, 0.30f);

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
        BuildSack();
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

    // ------------------------------------------------------------------ the sack on the back (v2.3)
    void BuildSack()
    {
        _sackRoot = Pivot(_torso, new Vector3(0, 0.34f, 0.26f));
        _sackBody = Pivot(_sackRoot, Vector3.Zero);
        var body = Util.SphereMesh(0.5f, Sack); _sackBody.AddChild(body);
        // the neck of the sack and the rope that ties it, on top; it rides above the bulge
        _sackTie = Util.BoxMesh(new Vector3(0.16f, 0.1f, 0.16f), new Color(0.36f, 0.27f, 0.15f)); _sackRoot.AddChild(_sackTie);
        // stones pushing against the burlap: more of them every step (a silhouette, not a count)
        var rng = new RandomNumberGenerator { Seed = 4242 };
        var stone = new Color(0.42f, 0.38f, 0.34f);
        for (int i = 0; i < 8; i++)
        {
            var dir = new Vector3(rng.RandfRange(-0.8f, 0.8f), rng.RandfRange(-0.7f, 0.5f), rng.RandfRange(0.3f, 1f)).Normalized();
            var lump = Util.SphereMesh(0.5f, i % 3 == 0 ? stone.Lightened(0.15f) : stone);
            _sackRoot.AddChild(lump);
            _lumps.Add((lump, dir, rng.RandfRange(0.17f, 0.26f)));
        }
        ApplySack(0f, 0);
    }

    /// <summary>Coarse sack size 0..4 (public information, by burden). The model eases to the new size.</summary>
    public void SetSackStep(int step)
    {
        step = Mathf.Clamp(step, 0, 4);
        if (_sackK < 0f) { _sackK = step; ApplySack(step, step); }
        _sackStep = step; _sackTarget = step; _trudgeTarget = StepTrudge[step];
    }

    void ApplySack(float k, int stepForLumps)
    {
        int lo = Mathf.Clamp(Mathf.FloorToInt(k), 0, 3), hi = Mathf.Min(4, lo + 1);
        float f = Mathf.Clamp(k - lo, 0f, 1f);
        var a = SackDims[lo]; var b = SackDims[hi];
        float w = Mathf.Lerp(a.w, b.w, f), h = Mathf.Lerp(a.h, b.h, f), d = Mathf.Lerp(a.d, b.d, f), y = Mathf.Lerp(a.y, b.y, f), z = Mathf.Lerp(a.z, b.z, f);
        _sackRoot.Position = new Vector3(0, y, z);
        _sackBody.Scale = new Vector3(w, h, d);
        _sackTie.Position = new Vector3(0, h * 0.5f + 0.02f, 0); _sackTie.Scale = new Vector3(Mathf.Lerp(0.8f, 1.5f, k / 4f), 1f, Mathf.Lerp(0.8f, 1.5f, k / 4f));
        for (int i = 0; i < _lumps.Count; i++)
        {
            var (m, dir, size) = _lumps[i];
            // lumps show from step 2 on: three at step 2, six at step 3, all eight at step 4
            float show = Mathf.Clamp(k - (1f + i / 3f), 0f, 1f);
            m.Visible = show > 0.01f;
            m.Position = new Vector3(dir.X * w * 0.46f, dir.Y * h * 0.46f, dir.Z * d * 0.46f);
            m.Scale = Vector3.One * (size * 2f * show) * Mathf.Lerp(0.8f, 1.35f, k / 4f);
        }
    }

    public void Swing() { _swingT = 0f; }

    /// <summary>A quick two-handed push: both arms thrust out and the body lunges forward (0.45 s).</summary>
    public void Shove() { _shoveT = 0f; }
    /// <summary>Screenshot staging: freeze the lunge at u (0..1 of the 0.45 s).</summary>
    public void DebugShove(float u) { _shoveT = u; }
    /// <summary>Dragging a downed miner: leaning back, both arms down and behind.</summary>
    public void SetDragging(bool on) { _dragging = on; }

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
        if (_sackK >= 0f && !Mathf.IsEqualApprox(_sackK, _sackTarget)) { _sackK = Mathf.MoveToward(_sackK, _sackTarget, dt * 5f); ApplySack(_sackK, _sackStep); }
        _trudge = Mathf.Lerp(_trudge, _trudgeTarget, Mathf.Min(1f, dt * 4f));
        _dragK = Mathf.MoveToward(_dragK, _dragging ? 1f : 0f, dt * 4f);
        _amp = Mathf.Lerp(_amp, targetAmp * (1f - 0.25f * _trudge), Mathf.Min(1f, dt * 10f));
        _phase += dt * (speed * 1.7f + 1f) * (1f - 0.3f * _trudge);   // heavy sacks: slower, shorter steps
        _crouch = Mathf.Lerp(_crouch, crouch ? 1f : 0f, Mathf.Min(1f, dt * 10f));
        float s = Mathf.Sin(_phase);
        float legAmp = 0.75f * _amp;

        _legL.Rotation = new Vector3(s * legAmp, 0, 0);
        _legR.Rotation = new Vector3(-s * legAmp, 0, 0);
        float hip = Mathf.Lerp(0.85f, 0.6f, _crouch) - 0.08f * _trudge - 0.06f * _dragK;
        _legL.Position = new Vector3(-0.12f, hip, 0); _legR.Position = new Vector3(0.12f, hip, 0);
        float ls = Mathf.Lerp(1f, 0.72f, _crouch);
        _legL.Scale = new Vector3(1, ls, 1); _legR.Scale = new Vector3(1, ls, 1);
        _bob = Mathf.Abs(s) * 0.03f * _amp * (1f + 0.8f * _trudge);
        // shove lunge: eases out and back over 0.45 s
        float lunge = 0f, armsOut = 0f;
        if (_shoveT < 1f)
        {
            _shoveT = Mathf.Min(1f, _shoveT + dt / 0.45f);
            float u = _shoveT < 0.35f ? _shoveT / 0.35f : 1f - (_shoveT - 0.35f) / 0.65f;
            lunge = Mathf.Sin(u * Mathf.Pi * 0.5f); armsOut = lunge;
        }
        _torso.Position = new Vector3(0, hip + _bob, -0.2f * lunge + 0.12f * _dragK);
        _torso.Rotation = new Vector3(-0.45f * _crouch - (sprint ? 0.18f : 0f) - 0.2f * _trudge - 0.5f * lunge + 0.25f * _dragK, 0, 0);
        _head.Rotation = new Vector3(0.3f * _crouch + 0.15f * _trudge, 0, 0);

        _armL.Rotation = new Vector3(Mathf.Lerp(-s * 0.7f * _amp + 0.1f, 1.45f, armsOut), 0, Mathf.Lerp(0.06f, -0.12f, armsOut));
        if (_dragK > 0f) _armL.Rotation = _armL.Rotation.Lerp(new Vector3(-0.6f, 0, 0.1f), _dragK);
        float rArm = s * 0.7f * _amp + 0.45f; // holding the tool a little forward
        if (_swingT < 1f)
        {
            _swingT = Mathf.Min(1f, _swingT + dt / 0.42f);
            float e = 1f - Mathf.Pow(1f - _swingT, 2.5f);
            rArm = Mathf.Lerp(2.5f, 0.2f, e);
        }
        float rZ = -0.06f;
        if (armsOut > 0f) { rArm = Mathf.Lerp(rArm, 1.45f, armsOut); rZ = Mathf.Lerp(rZ, 0.12f, armsOut); }
        if (_dragK > 0f) { rArm = Mathf.Lerp(rArm, -0.6f, _dragK); rZ = Mathf.Lerp(rZ, -0.1f, _dragK); }
        _armR.Rotation = new Vector3(rArm, 0, rZ);
    }
}
