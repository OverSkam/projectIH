using System;
using Godot;

namespace AppStein;

/// <summary>Gas Wisp: drifting poison cloud. Passes through gaps (flies), cannot be hurt by melee, disperses only by explosion.</summary>
public partial class WispObj : MonsterObj
{
    MeshInstance3D[] _puffs;
    Vector3[] _puffBase;
    OmniLight3D _light;
    Node3D _eyes;
    float _gasCd, _disperseLeft;
    static StandardMaterial3D _gasMat;

    public WispObj()
    {
        WalkSpeed = MonsterTuning.WispSpeed; InvestigateSpeed = MonsterTuning.WispInvestigate; ChaseSpeed = MonsterTuning.WispSpeed;
        TurnRate = 2f; MoveAlign = 3.2f; Flying = true; Hover = MonsterTuning.WispHover;
        SightDark = MonsterTuning.WispSightDark; SightLantern = MonsterTuning.WispSightLantern; HearMul = MonsterTuning.WispHearMul;
        AttackRange = 0f; BodyRadius = 1f; EyeH = 1.2f; WanderRadius = 7f;
    }

    static StandardMaterial3D GasMat()
    {
        if (_gasMat != null) return _gasMat;
        _gasMat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(0.5f, 0.95f, 0.25f, 0.28f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return _gasMat;
    }

    protected override void BuildModel()
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(Id * 13 + 5) };
        _puffs = new MeshInstance3D[9];
        _puffBase = new Vector3[_puffs.Length];
        for (int i = 0; i < _puffs.Length; i++)
        {
            float r = i == 0 ? 1.3f : rng.RandfRange(0.6f, 1.1f);
            var m = new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 10, Rings = 5 }, MaterialOverride = GasMat() };
            var p = i == 0 ? Vector3.Zero : new Vector3(rng.RandfRange(-1.6f, 1.6f), rng.RandfRange(-0.5f, 0.9f), rng.RandfRange(-1.6f, 1.6f));
            m.Position = p; _puffBase[i] = p;
            Rig.AddChild(m);
            _puffs[i] = m;
        }
        // a pair of pale eyes so it reads as a creature, not just fog
        _eyes = Pivot(Rig, new Vector3(0, 0.2f, 0));
        Box(_eyes, new Vector3(0.28f, 0.2f, 0.1f), new Color(0.9f, 1f, 0.6f), new Vector3(-0.4f, 0.1f, -1.0f), 2f);
        Box(_eyes, new Vector3(0.28f, 0.2f, 0.1f), new Color(0.9f, 1f, 0.6f), new Vector3(0.4f, 0.1f, -1.0f), 2f);
        _light = new OmniLight3D { LightColor = new Color(0.5f, 1f, 0.3f), LightEnergy = 1.0f, OmniRange = 6f, ShadowEnabled = false };
        Rig.AddChild(_light);
    }

    protected override void BuildBody()
    {
        // No solid collision against walls; a small sphere so melee swings register (and do nothing).
        AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 1.0f }, Position = Vector3.Zero });
    }

    public override void ServerHit(ServerPlayer p, float mining, bool pickaxe)
    {
        if (IsSrv) Util.Pass("wisp_melee_ignored", $"by={p?.Name}");
    }

    /// <summary>Server: the cloud scatters for 'seconds' (explosions). It does no damage while dispersed.</summary>
    public void Disperse(float seconds)
    {
        if (!IsSrv) return;
        _disperseLeft = seconds;
        SetState(MonsterState.Dispersed);
        Util.Pass("wisp_dispersed", $"id={Id} seconds={seconds:0.#}");
    }

    protected override bool WanderOk(Vector3 g) => !HallBlock(g) && Cave.RoomAt(Home)?.Bounds.Grow(-1f).HasPoint(new Vector3(g.X, Cave.RoomAt(Home).Bounds.Position.Y + 1f, g.Z)) != false;

    protected override void ServerThink(float dt)
    {
        if (State == MonsterState.Dispersed)
        {
            _disperseLeft -= dt;
            if (_disperseLeft <= 0) { Timer = 1f; SetState(MonsterState.Idle); }
            return;
        }
        GasTick(dt);
        base.ServerThink(dt);
    }

    void GasTick(float dt)
    {
        _gasCd -= dt;
        if (_gasCd > 0) return;
        _gasCd = MonsterTuning.WispGasInterval;
        var c = GlobalPosition;
        foreach (var p in Game.I.Players.Values)
        {
            if (!p.Alive || !p.Participant || Cave.InHall(p.Pos)) continue;
            var rel = p.Pos + new Vector3(0, 1f, 0) - c;
            if (Flat(rel).Length() > MonsterTuning.WispRadius || Mathf.Abs(rel.Y) > 3f) continue;
            float dealt = p.TakeDamage(MonsterTuning.WispGasDps * MonsterTuning.WispGasInterval, null, "gas");
            Util.Log($"MONSTER: gas victim={p.Name} dealt={dealt:0.##} hp={p.Health:0.#}");
        }
    }

    // Wisp never attacks; when it reaches a sighted miner it just hovers on them.
    protected override void ChaseTick(float dt)
    {
        if (Target != null && (!Target.Alive || Cave.InHall(Target.Pos))) { Target = null; VisibleNow = false; }
        var goal = VisibleNow && Target != null ? Target.Pos : LastSeen;
        MoveTo(goal, ChaseSpeed, dt, true, 0.8f);
        LostT += VisibleNow ? 0 : dt;
        if (LostT > 3f) { Timer = 1f; SetState(MonsterState.Return); }
    }

    protected override void Animate(float dt)
    {
        float t = Time.GetTicksMsec() / 1000f;
        float k = 1f;
        if (State == MonsterState.Dispersed)
        {
            float a = StateAge;
            k = a < 0.6f ? Mathf.Lerp(1f, 0.12f, a / 0.6f) : 0.12f;
        }
        else if (State == MonsterState.Idle && StateAge < 1.5f) k = Mathf.Lerp(0.12f, 1f, Mathf.Clamp(StateAge / 1.5f, 0f, 1f));
        for (int i = 0; i < _puffs.Length; i++)
        {
            float ph = t * (0.8f + i * 0.13f) + i;
            _puffs[i].Position = _puffBase[i] * k + new Vector3(Mathf.Sin(ph) * 0.25f, Mathf.Cos(ph * 1.3f) * 0.2f, Mathf.Cos(ph) * 0.25f) * k;
            _puffs[i].Scale = Vector3.One * k * (1f + 0.12f * Mathf.Sin(ph * 2f));
        }
        _eyes.Visible = State != MonsterState.Dispersed;
        _eyes.Rotation = new Vector3(0, 0, Mathf.Sin(t * 1.2f) * 0.1f);
        _light.LightEnergy = (0.7f + 0.25f * Mathf.Sin(t * 3f)) * k;
        Rig.Position = new Vector3(0, 0.1f * Mathf.Sin(t * 1.4f), 0);
    }
}
