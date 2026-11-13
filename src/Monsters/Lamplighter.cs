using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>
/// The Lamplighter: a tall thin figure carrying a light that is exactly a miner's lantern (the same model, colour, energy and range: from a distance you cannot tell it from another miner).
/// It wanders slowly and stands still in the tunnels. A miner who comes within 3 m, or who hits it, gets the light pinched out and is grabbed and held for 2 s (40 damage); then it walks away and relights.
/// </summary>
public partial class LamplighterObj : MonsterObj
{
    Node3D _hand, _armR, _armL, _body, _head, _legL, _legR;
    OmniLight3D _light;
    MeshInstance3D _flame;
    ServerPlayer _victim;
    float _holdTick, _awayT;
    bool _standing;
    MonsterState _prevState = MonsterState.Idle;
    float _lightBase;
    readonly Dictionary<int, double> _cool = new();   // a miner it has just held is left alone for a while
    static double Clk => Time.GetTicksMsec() / 1000.0;

    public LamplighterObj()
    {
        WalkSpeed = MonsterTuning.LampWalk; InvestigateSpeed = MonsterTuning.LampWalk; ChaseSpeed = 6.5f; TurnRate = 2.5f; MoveAlign = 0.9f;
        BodyRadius = 0.35f; EyeH = 1.8f; HearMul = 0f; Profile = new NavProfile { MinCeil = MonsterTuning.LampHeadroom, Squeeze = true };
        IdleMin = MonsterTuning.LampStand; IdleRand = 8f; PatrolTunnelsOnly = false;
    }

    protected override void BuildModel()
    {
        var coat = new Color(0.10f, 0.095f, 0.11f);
        var skin = new Color(0.42f, 0.40f, 0.36f);
        _legL = Pivot(Rig, new Vector3(-0.1f, 0.95f, 0)); _legR = Pivot(Rig, new Vector3(0.1f, 0.95f, 0));
        foreach (var l in new[] { _legL, _legR })
        {
            Box(l, new Vector3(0.09f, 0.95f, 0.1f), coat, new Vector3(0, -0.47f, 0));
            Box(l, new Vector3(0.11f, 0.07f, 0.24f), new Color(0.04f, 0.04f, 0.04f), new Vector3(0, -0.93f, -0.05f));
        }
        _body = Pivot(Rig, new Vector3(0, 0.95f, 0));
        Box(_body, new Vector3(0.3f, 0.78f, 0.17f), coat, new Vector3(0, 0.4f, 0));                    // narrow torso
        Box(_body, new Vector3(0.38f, 0.55f, 0.2f), coat.Lightened(0.03f), new Vector3(0, -0.12f, 0));  // long coat skirts
        Box(_body, new Vector3(0.42f, 0.09f, 0.2f), coat.Lightened(0.06f), new Vector3(0, 0.76f, 0));   // narrow high shoulders
        _head = Pivot(_body, new Vector3(0, 0.95f, 0));
        Box(_head, new Vector3(0.1f, 0.18f, 0.1f), skin, new Vector3(0, 0.0f, 0));                       // neck
        Box(_head, new Vector3(0.17f, 0.3f, 0.17f), skin, new Vector3(0, 0.22f, 0));                     // long pale head
        Box(_head, new Vector3(0.2f, 0.05f, 0.2f), coat, new Vector3(0, 0.4f, 0));                       // flat brim
        Box(_head, new Vector3(0.13f, 0.1f, 0.13f), coat, new Vector3(0, 0.46f, 0));
        // arms: left hangs; right is bent and carries the lantern at miner height
        _armL = Pivot(_body, new Vector3(-0.23f, 0.74f, 0));
        Box(_armL, new Vector3(0.07f, 0.62f, 0.08f), coat, new Vector3(0, -0.3f, 0));
        Box(_armL, new Vector3(0.07f, 0.55f, 0.07f), coat, new Vector3(0, -0.88f, 0));
        Box(_armL, new Vector3(0.08f, 0.14f, 0.1f), skin, new Vector3(0, -1.22f, 0));
        _armR = Pivot(_body, new Vector3(0.23f, 0.74f, 0));
        Box(_armR, new Vector3(0.07f, 0.55f, 0.08f), coat, new Vector3(0, -0.26f, 0));
        var fore = Pivot(_armR, new Vector3(0, -0.52f, 0)); fore.Rotation = new Vector3(-0.75f, 0, 0);
        Box(fore, new Vector3(0.07f, 0.5f, 0.07f), coat, new Vector3(0, -0.24f, 0));
        Box(fore, new Vector3(0.08f, 0.12f, 0.09f), skin, new Vector3(0, -0.52f, 0));
        // the lantern: the very same builder the miners use, held where a miner's right hand holds it (hand ~0.85 m up, 0.34 m out)
        _hand = Pivot(Rig, new Vector3(0.34f, 0.85f, 0.0f));
        _light = HeldVisuals.Lantern(_hand);
        _lightBase = _light.LightEnergy;
        foreach (var c in _hand.GetChildren())
            foreach (var cc in c.GetChildren())
                if (cc is MeshInstance3D mi && mi.MaterialOverride is StandardMaterial3D sm && sm.EmissionEnabled) _flame = mi;
    }

    protected override void BuildBody() => AddBox(new Vector3(0.6f, 2.05f, 0.6f), new Vector3(0, 1.02f, 0));

    // ------------------------------------------------------------------ server
    bool Lit => State == MonsterState.Idle || State == MonsterState.Wander || State == MonsterState.Investigate || State == MonsterState.Return || State == MonsterState.Stunned;

    public override void OnNoise(Vector3 pos, float loud, ServerPlayer by) { }
    public override void Dispatch(Vector3 area) { if (IsSrv && !Frozen && State == MonsterState.Idle) { Goal = SnapGoal(area); Timer = 0; StartWalkTo(Goal); } }

    void StartWalkTo(Vector3 g) { Goal = g; Timer = 150f; StuckT = 0; _path = null; SetState(MonsterState.Wander); }

    /// <summary>Hit by a pickaxe (or anything): it grabs the attacker.</summary>
    public override void ServerHit(ServerPlayer p, float mining, bool pickaxe)
    {
        if (!IsSrv || Frozen || State == MonsterState.Stunned || p == null || !p.Alive || Cave.InHall(p.Pos)) return;
        if (State == MonsterState.Windup || State == MonsterState.Attack || State == MonsterState.Recover) return;
        BeginGrab(p);
    }

    public void ResetCooldown() => _cool.Clear();

    void BeginGrab(ServerPlayer p)
    {
        _victim = p; Timer = MonsterTuning.LampSnuff; _cool[p.Peer] = Clk + MonsterTuning.LampCooldown;
        Util.Pass("lamplighter_triggered", $"victim={p.Name}");
        SetState(MonsterState.Windup);
    }

    protected override void ServerThink(float dt)
    {
        if (State == MonsterState.Stunned) { StunT -= dt; if (StunT <= 0) { Timer = 2f; SetState(MonsterState.Idle); } return; }
        if (Frozen) return;
        switch (State)
        {
            case MonsterState.Idle:
                Watch();
                Timer -= dt;
                if (Timer <= 0) StartPatrol();
                break;
            case MonsterState.Wander:
                Watch();
                Timer -= dt;
                if (State == MonsterState.Wander && (MoveTo(Goal, WalkSpeed, dt, false, 1.2f) || StuckT > 3f || Timer <= 0)) GoIdle();
                break;
            case MonsterState.Windup:
                // the light is pinched out; it lunges the last metres
                if (_victim == null || !_victim.Alive) { Release(); break; }
                {
                    var rel = Flat(_victim.Pos - GlobalPosition);
                    float want = Mathf.Atan2(-rel.X, -rel.Z);
                    Yaw = RotateToward(Yaw, want, 12f * dt);
                    if (rel.Length() > 0.9f) Step(GlobalPosition + Flat(rel).Normalized() * ChaseSpeed * dt, true, dt);
                }
                Timer -= dt;
                if (Timer <= 0) Grab();
                break;
            case MonsterState.Attack:
                if (_victim == null || !_victim.Alive) { Release(); break; }
                _holdTick -= dt; Timer -= dt;
                if (_holdTick <= 0)
                {
                    _holdTick = 0.5f;
                    float dealt = _victim.TakeDamage(MonsterTuning.LampDamage / (MonsterTuning.LampHold / 0.5f), null, "monster");
                    Util.Pass("monster_hit_miner", $"kind=lamplighter victim={_victim.Name} dmg={dealt:0.#} hp={_victim.Health:0.#}");
                }
                if (Timer <= 0) Release();
                break;
            case MonsterState.Recover:
                // walks away, light out, then relights
                _awayT -= dt;
                if (MoveTo(Goal, WalkSpeed * 2.2f, dt, false, 1.5f) || StuckT > 3f || _awayT <= 0) { Timer = MonsterTuning.LampStand; SetState(MonsterState.Idle); }
                break;
            default: StartPatrol(); break;
        }
    }

    /// <summary>A miner within 3 m (in sight) sets it off.</summary>
    void Watch()
    {
        foreach (var p in Game.I.Players.Values)
        {
            if (!p.Alive || !p.Participant || Cave.InHall(p.Pos)) continue;
            if (_cool.TryGetValue(p.Peer, out var cu) && cu > Clk) continue;
            var rel = p.Pos - GlobalPosition;
            if (Mathf.Abs(rel.Y) > 2.2f || Flat(rel).Length() > MonsterTuning.LampTrigger) continue;
            if (Ray(GlobalPosition + new Vector3(0, 1.4f, 0), p.Pos + new Vector3(0, 1.2f, 0))) continue;
            BeginGrab(p);
            return;
        }
    }

    void Grab()
    {
        Game.I.ServerRootPlayer(_victim, MonsterTuning.LampHold);
        _holdTick = 0.01f; Timer = MonsterTuning.LampHold;
        Util.Pass("lamplighter_grabbed", $"victim={_victim.Name}");
        SetState(MonsterState.Attack);
    }

    void Release()
    {
        _victim = null;
        var g = Nav.RandomGoal(Profile, GlobalPosition, 25f, 120f);
        Goal = g ?? GlobalPosition; _awayT = MonsterTuning.LampWalkAway; StuckT = 0; _path = null;
        SetState(MonsterState.Recover);
    }

    protected override void StartPatrol()
    {
        var g = Nav.RandomGoal(Profile, GlobalPosition, 15f, 120f);
        if (g == null) { Timer = 4f; return; }
        StartWalkTo(g.Value);
    }

    // ------------------------------------------------------------------ client
    protected override void Animate(float dt)
    {
        float t = Time.GetTicksMsec() / 1000f;
        float sw = Mathf.Clamp(AnimSpeed / 1.2f, 0f, 1.5f);
        AnimPhase += AnimSpeed * dt * 2.2f;
        float swing = Mathf.Sin(AnimPhase) * 0.35f * sw;
        _legL.Rotation = new Vector3(swing, 0, 0); _legR.Rotation = new Vector3(-swing, 0, 0);
        bool holding = State == MonsterState.Attack, windup = State == MonsterState.Windup;
        _armL.Rotation = new Vector3(holding ? -1.3f : windup ? -1.0f : -swing * 0.8f, 0, 0.05f);
        _armR.Rotation = new Vector3(holding || windup ? -1.0f : swing * 0.25f, 0, -0.05f);
        _body.Rotation = new Vector3(holding ? 0.35f : windup ? 0.25f : 0.04f + Mathf.Sin(t * 0.9f) * 0.015f, 0, 0);
        _head.Rotation = new Vector3(0.1f, State == MonsterState.Investigate ? Mathf.Sin(t * 1.5f) * 0.5f : Mathf.Sin(t * 0.4f) * 0.08f, 0);
        Rig.Position = new Vector3(0, Mathf.Abs(Mathf.Cos(AnimPhase)) * 0.03f * sw, 0);
        // the lantern: lit exactly like a miner's until it is snuffed
        bool lit = Lit;
        _light.Visible = lit;
        _light.LightEnergy = _lightBase;
        if (_flame != null) _flame.Visible = lit;
        if (State != _prevState)
        {
            if (State == MonsterState.Windup) Sfx.Play(SfxNames.LampSnuff, _hand.GlobalPosition);
            else if (State == MonsterState.Attack) Sfx.Play(SfxNames.LampGrab, GlobalPosition + Vector3.Up);
            else if (State == MonsterState.Idle && _prevState == MonsterState.Recover) Sfx.Play(SfxNames.LanternOn, _hand.GlobalPosition);
            _prevState = State;
        }
    }
}
