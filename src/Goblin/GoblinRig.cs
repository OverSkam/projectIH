using System;
using Godot;

namespace AppStein;

/// <summary>
/// The Goblin boss: a low-poly financier caricature (green skin, long ears, big nose, swept-back grey hair, heavy brows, smug half-smile,
/// navy quarter-zip polo with popped collar) behind a counter with a ledger and a pile of steins. Faces +Z. Built entirely from primitives.
/// Idle: breathing, head follows the nearest miner, ear twitch. Reactions via <see cref="React"/>: "happy" (rub hands) and "angry".
/// </summary>
public partial class GoblinRig : Node3D
{
    public static event Action<string> Reaction;
    public static void React(string kind) => Reaction?.Invoke(kind);

    static readonly Color Skin = new(0.38f, 0.64f, 0.27f);
    static readonly Color SkinDark = new(0.30f, 0.52f, 0.22f);
    static readonly Color Navy = new(0.11f, 0.17f, 0.36f);
    static readonly Color NavyLight = new(0.16f, 0.25f, 0.50f);
    static readonly Color Hair = new(0.88f, 0.88f, 0.94f);
    static readonly Color Brow = new(0.50f, 0.50f, 0.54f);
    static readonly Color Wood = new(0.30f, 0.20f, 0.12f);
    static readonly Color WoodLight = new(0.42f, 0.29f, 0.17f);

    Node3D _torso, _head, _earL, _earR, _browL, _browR, _mouthEnd, _lidL, _lidR, _pupilL, _pupilR;
    readonly Node3D[] _sh = new Node3D[2], _el = new Node3D[2];
    float _t, _earTimer = 2f, _earTwitch, _yaw, _pitch;
    string _react = ""; float _reactT;
    float _a1L, _a2L, _a1R, _a2R, _yawL, _yawR, _tilt, _lean, _browAngle, _shake, _bounce;
    Node3D _bodyRoot;
    float _idleLookPhase;

    public override void _Ready()
    {
        Build();
        Reaction += OnReact;
    }

    public override void _ExitTree() { Reaction -= OnReact; }

    void OnReact(string kind)
    {
        _react = kind; _reactT = kind == "happy" ? 2.4f : 2.0f;
        Sfx.Play(kind == "happy" ? SfxNames.GoblinLaugh : SfxNames.GoblinGrowl, GlobalPosition);
    }

    /// <summary>Called by GoblinObj.Build(): collision (layers 1|4) + the animated rig.</summary>
    public static void Attach(NetObject host)
    {
        host.CollisionLayer = 1 | 4; host.CollisionMask = 0;
        host.AddChild(new GoblinRig { Name = "Rig" });
        host.AddChild(Util.BoxShape(new Vector3(3.0f, 1f, 0.85f), new Vector3(0, 0.5f, 1.05f)));
        host.AddChild(Util.BoxShape(new Vector3(1f, 2f, 1f), new Vector3(0, 1f, 0)));
    }

    // ------------------------------------------------------------------ builders
    static MeshInstance3D Box(Node3D p, Vector3 size, Vector3 pos, Color c, Vector3? rot = null, float emission = 0f)
    {
        var m = Util.BoxMesh(size, c, emission); m.Position = pos; if (rot.HasValue) m.Rotation = rot.Value; p.AddChild(m); return m;
    }
    static MeshInstance3D Sph(Node3D p, float r, Vector3 pos, Color c, Vector3? scale = null, float emission = 0f)
    {
        var m = Util.SphereMesh(r, c, emission); m.Position = pos; if (scale.HasValue) m.Scale = scale.Value; p.AddChild(m); return m;
    }
    static MeshInstance3D Cone(Node3D p, float radius, float h, Vector3 pos, Color c, Vector3? rot = null, float topR = 0f)
    {
        var m = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = topR, BottomRadius = radius, Height = h, RadialSegments = 8, Rings = 0 }, MaterialOverride = Util.Mat(c) };
        m.Position = pos; if (rot.HasValue) m.Rotation = rot.Value; p.AddChild(m); return m;
    }
    static Node3D Pivot(Node3D p, Vector3 pos) { var n = new Node3D { Position = pos }; p.AddChild(n); return n; }

    void Build()
    {
        _bodyRoot = Pivot(this, Vector3.Zero);
        BuildCounter();

        // legs / trousers (mostly hidden by the counter)
        Box(_bodyRoot, new Vector3(0.28f, 0.7f, 0.3f), new Vector3(-0.2f, 0.35f, 0), new Color(0.12f, 0.12f, 0.15f));
        Box(_bodyRoot, new Vector3(0.28f, 0.7f, 0.3f), new Vector3(0.2f, 0.35f, 0), new Color(0.12f, 0.12f, 0.15f));

        _torso = Pivot(_bodyRoot, new Vector3(0, 0.72f, 0));
        // paunchy financier torso, navy polo / quarter-zip
        Box(_torso, new Vector3(0.88f, 0.78f, 0.5f), new Vector3(0, 0.38f, 0), Navy);
        Sph(_torso, 0.34f, new Vector3(0, 0.22f, 0.12f), Navy, new Vector3(1.25f, 0.9f, 0.9f));
        Box(_torso, new Vector3(0.9f, 0.1f, 0.52f), new Vector3(0, 0.0f, 0), new Color(0.35f, 0.25f, 0.12f)); // belt
        Box(_torso, new Vector3(0.1f, 0.07f, 0.02f), new Vector3(0, 0.0f, 0.27f), new Color(1f, 0.8f, 0.25f), null, 0.4f); // buckle
        // quarter-zip placket and popped collar
        Box(_torso, new Vector3(0.035f, 0.32f, 0.015f), new Vector3(0, 0.58f, 0.255f), new Color(0.75f, 0.77f, 0.85f));
        Box(_torso, new Vector3(0.11f, 0.03f, 0.03f), new Vector3(0, 0.44f, 0.26f), new Color(0.85f, 0.85f, 0.9f));
        Box(_torso, new Vector3(0.34f, 0.2f, 0.07f), new Vector3(-0.2f, 0.84f, 0.1f), NavyLight, new Vector3(-0.25f, 0.4f, 0.35f));
        Box(_torso, new Vector3(0.34f, 0.2f, 0.07f), new Vector3(0.2f, 0.84f, 0.1f), NavyLight, new Vector3(-0.25f, -0.4f, -0.35f));
        Box(_torso, new Vector3(0.5f, 0.22f, 0.07f), new Vector3(0, 0.88f, -0.14f), NavyLight, new Vector3(0.25f, 0, 0));
        Cone(_torso, 0.12f, 0.16f, new Vector3(0, 0.84f, 0), Skin, null, 0.11f); // neck

        // arms: shoulder pivot -> upper arm (sleeve) -> elbow pivot -> forearm (skin) + hand + watch
        for (int i = 0; i < 2; i++)
        {
            float s = i == 0 ? 1f : -1f;
            var sh = Pivot(_torso, new Vector3(0.52f * s, 0.74f, 0));
            Sph(sh, 0.15f, Vector3.Zero, Navy);
            Box(sh, new Vector3(0.18f, 0.36f, 0.18f), new Vector3(0, -0.2f, 0), Navy);
            var el = Pivot(sh, new Vector3(0, -0.38f, 0));
            Box(el, new Vector3(0.13f, 0.36f, 0.13f), new Vector3(0, -0.18f, 0), Skin);
            Box(el, new Vector3(0.15f, 0.04f, 0.15f), new Vector3(0, -0.28f, 0), new Color(1f, 0.82f, 0.3f), null, 0.5f); // gold watch
            Sph(el, 0.095f, new Vector3(0, -0.42f, 0), Skin);
            Box(el, new Vector3(0.03f, 0.03f, 0.03f), new Vector3(0.04f * s, -0.36f, 0.06f), new Color(1f, 0.85f, 0.3f), null, 0.6f); // pinky ring
            _sh[i] = sh; _el[i] = el;
        }

        // head
        _head = Pivot(_torso, new Vector3(0, 1.0f, 0.02f));
        BuildHead();

        // light
        AddChild(new OmniLight3D { Position = new Vector3(0, 2.7f, 1.8f), LightColor = new Color(1f, 0.85f, 0.5f), OmniRange = 9f, LightEnergy = 1.2f });
        AddChild(new OmniLight3D { Position = new Vector3(0.9f, 1.4f, 1.1f), LightColor = new Color(1f, 0.7f, 0.3f), OmniRange = 3f, LightEnergy = 0.9f });
    }

    void BuildHead()
    {
        Sph(_head, 0.3f, new Vector3(0, 0.3f, 0), Skin, new Vector3(1f, 1.08f, 1.0f)); // skull
        Box(_head, new Vector3(0.22f, 0.16f, 0.2f), new Vector3(0, 0.05f, 0.13f), Skin); // jaw
        Box(_head, new Vector3(0.12f, 0.1f, 0.1f), new Vector3(0, -0.02f, 0.2f), SkinDark); // sharp chin
        // big hooked nose
        Cone(_head, 0.115f, 0.38f, new Vector3(0, 0.22f, 0.38f), Skin, new Vector3(Mathf.Pi / 2f + 0.25f, 0, 0));
        Sph(_head, 0.085f, new Vector3(0, 0.17f, 0.57f), new Color(0.50f, 0.74f, 0.30f));
        Box(_head, new Vector3(0.4f, 0.12f, 0.16f), new Vector3(0, 0.12f, 0.16f), Skin); // cheek/jaw line
        // eyes (half-lidded, smug)
        for (int i = 0; i < 2; i++)
        {
            float s = i == 0 ? -1f : 1f;
            Sph(_head, 0.07f, new Vector3(0.115f * s, 0.34f, 0.25f), new Color(0.95f, 0.93f, 0.8f));
            var pupil = Sph(_head, 0.036f, new Vector3(0.115f * s, 0.335f, 0.315f), new Color(0.05f, 0.04f, 0.02f));
            var lid = Box(_head, new Vector3(0.17f, 0.05f, 0.14f), new Vector3(0.115f * s, 0.385f, 0.255f), SkinDark);
            var brow = Pivot(_head, new Vector3(0.125f * s, 0.455f, 0.255f));
            Box(brow, new Vector3(0.22f, 0.06f, 0.08f), Vector3.Zero, Brow);
            if (i == 0) { _pupilL = pupil; _lidL = lid; _browL = brow; } else { _pupilR = pupil; _lidR = lid; _browR = brow; }
        }
        // smug half-smile with one corner raised, a hint of tooth
        Box(_head, new Vector3(0.22f, 0.028f, 0.03f), new Vector3(-0.01f, 0.09f, 0.29f), new Color(0.12f, 0.2f, 0.08f));
        _mouthEnd = Pivot(_head, new Vector3(0.115f, 0.09f, 0.29f));
        Box(_mouthEnd, new Vector3(0.09f, 0.028f, 0.03f), new Vector3(0.03f, 0.018f, 0), new Color(0.12f, 0.2f, 0.08f), new Vector3(0, 0, 0.5f));
        Box(_head, new Vector3(0.04f, 0.025f, 0.02f), new Vector3(0.07f, 0.075f, 0.285f), new Color(0.95f, 0.92f, 0.8f));
        // swept-back grey hair
        Box(_head, new Vector3(0.58f, 0.1f, 0.5f), new Vector3(0, 0.6f, -0.04f), Hair, new Vector3(0.2f, 0, 0));
        Box(_head, new Vector3(0.5f, 0.12f, 0.34f), new Vector3(0, 0.62f, -0.2f), Hair, new Vector3(0.55f, 0, 0));
        Box(_head, new Vector3(0.52f, 0.34f, 0.2f), new Vector3(0, 0.38f, -0.26f), Hair, new Vector3(0.2f, 0, 0));
        Box(_head, new Vector3(0.1f, 0.22f, 0.34f), new Vector3(-0.28f, 0.42f, -0.08f), Hair);
        Box(_head, new Vector3(0.1f, 0.22f, 0.34f), new Vector3(0.28f, 0.42f, -0.08f), Hair);
        for (int i = -2; i <= 2; i++) Box(_head, new Vector3(0.04f, 0.03f, 0.5f), new Vector3(i * 0.1f, 0.68f, -0.06f), new Color(0.93f, 0.93f, 0.96f), new Vector3(0.2f, 0, 0));
        // long pointed ears
        _earL = MakeEar(-1f); _earR = MakeEar(1f);
    }

    Node3D MakeEar(float s)
    {
        var piv = Pivot(_head, new Vector3(0.27f * s, 0.36f, -0.02f));
        var ear = Cone(piv, 0.1f, 0.5f, new Vector3(0, 0.25f, 0), Skin);
        ear.Scale = new Vector3(1f, 1f, 0.3f);
        Cone(piv, 0.06f, 0.4f, new Vector3(0, 0.24f, 0.03f), new Color(0.78f, 0.55f, 0.5f)).Scale = new Vector3(1f, 1f, 0.2f);
        piv.Rotation = new Vector3(0, 0, -s * (Mathf.Pi / 2f - 0.5f));
        return piv;
    }

    void BuildCounter()
    {
        var counter = Pivot(this, new Vector3(0, 0, 1.05f));
        Box(counter, new Vector3(3.0f, 1.0f, 0.85f), new Vector3(0, 0.5f, 0), Wood);
        Box(counter, new Vector3(3.15f, 0.07f, 1.0f), new Vector3(0, 1.035f, 0), WoodLight);
        Box(counter, new Vector3(3.0f, 0.06f, 0.05f), new Vector3(0, 0.6f, 0.44f), new Color(0.78f, 0.58f, 0.2f), null, 0.15f);
        Box(counter, new Vector3(3.0f, 0.06f, 0.05f), new Vector3(0, 0.2f, 0.44f), new Color(0.78f, 0.58f, 0.2f), null, 0.15f);
        for (int i = -1; i <= 1; i++) Box(counter, new Vector3(0.7f, 0.9f, 0.04f), new Vector3(i * 0.95f, 0.5f, 0.43f), new Color(0.24f, 0.16f, 0.1f));
        // ledger (open book, tilted towards the miners)
        var led = Pivot(counter, new Vector3(0.45f, 1.08f, 0.12f)); led.Rotation = new Vector3(0, 0.15f, 0);
        Box(led, new Vector3(0.7f, 0.04f, 0.45f), Vector3.Zero, new Color(0.35f, 0.1f, 0.08f));
        Box(led, new Vector3(0.32f, 0.03f, 0.4f), new Vector3(-0.17f, 0.035f, 0), new Color(0.93f, 0.9f, 0.78f), new Vector3(0, 0, 0.06f));
        Box(led, new Vector3(0.32f, 0.03f, 0.4f), new Vector3(0.17f, 0.035f, 0), new Color(0.93f, 0.9f, 0.78f), new Vector3(0, 0, -0.06f));
        for (int i = 0; i < 4; i++) Box(led, new Vector3(0.24f, 0.004f, 0.012f), new Vector3(-0.17f, 0.056f, -0.13f + i * 0.08f), new Color(0.3f, 0.25f, 0.2f));
        // quill and inkpot
        Box(counter, new Vector3(0.1f, 0.1f, 0.1f), new Vector3(0.95f, 1.12f, 0.1f), new Color(0.08f, 0.08f, 0.12f));
        Box(counter, new Vector3(0.02f, 0.4f, 0.02f), new Vector3(0.95f, 1.35f, 0.1f), new Color(0.9f, 0.88f, 0.8f), new Vector3(0, 0, 0.25f));
        // pile of steins (power stones) and coins
        var cols = new[] { new Color(0.2f, 0.9f, 0.3f), new Color(0.3f, 0.7f, 1f), new Color(0.85f, 0.85f, 0.85f), new Color(0.9f, 0.6f, 0.2f), new Color(0.9f, 0.2f, 0.3f), new Color(0.75f, 0.4f, 1f) };
        var pos = new[] { new Vector3(-0.95f, 1.14f, 0.1f), new Vector3(-0.72f, 1.14f, 0.18f), new Vector3(-0.84f, 1.14f, -0.1f), new Vector3(-0.62f, 1.14f, -0.05f), new Vector3(-0.85f, 1.3f, 0.05f), new Vector3(-0.7f, 1.3f, 0.06f) };
        for (int i = 0; i < 6; i++) Sph(counter, 0.1f, pos[i], cols[i], null, 1.2f);
        for (int i = 0; i < 4; i++) Cone(counter, 0.1f, 0.025f, new Vector3(-1.25f, 1.085f + i * 0.026f, 0.15f), new Color(1f, 0.8f, 0.2f), new Vector3(0, i * 0.4f, 0), 0.1f).MaterialOverride = Util.Mat(new Color(1f, 0.8f, 0.2f), 0.35f);
        // nameplate
        Box(counter, new Vector3(0.9f, 0.18f, 0.05f), new Vector3(0, 1.15f, 0.42f), new Color(0.75f, 0.55f, 0.2f), new Vector3(-0.3f, 0, 0), 0.25f);
        var plate = new Label3D { Text = "G. GOBLIN  -  CEO", PixelSize = 0.0011f, FontSize = 48, Modulate = new Color(0.12f, 0.08f, 0.03f), OutlineSize = 0, Position = new Vector3(0, 1.15f, 0.447f), Rotation = new Vector3(-0.3f, 0, 0) };
        counter.AddChild(plate);
    }

    // ------------------------------------------------------------------ animation
    public override void _Process(double delta)
    {
        if (_head == null) return;
        float dt = (float)delta;
        _t += dt;
        if (_reactT > 0) { _reactT -= dt; if (_reactT <= 0) _react = ""; }

        // breathing
        float br = Mathf.Sin(_t * 1.9f);
        _torso.Scale = new Vector3(1f + 0.008f * br, 1f + 0.018f * br, 1f);
        _bounce = Mathf.Lerp(_bounce, _react == "happy" ? Mathf.Abs(Mathf.Sin(_t * 9f)) * 0.03f : 0f, Mathf.Min(1f, dt * 10f));
        _torso.Position = new Vector3(0, 0.72f + _bounce, 0);

        // head follows the nearest miner
        float targetYaw = 0.25f * Mathf.Sin(_t * 0.4f), targetPitch = 0f;
        var near = NearestMinerPos(out float dist);
        if (near.HasValue)
        {
            var local = ToLocal(near.Value);
            targetYaw = Mathf.Clamp(Mathf.Atan2(local.X, Mathf.Max(0.2f, local.Z)), -1.1f, 1.1f);
            targetPitch = Mathf.Clamp(Mathf.Atan2(local.Y - 1.9f, new Vector2(local.X, local.Z).Length()), -0.35f, 0.3f);
        }
        _yaw = Mathf.Lerp(_yaw, targetYaw, Mathf.Min(1f, dt * 4f));
        _pitch = Mathf.Lerp(_pitch, -targetPitch, Mathf.Min(1f, dt * 4f));

        // targets per state
        float t1 = -0.9f, t2 = -0.55f, yw = 0f, tilt = 0.06f, lean = 0f, brow = 0.12f, shake = 0f;
        float smugSwing = Mathf.Sin(_t * 0.7f);
        t1 += 0.04f * smugSwing;
        if (_react == "happy")
        {
            float k = Mathf.Sin(_t * 10f);
            t1 = -1.15f; t2 = -1.55f + 0.14f * k; yw = 0.52f + 0.06f * Mathf.Sin(_t * 10f + 1.5f);
            tilt = -0.18f; brow = 0.3f; lean = -0.06f;
        }
        else if (_react == "angry")
        {
            float ph = (_reactT * 4f) % 1f;
            t1 = ph < 0.55f ? -0.15f : -1.0f; t2 = ph < 0.55f ? -0.5f : -0.4f; yw = 0.1f;
            brow = -0.45f; lean = 0.14f; tilt = 0f; shake = Mathf.Sin(_t * 40f) * 0.03f;
        }
        float lp = Mathf.Min(1f, dt * 9f);
        _a1L = Mathf.Lerp(_a1L, t1, lp); _a1R = Mathf.Lerp(_a1R, t1, lp);
        _a2L = Mathf.Lerp(_a2L, t2, lp); _a2R = Mathf.Lerp(_a2R, t2, lp);
        _yawL = Mathf.Lerp(_yawL, yw, lp); _yawR = Mathf.Lerp(_yawR, yw, lp);
        _tilt = Mathf.Lerp(_tilt, tilt, lp); _lean = Mathf.Lerp(_lean, lean, lp);
        _browAngle = Mathf.Lerp(_browAngle, brow, lp);
        _shake = shake;
        // arm 0 is at +X (goblin's left), arm 1 at -X; inward yaw is negative for +X arm
        _sh[0].Rotation = new Vector3(_a1L, -_yawL, 0);
        _sh[1].Rotation = new Vector3(_a1R, _yawR, 0);
        _el[0].Rotation = new Vector3(_a2L, 0, 0);
        _el[1].Rotation = new Vector3(_a2R, 0, 0);
        _torso.Rotation = new Vector3(_lean, _shake, 0);
        _head.Rotation = new Vector3(_pitch - _lean * 0.5f, _yaw + _shake * 2f, _tilt);

        // brows: inner ends drop when angry (slant), rise slightly when happy
        _browL.Rotation = new Vector3(0, 0, -_browAngle * 0.9f);
        _browR.Rotation = new Vector3(0, 0, _browAngle * 0.9f);
        // eyes follow the miner a little within the head
        float eyeX = Mathf.Clamp((near.HasValue ? _yaw - targetYaw : 0f) * -0.04f, -0.02f, 0.02f);
        _pupilL.Position = new Vector3(-0.115f + eyeX, 0.335f, 0.315f);
        _pupilR.Position = new Vector3(0.115f + eyeX, 0.335f, 0.315f);
        _mouthEnd.Rotation = new Vector3(0, 0, _react == "angry" ? -0.9f : (_react == "happy" ? 0.5f : 0f));

        // ear twitch
        _earTimer -= dt;
        if (_earTimer <= 0) { _earTimer = 2.5f + (float)GD.RandRange(0, 4.5); _earTwitch = 0.35f; }
        _earTwitch = Mathf.Max(0, _earTwitch - dt);
        float tw = _earTwitch > 0 ? Mathf.Sin(_earTwitch * 55f) * 0.18f * (_earTwitch / 0.35f) : 0f;
        float basis = Mathf.Pi / 2f - 0.5f;
        _earL.Rotation = new Vector3(0, 0, basis + tw);
        _earR.Rotation = new Vector3(0, 0, -basis - tw * 0.3f);
    }

    Vector3? NearestMinerPos(out float dist)
    {
        dist = float.MaxValue; Vector3? best = null;
        var w = Game.I?.World;
        if (w?.MinersRoot == null) return null;
        foreach (var m in w.Miners())
        {
            if (!IsInstanceValid(m) || !m.IsInsideTree()) continue;
            float d = m.GlobalPosition.DistanceTo(GlobalPosition);
            if (d < dist && d < 16f) { dist = d; best = m.GlobalPosition + new Vector3(0, 1.6f, 0); }
        }
        return best;
    }

    /// <summary>Debug / screenshots: pretend a reaction happened.</summary>
    public void Debug(string kind) => OnReact(kind);
}
