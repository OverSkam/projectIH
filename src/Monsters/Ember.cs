using System;
using Godot;

namespace AppStein;

/// <summary>Ember Elemental: small, fast, glowing light source. Burns on contact; dies in water.</summary>
public partial class EmberObj : MonsterObj
{
    OmniLight3D _light;
    Node3D _core, _flames, _armL, _armR;
    MeshInstance3D[] _spikes;
    float _auraCd, _lungeT;
    bool _lungeHit;
    Vector3 _lungeDir;

    public EmberObj()
    {
        WalkSpeed = MonsterTuning.EmberWalk; InvestigateSpeed = 3.5f; ChaseSpeed = MonsterTuning.EmberChase;
        TurnRate = MonsterTuning.EmberTurn; MoveAlign = 1.2f;
        SightDark = MonsterTuning.EmberSightDark; SightLantern = MonsterTuning.EmberSightLantern;
        AttackRange = 2.4f; AttackAlign = 0.6f; BodyRadius = 0.4f; EyeH = 0.9f; WanderRadius = 7f; HearMul = 1.1f;
    }

    protected override void BuildModel()
    {
        var orange = new Color(1f, 0.45f, 0.08f);
        var yellow = new Color(1f, 0.85f, 0.25f);
        _core = Pivot(Rig, new Vector3(0, 0.75f, 0));
        var body = Util.SphereMesh(0.42f, orange, 1.2f); _core.AddChild(body);
        var inner = Util.SphereMesh(0.26f, yellow, 1.6f); _core.AddChild(inner);
        _flames = Pivot(_core, Vector3.Zero);
        _spikes = new MeshInstance3D[7];
        for (int i = 0; i < _spikes.Length; i++)
        {
            float a = i * Mathf.Tau / _spikes.Length;
            var cone = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.17f, Height = 0.7f, RadialSegments = 5, Rings = 1 }, MaterialOverride = Util.Mat(i % 2 == 0 ? orange : yellow, 1.3f) };
            cone.Position = new Vector3(Mathf.Cos(a) * 0.18f, 0.38f + (i % 3) * 0.08f, Mathf.Sin(a) * 0.18f);
            cone.Rotation = new Vector3(Mathf.Sin(a) * 0.5f, 0, -Mathf.Cos(a) * 0.5f);
            _flames.AddChild(cone);
            _spikes[i] = cone;
        }
        // face (dark eyes so it reads as a creature) and stubby claws
        Box(_core, new Vector3(0.1f, 0.12f, 0.06f), new Color(0.15f, 0.02f, 0.0f), new Vector3(-0.12f, 0.05f, -0.3f));
        Box(_core, new Vector3(0.1f, 0.12f, 0.06f), new Color(0.15f, 0.02f, 0.0f), new Vector3(0.12f, 0.05f, -0.3f));
        _armL = Pivot(_core, new Vector3(-0.34f, -0.05f, 0));
        _armR = Pivot(_core, new Vector3(0.34f, -0.05f, 0));
        foreach (var a in new[] { _armL, _armR }) Box(a, new Vector3(0.12f, 0.45f, 0.12f), orange, new Vector3(0, -0.2f, 0), 1.2f);
        _light = new OmniLight3D { Position = new Vector3(0, 0.9f, 0), LightColor = new Color(1f, 0.55f, 0.2f), LightEnergy = 2.4f, OmniRange = 8f, OmniAttenuation = 1.1f, ShadowEnabled = false };
        Rig.AddChild(_light);
    }

    protected override void BuildBody()
    {
        AddBox(new Vector3(0.9f, 1.4f, 0.9f), new Vector3(0, 0.75f, 0));
    }

    // ------------------------------------------------------------------ server
    protected override bool Allowed(Vector3 np, bool chasing)
    {
        if (!base.Allowed(np, chasing)) return false;
        // reluctant to enter water unless it is chasing
        if (!chasing && Cave.InWater(np + new Vector3(0, 0.3f, 0))) return false;
        return true;
    }

    protected override bool WanderOk(Vector3 g) => base.WanderOk(g) && !Cave.InWater(g + new Vector3(0, 0.3f, 0));

    protected override void ServerThink(float dt)
    {
        if (State != MonsterState.Dead && Cave.InWater(GlobalPosition + new Vector3(0, 0.3f, 0)))
        {
            Die(null, "water");
            return;
        }
        if (State != MonsterState.Dead && !Frozen)
        {
            _auraCd -= dt;
            if (_auraCd <= 0)
                foreach (var p in Game.I.Players.Values)
                {
                    if (!p.Alive || !p.Participant || Cave.InCamp(p.Pos)) continue;
                    if (Flat(p.Pos - GlobalPosition).Length() <= MonsterTuning.EmberAuraRadius && Mathf.Abs(p.Pos.Y - GlobalPosition.Y) < 2f)
                    {
                        _auraCd = MonsterTuning.EmberAuraInterval;
                        float dealt = p.TakeDamage(MonsterTuning.EmberAuraDamage, null, "fire");
                        MonsterModule.Ignite(p, MonsterTuning.EmberBurnSeconds);
                        Util.Pass("monster_hit_miner", $"kind=ember victim={p.Name} dmg={dealt:0.#} hp={p.Health:0.#} burn=1");
                    }
                }
        }
        base.ServerThink(dt);
    }

    protected override float ModifyMelee(ServerPlayer p, float dmg) => dmg; // weak: any hit counts

    protected override void DropLoot() => SpawnLoot("ruby", Vector3.Zero);

    protected override bool TryStartAttack(ServerPlayer t) { SetState(MonsterState.Windup); return true; }

    protected override void AttackTick(float dt)
    {
        float age = StateAge;
        var pos = GlobalPosition;
        switch (State)
        {
            case MonsterState.Windup:
                if (Target != null)
                    Yaw = RotateToward(Yaw, Mathf.Atan2(-(Target.Pos.X - pos.X), -(Target.Pos.Z - pos.Z)), 6f * dt);
                if (age >= MonsterTuning.EmberWindup) { _lungeDir = Forward; _lungeHit = false; SetState(MonsterState.Attack); }
                break;
            case MonsterState.Attack:
                Step(pos + _lungeDir * MonsterTuning.EmberLungeSpeed * dt, true, dt);
                if (!_lungeHit)
                    foreach (var p in Game.I.Players.Values)
                    {
                        if (!p.Alive || !p.Participant || Cave.InCamp(p.Pos)) continue;
                        if (Flat(p.Pos - GlobalPosition).Length() < 1.3f && Mathf.Abs(p.Pos.Y - GlobalPosition.Y) < 2f)
                        {
                            _lungeHit = true;
                            float dealt = p.TakeDamage(MonsterTuning.EmberLungeDamage, null, "fire");
                            MonsterModule.Ignite(p, MonsterTuning.EmberBurnSeconds);
                            Util.Pass("monster_hit_miner", $"kind=ember victim={p.Name} dmg={dealt:0.#} hp={p.Health:0.#} burn=1");
                        }
                    }
                if (age >= MonsterTuning.EmberLunge) SetState(MonsterState.Recover);
                break;
            case MonsterState.Recover:
                if (age >= MonsterTuning.EmberRecover) { LostT = 0; SetState(MonsterState.Chase); }
                break;
        }
    }

    // ------------------------------------------------------------------ animation
    protected override void Animate(float dt)
    {
        float t = Time.GetTicksMsec() / 1000f;
        float age = StateAge;
        AnimPhase += (2f + AnimSpeed) * dt * 3f;
        float bob = 0.1f * Mathf.Sin(t * 5f) + (AnimSpeed > 1f ? 0.05f * Mathf.Sin(AnimPhase * 2f) : 0f);
        float scale = 1f; float energy = 2.4f;
        float lean = Mathf.Clamp(AnimSpeed / 6f, 0f, 1f) * 0.5f;
        switch (State)
        {
            case MonsterState.Windup:
                { float k = Mathf.Clamp(age / MonsterTuning.EmberWindup, 0f, 1f); scale = 1f + 0.6f * k; energy = 2.4f + 4f * k; lean = -0.5f * k; break; }
            case MonsterState.Attack: scale = 1.25f; energy = 5f; lean = 0.9f; break;
            case MonsterState.Recover: scale = 0.85f; energy = 1.5f; break;
            case MonsterState.Dead:
                scale = Mathf.Max(0.05f, 1f - age * 2.5f); energy = Mathf.Max(0f, 2f - age * 3f); break;
        }
        _core.Position = new Vector3(0, 0.75f + bob, 0);
        _core.Scale = Vector3.One * scale * (Flash > 0 ? 1.15f : 1f);
        _core.Rotation = new Vector3(lean, 0, 0);
        for (int i = 0; i < _spikes.Length; i++)
            _spikes[i].Scale = new Vector3(1f, 0.8f + 0.5f * Mathf.Sin(t * 14f + i * 1.9f), 1f);
        _flames.Rotation = new Vector3(0, t * 1.5f, 0);
        _armL.Rotation = new Vector3(Mathf.Sin(AnimPhase) * 0.6f, 0, 0.2f);
        _armR.Rotation = new Vector3(-Mathf.Sin(AnimPhase) * 0.6f, 0, -0.2f);
        _light.LightEnergy = energy * (0.9f + 0.15f * Mathf.Sin(t * 23f) + 0.1f * Mathf.Sin(t * 37f));
    }
}
