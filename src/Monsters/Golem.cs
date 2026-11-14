using System;
using Godot;

namespace AppStein;

/// <summary>Stone Golem: slow, armoured from the front, telegraphed overhead slam, glowing crystal on its back.</summary>
public partial class GolemObj : MonsterObj
{
    Node3D _legL, _legR, _armL, _armR, _torso, _head;
    MeshInstance3D _crystal;
    OmniLight3D _light;
    float _armX, _slamDone;
    bool _impacted;

    public GolemObj()
    {
        WalkSpeed = MonsterTuning.GolemWalk; InvestigateSpeed = MonsterTuning.GolemInvestigate; ChaseSpeed = MonsterTuning.GolemChase;
        TurnRate = MonsterTuning.GolemTurn; MoveAlign = 0.8f; SightCos = MonsterTuning.GolemSightCos; SightLantern = 30f;
        AttackRange = MonsterTuning.GolemSlamReach - 0.4f; AttackAlign = 0.35f; BodyRadius = 0.5f; EyeH = 1.7f;
        Profile = NavProfile.Tall; IdleMin = 0.3f; IdleRand = 1.2f;
    }

    MeshInstance3D _eyeL, _eyeR;
    OmniLight3D _eyeGlow;
    Node3D _body, _pelvis;
    CollisionShape3D _col;
    float _posture = 1f;                 // 1 = standing tall (rooms), 0 = crouched (tunnels)
    int _stepIdx = int.MinValue;
    const float Hip = 1.05f;             // standing hip height (m)

    /// <summary>Imposing stone giant: 2.9 m standing, about 2.3 m across the shoulders. In low ceilings (tunnels, doorways) it crouches (hip down, torso pitched forward, shoulders drawn in) to about 1.8 m.
    /// Dim red eyes burn in the dark, a cyan crystal on its back is the weak point.</summary>
    protected override void BuildModel()
    {
        var stone = new Color(0.42f, 0.41f, 0.45f);
        var dark = new Color(0.26f, 0.25f, 0.29f);
        var light = new Color(0.52f, 0.5f, 0.52f);
        var moss = new Color(0.30f, 0.36f, 0.27f);
        _body = new Node3D { Name = "Body" }; Rig.AddChild(_body);
        _legL = Pivot(_body, new Vector3(-0.5f, Hip, 0));
        _legR = Pivot(_body, new Vector3(0.5f, Hip, 0));
        foreach (var l in new[] { _legL, _legR })
        {
            Box(l, new Vector3(0.62f, 1.0f, 0.7f), stone, new Vector3(0, -0.5f, 0));
            Box(l, new Vector3(0.7f, 0.22f, 0.78f), dark, new Vector3(0, -0.1f, 0));        // hip guard
            Box(l, new Vector3(0.78f, 0.27f, 1.05f), dark, new Vector3(0, -0.93f, -0.14f));
            Box(l, new Vector3(0.5f, 0.3f, 0.2f), moss, new Vector3(0.05f, -0.45f, -0.36f), 0, new Vector3(0, 0.3f, 0));
        }
        _pelvis = Pivot(_body, new Vector3(0, Hip, 0));     // the torso pitches about the hips
        _torso = Pivot(_pelvis, Vector3.Zero);
        Box(_torso, new Vector3(1.5f, 1.25f, 0.95f), stone, new Vector3(0, 0.68f, 0));
        Box(_torso, new Vector3(1.2f, 0.4f, 0.85f), dark, new Vector3(0, 0.12f, 0));
        // front armour plates (the side that hits do nothing against)
        Box(_torso, new Vector3(1.3f, 1.05f, 0.18f), dark, new Vector3(0, 0.72f, -0.52f));
        Box(_torso, new Vector3(1.45f, 0.2f, 0.3f), light, new Vector3(0, 1.2f, -0.52f));
        Box(_torso, new Vector3(0.18f, 0.95f, 0.3f), light, new Vector3(-0.46f, 0.7f, -0.55f));
        Box(_torso, new Vector3(0.18f, 0.95f, 0.3f), light, new Vector3(0.46f, 0.7f, -0.55f));
        Box(_torso, new Vector3(0.5f, 0.3f, 0.3f), moss, new Vector3(0.1f, 0.35f, -0.6f), 0, new Vector3(0, 0, 0.2f));
        // huge rocky shoulders
        Box(_torso, new Vector3(0.8f, 0.7f, 0.95f), light, new Vector3(-0.98f, 1.2f, 0), 0, new Vector3(0.3f, 0.4f, 0.25f));
        Box(_torso, new Vector3(0.8f, 0.7f, 0.95f), light, new Vector3(0.98f, 1.2f, 0), 0, new Vector3(-0.2f, 0.3f, -0.3f));
        Box(_torso, new Vector3(0.45f, 0.4f, 0.5f), stone, new Vector3(-1.1f, 1.42f, 0.1f), 0, new Vector3(0.5f, 0.2f, 0.4f));
        Box(_torso, new Vector3(0.4f, 0.4f, 0.45f), stone, new Vector3(1.08f, 1.4f, -0.05f), 0, new Vector3(-0.3f, 0.6f, -0.3f));
        // head, low and forward between the shoulders
        _head = Pivot(_torso, new Vector3(0, 1.5f, -0.28f));
        Box(_head, new Vector3(0.8f, 0.68f, 0.8f), stone, Vector3.Zero);
        Box(_head, new Vector3(0.9f, 0.2f, 0.5f), dark, new Vector3(0, 0.2f, -0.3f)); // brow
        Box(_head, new Vector3(0.55f, 0.22f, 0.3f), dark, new Vector3(0, -0.3f, -0.28f)); // jaw
        var ember = new Color(1f, 0.1f, 0.05f);
        _eyeL = Box(_head, new Vector3(0.17f, 0.1f, 0.08f), ember, new Vector3(-0.2f, 0.04f, -0.42f), 2.4f);
        _eyeR = Box(_head, new Vector3(0.17f, 0.1f, 0.08f), ember, new Vector3(0.2f, 0.04f, -0.42f), 2.4f);
        _eyeGlow = new OmniLight3D { Position = new Vector3(0, 0.05f, -0.7f), LightColor = new Color(1f, 0.12f, 0.06f), LightEnergy = 0.2f, OmniRange = 3.2f, ShadowEnabled = false };
        _head.AddChild(_eyeGlow);
        // long, heavy arms (pivot at the shoulder, knuckles nearly at the floor)
        _armL = Pivot(_torso, new Vector3(-1.0f, 1.05f, 0));
        _armR = Pivot(_torso, new Vector3(1.0f, 1.05f, 0));
        foreach (var a in new[] { _armL, _armR })
        {
            Box(a, new Vector3(0.46f, 1.55f, 0.5f), stone, new Vector3(0, -0.8f, 0));
            Box(a, new Vector3(0.62f, 0.7f, 0.7f), light, new Vector3(0, -1.75f, 0));
            Box(a, new Vector3(0.5f, 0.18f, 0.55f), dark, new Vector3(0, -0.1f, 0));
        }
        // glowing crystal cluster on the back = weak point
        var cyan = new Color(0.3f, 0.95f, 1f);
        _crystal = Box(_torso, new Vector3(0.5f, 0.95f, 0.5f), cyan, new Vector3(0, 0.95f, 0.62f), 2.5f, new Vector3(-0.5f, 0, 0.6f));
        Box(_torso, new Vector3(0.3f, 0.6f, 0.3f), cyan, new Vector3(0.42f, 0.45f, 0.58f), 2.5f, new Vector3(-0.2f, 0.4f, -0.5f));
        Box(_torso, new Vector3(0.3f, 0.56f, 0.3f), cyan, new Vector3(-0.4f, 1.15f, 0.56f), 2.5f, new Vector3(-0.1f, -0.3f, 0.7f));
        _light = new OmniLight3D { Position = new Vector3(0, 2.4f, 1.3f), LightColor = cyan, LightEnergy = 0.9f, OmniRange = 6.5f, ShadowEnabled = false };
        Rig.AddChild(_light);
    }

    protected override void BuildBody()
    {
        _col = Util.BoxShape(new Vector3(1.7f, 2.85f, 1.3f), new Vector3(0, 1.425f, 0));
        AddChild(_col);
    }

    /// <summary>Lowest ceiling over the cell it stands in and the cells just ahead (the doorway it is about to enter).</summary>
    float CeilingHere()
    {
        if (ModularCave.Last is not { } mc) return 9f;
        float c = 99f;
        foreach (var o in new[] { Vector3.Zero, Forward * 2.2f, Forward * 1.1f })
        {
            var p = GlobalPosition + o;
            ref var cell = ref mc.At((int)Math.Round(p.X / 2f), (int)Math.Round(p.Z / 2f));
            if (cell.Open) c = Mathf.Min(c, cell.Ceil);
        }
        return c > 90f ? 9f : c;
    }

    /// <summary>Collision box (width, height, depth) for a posture (0 crouched .. 1 standing).</summary>
    public static Vector3 BoxFor(float posture) => new(Mathf.Lerp(1.15f, 1.7f, posture), Mathf.Lerp(1.9f, 2.85f, posture), Mathf.Lerp(1.5f, 1.3f, posture));
    public float Posture => _posture;

    // ------------------------------------------------------------------ server: the back crystal
    /// <summary>The golem cannot be hurt. A hit on the glowing crystal on its back (from behind) stuns it for GolemStunSeconds; hits from the front or side do nothing.</summary>
    public override void ServerHit(ServerPlayer p, float mining, bool pickaxe)
    {
        if (!IsSrv || State == MonsterState.Stunned || !pickaxe) return;
        var toAtt = Flat(p.Pos - GlobalPosition);
        if (toAtt.LengthSquared() < 0.01f) return;
        if (Forward.Dot(toAtt.Normalized()) < MonsterTuning.GolemRearDot) Stun(MonsterTuning.GolemStunSeconds);
    }

    // ------------------------------------------------------------------ server: slam
    protected override bool TryStartAttack(ServerPlayer t)
    {
        _impacted = false;
        SetState(MonsterState.Windup);
        return true;
    }

    protected override void AttackTick(float dt)
    {
        float age = StateAge;
        switch (State)
        {
            case MonsterState.Windup:
                // slowly keeps turning toward the target; the telegraph is long enough to sidestep
                if (Target != null && Target.Alive)
                {
                    var pos = GlobalPosition;
                    float want = Mathf.Atan2(-(Target.Pos.X - pos.X), -(Target.Pos.Z - pos.Z));
                    Yaw = RotateToward(Yaw, want, TurnRate * 0.35f * dt);
                }
                if (age >= MonsterTuning.GolemWindup) { _impacted = false; SetState(MonsterState.Attack); Impact(); }
                break;
            case MonsterState.Attack:
                if (age >= MonsterTuning.GolemSlamTime) SetState(MonsterState.Recover);
                break;
            case MonsterState.Recover:
                if (age >= MonsterTuning.GolemRecover) { LostT = 0; SetState(MonsterState.Chase); }
                break;
        }
    }

    void Impact()
    {
        if (_impacted) return;
        _impacted = true;
        var pos = GlobalPosition; var fwd = Forward;
        foreach (var p in Game.I.Players.Values)
        {
            if (!p.Alive || !p.Participant || Cave.InHall(p.Pos)) continue;
            var rel = Flat(p.Pos - pos);
            float d = rel.Length();
            if (d > MonsterTuning.GolemSlamReach) continue;
            if (d > 1.0f && Mathf.Acos(Mathf.Clamp(fwd.Dot(rel / d), -1f, 1f)) > MonsterTuning.GolemSlamArc) continue;
            float dealt = p.TakeDamage(MonsterTuning.GolemSlamDamage, null, "monster");
            var kb = (d > 0.1f ? rel / d : fwd) * MonsterTuning.GolemKnockback + new Vector3(0, 4f, 0);
            Game.I.ServerKnockback(p.Peer, kb);
            Util.Pass("monster_hit_miner", $"kind=golem victim={p.Name} dmg={dealt:0.#} hp={p.Health:0.#}");
        }
    }

    // ------------------------------------------------------------------ animation
    protected override void Animate(float dt)
    {
        float sw = Mathf.Clamp(AnimSpeed / 2.6f, 0f, 1.2f);
        AnimPhase += AnimSpeed * dt * 1.15f;
        float age = StateAge;
        float breathe = Mathf.Sin(Time.GetTicksMsec() / 700f) * 0.015f;

        // posture follows the ceiling: stands tall in rooms, crouches into tunnels and doorways
        float want = Mathf.Clamp((CeilingHere() - 2.35f) / 1.35f, 0f, 1f);
        _posture = Mathf.MoveToward(_posture, want, dt * 1.6f);
        float p = _posture;
        var box = BoxFor(p);
        if (_col != null) { ((BoxShape3D)_col.Shape).Size = box; _col.Position = new Vector3(0, box.Y * 0.5f, 0); }
        float hip = Mathf.Lerp(0.52f, Hip, p), legK = hip / Hip;
        _body.Scale = new Vector3(Mathf.Lerp(0.56f, 1f, p), 1f, 1f);
        _legL.Position = new Vector3(-0.5f, hip, 0); _legR.Position = new Vector3(0.5f, hip, 0);
        _legL.Scale = _legR.Scale = new Vector3(1f, legK, 1f);
        _pelvis.Position = new Vector3(0, hip, 0);

        float legSwing = Mathf.Sin(AnimPhase) * (0.42f + 0.1f * (1f - p)) * sw;
        _legL.Rotation = new Vector3(legSwing, 0, 0);
        _legR.Rotation = new Vector3(-legSwing, 0, 0);
        Rig.Position = new Vector3(0, Mathf.Abs(Mathf.Cos(AnimPhase)) * 0.14f * sw, 0);
        // footfalls: heavy thud on every step, the ground shakes for miners nearby
        int step = Mathf.FloorToInt(AnimPhase / Mathf.Pi);
        if (_stepIdx == int.MinValue) _stepIdx = step;
        if (step != _stepIdx)
        {
            _stepIdx = step;
            if (sw > 0.25f && State != MonsterState.Stunned) Footfall();
        }
        float basePitch = Mathf.Lerp(-0.95f, -0.16f, p);   // negative = leaning forward

        float armTarget; float rate = 10f; float lean = 0f;
        switch (State)
        {
            case MonsterState.Windup:
                {
                    float t = Mathf.Clamp(age / MonsterTuning.GolemWindup, 0f, 1f);
                    armTarget = 2.9f * (1f - Mathf.Pow(1f - t, 2f)); rate = 6f; lean = 0.32f * t;
                    // glow brighter as the slam nears: readable warning
                    _crystal.Scale = Vector3.One * (1f + 0.15f * Mathf.Sin(age * 18f));
                    break;
                }
            case MonsterState.Attack: armTarget = 0.5f; rate = 30f; lean = -0.4f; break;
            case MonsterState.Recover: armTarget = age < 0.9f ? 0.5f : 0.0f; rate = 4f; lean = -0.25f * Mathf.Clamp(1f - age, 0f, 1f); break;
            case MonsterState.Stunned: armTarget = 0.15f; rate = 3f; lean = -0.35f; break;
            default: armTarget = 0f; break;
        }
        if (State != MonsterState.Windup) _crystal.Scale = Vector3.One;
        _armX = Mathf.Lerp(_armX, armTarget, 1f - Mathf.Exp(-rate * dt));
        _torso.Rotation = new Vector3(basePitch + lean, Mathf.Sin(AnimPhase) * 0.07f * sw, 0);
        _torso.Scale = Vector3.One * (1f + breathe + (Flash > 0 ? 0.05f : 0f));
        // arms swing in opposition to legs when walking; slam overrides. Crouched, the arms trail forward like knuckles.
        bool slam = State == MonsterState.Windup || State == MonsterState.Attack || State == MonsterState.Recover || State == MonsterState.Stunned;
        float armBase = (1f - p) * 0.9f;
        _armL.Rotation = new Vector3(slam ? _armX : armBase - legSwing * 0.7f, 0, 0.08f);
        _armR.Rotation = new Vector3(slam ? _armX : armBase + legSwing * 0.7f, 0, -0.08f);
        _head.Rotation = new Vector3((State == MonsterState.Stunned ? 0.5f : 0f) - basePitch * 0.55f, State == MonsterState.Investigate ? Mathf.Sin(age * 2f) * 0.5f : 0f, 0);
        float pulse = 1.2f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 300f);
        _light.LightEnergy = State == MonsterState.Windup ? 2f : pulse * 0.7f;
        // eyes: steady dim red; flare in the wind-up, dark when stunned
        bool eyesOn = State != MonsterState.Stunned;
        float eyeE = State == MonsterState.Windup ? 0.7f : State == MonsterState.Chase ? 0.4f : 0.2f;
        _eyeGlow.Visible = eyesOn; _eyeGlow.LightEnergy = eyeE;
        _eyeL.Visible = _eyeR.Visible = eyesOn;
        if (State == MonsterState.Stunned)
        {
            // slumped, crystal flickering weakly
            Rig.Scale = new Vector3(1f, 0.93f, 1f);
            _light.LightEnergy = 0.3f + 0.5f * Mathf.Abs(Mathf.Sin(Time.GetTicksMsec() / 90f));
        }
        else Rig.Scale = Vector3.One;
    }

    /// <summary>A heavy step: thud sound and, for the local miner within 8 m, a short camera shake that fades with distance.</summary>
    void Footfall()
    {
        Sfx.Play(SfxNames.GolemStep, GlobalPosition, 3f);
        var me = Game.I?.LocalMiner; if (!GodotObject.IsInstanceValid(me)) return;
        if (me == null) return;
        float d = me.GlobalPosition.DistanceTo(GlobalPosition);
        if (d < 8f) me.AddShake(Mathf.Lerp(1f, 0.15f, d / 8f));
    }
}
