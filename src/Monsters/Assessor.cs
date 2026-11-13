using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// How the Assessor reads the sack. The sack/tribute rules are being reworked elsewhere: replace these delegates (one place) when they change; the monster keeps working.
/// Default: a miner's tribute is the heavier of the fed total (<see cref="ServerPlayer.TributeWeight"/>) and the weight of the stones in the sack.
/// </summary>
public static class AssessorHooks
{
    /// <summary>The tribute weight the Assessor judges a miner by.</summary>
    public static Func<ServerPlayer, int> TributeOf = p =>
    {
        int sack = 0;
        foreach (var s in p.Inv.Sack) sack += Ores.WeightOf(s);
        return Math.Max(p.TributeWeight, sack);
    };

    /// <summary>Removes the miner's single highest-tribute stone (credited to nobody) and returns its id, or null when there is nothing to take.</summary>
    public static Func<ServerPlayer, string> TakeTopStone = p =>
    {
        int best = -1, bw = 0;
        for (int i = 0; i < p.Inv.Sack.Count; i++)
        {
            int w = Ores.WeightOf(p.Inv.Sack[i]);
            if (w > bw) { bw = w; best = i; }
        }
        if (best < 0) return null;
        var id = p.Inv.Sack[best];
        p.Inv.Sack.RemoveAt(best);
        p.Dirty = true;
        return id;
    };
}

/// <summary>
/// The Assessor: the Goblin's bailiff. Gaunt, long coat, a ledger. Each round it stalks the miner carrying the highest tribute (re-checked every 20 s, nothing on screen says who). It walks at miner walking speed and never runs.
/// On catching its target it pins them for 3 s and takes their single highest-tribute stone (credited to nobody), then retreats for 45 s. It appears from round 2 on.
/// </summary>
public partial class AssessorObj : MonsterObj
{
    Node3D _legL, _legR, _armL, _armR, _body, _head, _ledger;
    MeshInstance3D _lensL, _lensR;
    float _evalT, _retreatT, _pinT;
    ServerPlayer _victim;
    float _stepAcc;
    MonsterState _prevState = MonsterState.Idle;

    public AssessorObj()
    {
        WalkSpeed = InvestigateSpeed = ChaseSpeed = MonsterTuning.AssessorWalk; TurnRate = 3f; MoveAlign = 1.0f;
        BodyRadius = 0.35f; EyeH = 1.8f; HearMul = 0f; Profile = NavProfile.Person; AttackRange = MonsterTuning.AssessorCatch;
        IdleMin = 1f; IdleRand = 2f;
    }

    protected override void BuildModel()
    {
        var coat = new Color(0.20f, 0.08f, 0.09f);        // oxblood long coat
        var trim = new Color(0.42f, 0.36f, 0.14f);
        var skin = new Color(0.78f, 0.78f, 0.70f);
        var black = new Color(0.05f, 0.05f, 0.06f);
        _legL = Pivot(Rig, new Vector3(-0.1f, 0.8f, 0)); _legR = Pivot(Rig, new Vector3(0.1f, 0.8f, 0));
        foreach (var l in new[] { _legL, _legR })
        {
            Box(l, new Vector3(0.11f, 0.8f, 0.12f), black, new Vector3(0, -0.4f, 0));
            Box(l, new Vector3(0.13f, 0.08f, 0.27f), black, new Vector3(0, -0.78f, -0.06f));
        }
        _body = Pivot(Rig, new Vector3(0, 0.8f, 0));
        Box(_body, new Vector3(0.4f, 0.72f, 0.24f), coat, new Vector3(0, 0.46f, 0));                         // gaunt chest
        Box(_body, new Vector3(0.5f, 0.78f, 0.3f), coat.Darkened(0.12f), new Vector3(0, -0.18f, 0));         // long coat to the shins
        Box(_body, new Vector3(0.06f, 0.95f, 0.02f), trim, new Vector3(0, 0.1f, -0.155f));                   // button strip
        Box(_body, new Vector3(0.52f, 0.1f, 0.28f), coat.Lightened(0.05f), new Vector3(0, 0.84f, 0));        // sharp shoulders
        Box(_body, new Vector3(0.3f, 0.14f, 0.26f), coat.Lightened(0.08f), new Vector3(0, 0.92f, 0.02f));    // high collar
        _head = Pivot(_body, new Vector3(0, 1.0f, 0));
        Box(_head, new Vector3(0.17f, 0.24f, 0.19f), skin, new Vector3(0, 0.14f, 0));                        // skull face
        Box(_head, new Vector3(0.07f, 0.1f, 0.06f), skin, new Vector3(0, 0.1f, -0.12f));                      // nose
        Box(_head, new Vector3(0.14f, 0.04f, 0.02f), black, new Vector3(0, 0.02f, -0.1f));                    // thin mouth
        var lens = new Color(0.95f, 0.9f, 0.5f);
        _lensL = Box(_head, new Vector3(0.06f, 0.05f, 0.02f), lens, new Vector3(-0.045f, 0.18f, -0.105f), 1.2f);   // pince-nez
        _lensR = Box(_head, new Vector3(0.06f, 0.05f, 0.02f), lens, new Vector3(0.045f, 0.18f, -0.105f), 1.2f);
        Box(_head, new Vector3(0.28f, 0.03f, 0.28f), black, new Vector3(0, 0.3f, 0));                        // bowler brim
        Box(_head, new Vector3(0.18f, 0.16f, 0.18f), black, new Vector3(0, 0.38f, 0));                       // bowler crown
        _armL = Pivot(_body, new Vector3(-0.28f, 0.86f, 0));
        Box(_armL, new Vector3(0.08f, 0.62f, 0.09f), coat, new Vector3(0, -0.3f, 0));
        Box(_armL, new Vector3(0.07f, 0.5f, 0.08f), coat, new Vector3(0, -0.84f, 0));
        Box(_armL, new Vector3(0.08f, 0.14f, 0.1f), skin, new Vector3(0, -1.15f, 0));
        _armR = Pivot(_body, new Vector3(0.28f, 0.86f, 0));
        Box(_armR, new Vector3(0.08f, 0.55f, 0.09f), coat, new Vector3(0, -0.26f, 0));
        var fore = Pivot(_armR, new Vector3(0, -0.52f, 0)); fore.Rotation = new Vector3(-1.2f, 0, 0);
        Box(fore, new Vector3(0.07f, 0.5f, 0.08f), coat, new Vector3(0, -0.24f, 0));
        Box(fore, new Vector3(0.08f, 0.13f, 0.1f), skin, new Vector3(0, -0.52f, 0));
        // the ledger, held open in front of the chest
        _ledger = Pivot(fore, new Vector3(0, -0.55f, -0.06f));
        Box(_ledger, new Vector3(0.3f, 0.04f, 0.38f), new Color(0.12f, 0.1f, 0.08f), new Vector3(0, 0, 0));
        Box(_ledger, new Vector3(0.26f, 0.03f, 0.34f), new Color(0.78f, 0.74f, 0.6f), new Vector3(0, 0.03f, 0));
        Box(_ledger, new Vector3(0.02f, 0.05f, 0.34f), black, new Vector3(0, 0.03f, 0));
    }

    protected override void BuildBody() => AddBox(new Vector3(0.6f, 1.95f, 0.5f), new Vector3(0, 0.98f, 0));

    // ------------------------------------------------------------------ server
    public override void OnNoise(Vector3 pos, float loud, ServerPlayer by) { }
    public override void Dispatch(Vector3 area) { }

    /// <summary>The living, outside-the-Hall miner carrying the most tribute (nearest on a tie); null when nobody carries anything.</summary>
    ServerPlayer PickTarget()
    {
        ServerPlayer best = null; int bw = 0; float bd = float.MaxValue;
        foreach (var p in Game.I.Players.Values)
        {
            if (!p.Alive || !p.Participant || p.Downed || Cave.InHall(p.Pos)) continue;
            int w = AssessorHooks.TributeOf(p);
            if (w <= 0) continue;
            float d = p.Pos.DistanceTo(GlobalPosition);
            if (w > bw || (w == bw && d < bd)) { best = p; bw = w; bd = d; }
        }
        return best;
    }

    protected override void ServerThink(float dt)
    {
        if (State == MonsterState.Stunned) { StunT -= dt; if (StunT <= 0) { Timer = 1f; SetState(MonsterState.Idle); } return; }
        if (Frozen) return;
        switch (State)
        {
            case MonsterState.Attack:
                // pinning: the miner is rooted for the duration
                _pinT -= dt;
                if (_pinT <= 0) TakeStone();
                return;
            case MonsterState.Recover:
                _retreatT -= dt;
                if (MoveTo(Goal, WalkSpeed, dt, false, 1.5f) || StuckT > 3f)
                {
                    var g = Nav.RandomGoal(Profile, GlobalPosition, 30f, 150f);
                    if (g != null) { Goal = g.Value; _path = null; StuckT = 0; }
                }
                if (_retreatT <= 0) { _evalT = 0; SetState(MonsterState.Idle); }
                return;
        }
        _evalT -= dt;
        if (_evalT <= 0 || Target == null || !Target.Alive || Cave.InHall(Target.Pos))
        {
            var prev = Target;
            Target = PickTarget(); _evalT = Target == null ? 1.5f : MonsterTuning.AssessorReevaluate;
            if (Target != null && Target != prev) Util.Log($"MONSTER: assessor {Id} stalks {Target.Name}");   // server log only: nothing on screen reveals the target
        }
        if (Target != null && Target.Alive && !Cave.InHall(Target.Pos))
        {
            LastSeen = Target.Pos;
            float d = Flat(Target.Pos - GlobalPosition).Length();
            if (d <= MonsterTuning.AssessorCatch && Mathf.Abs(Target.Pos.Y - GlobalPosition.Y) < 2.2f) { BeginPin(Target); return; }
            if (State != MonsterState.Chase) SetState(MonsterState.Chase);   // "stalking" (never runs: same speed as walking)
            MoveTo(Target.Pos, MonsterTuning.AssessorWalk, dt, true, MonsterTuning.AssessorCatch * 0.6f);
            return;
        }
        // nobody to take from: wanders the cave
        Target = null;
        switch (State)
        {
            case MonsterState.Idle: Timer -= dt; if (Timer <= 0) StartPatrol(); break;
            case MonsterState.Wander:
                Timer -= dt;
                if (MoveTo(Goal, WalkSpeed, dt, false, 1.2f) || StuckT > 3f || Timer <= 0) GoIdle();
                break;
            default: GoIdle(); break;
        }
    }

    void BeginPin(ServerPlayer t)
    {
        _victim = t; _pinT = MonsterTuning.AssessorPin;
        Game.I.ServerRootPlayer(t, MonsterTuning.AssessorPin);
        Util.Pass("assessor_caught", $"victim={t.Name}");
        SetState(MonsterState.Attack);
    }

    void TakeStone()
    {
        var v = _victim; _victim = null;
        if (v != null && v.Alive)
        {
            var stone = AssessorHooks.TakeTopStone(v);
            if (stone != null)
            {
                Game.I.ServerNotice(v, "The Assessor strikes your heaviest stone from the ledger and takes it.");
                Util.Pass("assessor_took_stone", $"victim={v.Name} stone={stone}");
            }
            else Util.Pass("assessor_found_nothing", $"victim={v.Name}");
        }
        Target = null;
        var g = Nav.RandomGoal(Profile, GlobalPosition, 30f, 150f);
        Goal = g ?? GlobalPosition; _path = null; StuckT = 0; _retreatT = MonsterTuning.AssessorRetreat;
        SetState(MonsterState.Recover);
    }

    // ------------------------------------------------------------------ client
    protected override void Animate(float dt)
    {
        float t = Time.GetTicksMsec() / 1000f;
        float sw = Mathf.Clamp(AnimSpeed / 3f, 0f, 1.3f);
        AnimPhase += AnimSpeed * dt * 1.5f;
        float swing = Mathf.Sin(AnimPhase) * 0.45f * sw;
        _legL.Rotation = new Vector3(swing, 0, 0); _legR.Rotation = new Vector3(-swing, 0, 0);
        bool pin = State == MonsterState.Attack;
        _armL.Rotation = new Vector3(pin ? -1.4f : -swing * 0.7f, 0, 0.04f);
        _armR.Rotation = new Vector3(pin ? -0.4f : swing * 0.15f - 0.05f, 0, -0.04f);
        _body.Rotation = new Vector3(pin ? 0.3f : 0.12f + Mathf.Sin(t * 0.8f) * 0.01f, 0, 0);
        _head.Rotation = new Vector3(0.12f, pin ? 0f : Mathf.Sin(t * 0.55f) * 0.15f, 0);
        Rig.Position = new Vector3(0, Mathf.Abs(Mathf.Cos(AnimPhase)) * 0.025f * sw, 0);
        _ledger.Rotation = new Vector3(pin ? Mathf.Sin(t * 14f) * 0.08f : 0, 0, 0);   // scribbling
        if (State != _prevState)
        {
            if (State == MonsterState.Attack) Sfx.Play(SfxNames.AssessorStamp, GlobalPosition + Vector3.Up);
            _prevState = State;
        }
        if (AnimSpeed > 0.4f && State != MonsterState.Stunned)
        {
            _stepAcc += AnimSpeed * dt;
            if (_stepAcc >= 1.9f) { _stepAcc -= 1.9f; Sfx.Play(SfxNames.AssessorStep, GlobalPosition); }
        }
    }
}
