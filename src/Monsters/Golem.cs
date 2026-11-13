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

    /// <summary>Hunched, narrow stone giant: 1.15 m wide and under 2 m tall, so it fits the one-wide tunnels. Dim red eyes burn in the dark.</summary>
    protected override void BuildModel()
    {
        var stone = new Color(0.42f, 0.41f, 0.45f);
        var dark = new Color(0.26f, 0.25f, 0.29f);
        var light = new Color(0.52f, 0.5f, 0.52f);
        _legL = Pivot(Rig, new Vector3(-0.27f, 0.95f, 0));
        _legR = Pivot(Rig, new Vector3(0.27f, 0.95f, 0));
        foreach (var l in new[] { _legL, _legR })
        {
            Box(l, new Vector3(0.42f, 0.95f, 0.5f), stone, new Vector3(0, -0.45f, 0));
            Box(l, new Vector3(0.5f, 0.22f, 0.7f), dark, new Vector3(0, -0.86f, -0.1f));
        }
        _torso = Pivot(Rig, new Vector3(0, 1.3f, 0.05f));
        _torso.Rotation = new Vector3(0.42f, 0, 0);             // hunched forward (animation adds to this)
        Box(_torso, new Vector3(0.78f, 0.95f, 0.58f), stone, Vector3.Zero);
        Box(_torso, new Vector3(0.62f, 0.3f, 0.5f), dark, new Vector3(0, -0.55f, 0));
        // front armour plates (the side that hits do nothing against)
        Box(_torso, new Vector3(0.66f, 0.8f, 0.12f), dark, new Vector3(0, 0.0f, -0.32f));
        Box(_torso, new Vector3(0.74f, 0.14f, 0.2f), light, new Vector3(0, 0.4f, -0.32f));
        Box(_torso, new Vector3(0.12f, 0.7f, 0.2f), light, new Vector3(-0.24f, 0, -0.34f));
        Box(_torso, new Vector3(0.12f, 0.7f, 0.2f), light, new Vector3(0.24f, 0, -0.34f));
        // rocky shoulders (hunched, a little above the head line)
        Box(_torso, new Vector3(0.4f, 0.4f, 0.5f), light, new Vector3(-0.47f, 0.34f, 0), 0, new Vector3(0.3f, 0.4f, 0.2f));
        Box(_torso, new Vector3(0.4f, 0.4f, 0.5f), light, new Vector3(0.47f, 0.34f, 0), 0, new Vector3(-0.2f, 0.3f, -0.3f));
        // head, low and forward between the shoulders
        _head = Pivot(_torso, new Vector3(0, 0.5f, -0.22f));
        Box(_head, new Vector3(0.5f, 0.42f, 0.5f), stone, Vector3.Zero);
        Box(_head, new Vector3(0.56f, 0.14f, 0.3f), dark, new Vector3(0, 0.12f, -0.2f)); // brow
        var ember = new Color(1f, 0.1f, 0.05f);
        _eyeL = Box(_head, new Vector3(0.1f, 0.06f, 0.06f), ember, new Vector3(-0.12f, 0.03f, -0.26f), 2.4f);
        _eyeR = Box(_head, new Vector3(0.1f, 0.06f, 0.06f), ember, new Vector3(0.12f, 0.03f, -0.26f), 2.4f);
        _eyeGlow = new OmniLight3D { Position = new Vector3(0, 0.05f, -0.45f), LightColor = new Color(1f, 0.12f, 0.06f), LightEnergy = 0.5f, OmniRange = 3.5f, ShadowEnabled = false };
        _head.AddChild(_eyeGlow);
        // long arms (pivot at the shoulder, hang to the knees)
        _armL = Pivot(_torso, new Vector3(-0.52f, 0.3f, 0));
        _armR = Pivot(_torso, new Vector3(0.52f, 0.3f, 0));
        foreach (var a in new[] { _armL, _armR })
        {
            Box(a, new Vector3(0.26f, 1.1f, 0.3f), stone, new Vector3(0, -0.55f, 0));
            Box(a, new Vector3(0.4f, 0.5f, 0.45f), light, new Vector3(0, -1.2f, 0));
        }
        // glowing crystal on the back = weak point
        var cyan = new Color(0.3f, 0.95f, 1f);
        _crystal = Box(_torso, new Vector3(0.3f, 0.6f, 0.3f), cyan, new Vector3(0, 0.1f, 0.38f), 2.5f, new Vector3(0.5f, 0, 0.6f));
        Box(_torso, new Vector3(0.18f, 0.36f, 0.18f), cyan, new Vector3(0.26f, -0.2f, 0.34f), 2.5f, new Vector3(0.2f, 0.4f, -0.5f));
        Box(_torso, new Vector3(0.18f, 0.34f, 0.18f), cyan, new Vector3(-0.25f, 0.28f, 0.34f), 2.5f, new Vector3(0.1f, -0.3f, 0.7f));
        _light = new OmniLight3D { Position = new Vector3(0, 1.7f, 0.9f), LightColor = cyan, LightEnergy = 0.9f, OmniRange = 4.5f, ShadowEnabled = false };
        Rig.AddChild(_light);
    }

    protected override void BuildBody()
    {
        AddBox(new Vector3(1.1f, 1.95f, 1.0f), new Vector3(0, 0.98f, 0));
    }

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
        float sw = Mathf.Clamp(AnimSpeed / 2.2f, 0f, 1.2f);
        AnimPhase += AnimSpeed * dt * 1.7f;
        float age = StateAge;
        float breathe = Mathf.Sin(Time.GetTicksMsec() / 700f) * 0.015f;
        float legSwing = Mathf.Sin(AnimPhase) * 0.5f * sw;
        _legL.Rotation = new Vector3(legSwing, 0, 0);
        _legR.Rotation = new Vector3(-legSwing, 0, 0);
        Rig.Position = new Vector3(0, Mathf.Abs(Mathf.Cos(AnimPhase)) * 0.11f * sw, 0);
        _torso.Rotation = new Vector3(0.42f, Mathf.Sin(AnimPhase) * 0.07f * sw, 0);
        _torso.Scale = Vector3.One * (1f + breathe + (Flash > 0 ? 0.05f : 0f));

        float armTarget; float rate = 10f; float lean = 0f;
        float swing = Mathf.Sin(AnimPhase) * 0.35f * sw;
        switch (State)
        {
            case MonsterState.Windup:
                {
                    float t = Mathf.Clamp(age / MonsterTuning.GolemWindup, 0f, 1f);
                    armTarget = 2.9f * (1f - Mathf.Pow(1f - t, 2f)); rate = 6f; lean = -0.3f * t;
                    // glow brighter as the slam nears: readable warning
                    _crystal.Scale = Vector3.One * (1f + 0.15f * Mathf.Sin(age * 18f));
                    break;
                }
            case MonsterState.Attack: armTarget = 0.5f; rate = 30f; lean = 0.25f; break;
            case MonsterState.Recover: armTarget = age < 0.9f ? 0.5f : 0.0f; rate = 4f; lean = 0.15f * Mathf.Clamp(1f - age, 0f, 1f); break;
            case MonsterState.Stunned: armTarget = 0.15f; rate = 3f; lean = 0.55f; break;
            default: armTarget = 0f; break;
        }
        if (State != MonsterState.Windup) _crystal.Scale = Vector3.One;
        _armX = Mathf.Lerp(_armX, armTarget, 1f - Mathf.Exp(-rate * dt));
        // arms swing in opposition to legs when walking; slam overrides
        bool slam = State == MonsterState.Windup || State == MonsterState.Attack || State == MonsterState.Recover || State == MonsterState.Stunned;
        _armL.Rotation = new Vector3(slam ? _armX : -legSwing * 0.7f + swing * 0f, 0, 0.08f);
        _armR.Rotation = new Vector3(slam ? _armX : legSwing * 0.7f, 0, -0.08f);
        _torso.Rotation += new Vector3(lean, 0, 0);
        _head.Rotation = new Vector3(State == MonsterState.Stunned ? 0.5f : 0f, State == MonsterState.Investigate ? Mathf.Sin(age * 2f) * 0.5f : 0f, 0);
        float pulse = 1.2f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 300f);
        _light.LightEnergy = State == MonsterState.Windup ? 2f : pulse * 0.7f;
        // eyes: steady dim red; flare in the wind-up, dark when stunned
        bool eyesOn = State != MonsterState.Stunned;
        float eyeE = State == MonsterState.Windup ? 1.6f : State == MonsterState.Chase ? 1.0f : 0.6f;
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
}
