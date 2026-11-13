using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>
/// The Plug: a bloated mass that exactly fills a one-wide tunnel (1.3 m x 2.2 m) and creeps along the tunnels toward the last noise. It cannot be passed (its body is solid to miners), it cannot be stunned for long
/// (dynamite shoves it back a few metres instead) and touching it crushes: 20 damage per second. It forces detours; a crawl or a squeeze it cannot enter is the way round.
/// </summary>
public partial class PlugObj : MonsterObj
{
    Node3D _mass;
    readonly List<MeshInstance3D> _pustules = new();
    OmniLight3D _glow;
    bool _hasGoal;
    float _crushTick, _idleT, _pushPause, _step;
    readonly List<Vector3> _trail = new();
    List<Vector3> _pushPath;
    MonsterState _prevState = MonsterState.Idle;

    public PlugObj()
    {
        WalkSpeed = InvestigateSpeed = ChaseSpeed = MonsterTuning.PlugSpeed; TurnRate = 1.6f; MoveAlign = 1.0f; HearMul = MonsterTuning.PlugHearMul;
        BodyRadius = 0.6f; EyeH = 1.2f; Profile = NavProfile.Tall; PatrolTunnelsOnly = true;
    }

    protected override void BuildModel()
    {
        var flesh = new Color(0.60f, 0.47f, 0.46f);
        var pale = new Color(0.72f, 0.60f, 0.56f);
        var vein = new Color(0.32f, 0.16f, 0.2f);
        _mass = Pivot(Rig, new Vector3(0, 1.1f, 0));
        // a lumpy slab that fills the tunnel: wide at the bottom, domed under the ceiling, rolls of flesh along the sides
        Blob(_mass, new Vector3(0.78f, 0.95f, 1.25f), flesh, new Vector3(0, 0.0f, 0));
        Blob(_mass, new Vector3(0.70f, 0.7f, 0.9f), pale, new Vector3(0.12f, 0.55f, -0.35f));
        Blob(_mass, new Vector3(0.62f, 0.6f, 0.8f), flesh, new Vector3(-0.15f, 0.62f, 0.4f));
        Blob(_mass, new Vector3(0.45f, 0.5f, 0.6f), pale, new Vector3(0.5f, -0.25f, 0.55f));
        Blob(_mass, new Vector3(0.45f, 0.55f, 0.65f), pale, new Vector3(-0.52f, -0.2f, -0.5f));
        Blob(_mass, new Vector3(0.4f, 0.45f, 0.5f), flesh, new Vector3(0.55f, 0.3f, -0.5f));
        Blob(_mass, new Vector3(0.4f, 0.5f, 0.55f), flesh, new Vector3(-0.55f, 0.25f, 0.5f));
        Blob(_mass, new Vector3(0.5f, 0.4f, 0.45f), vein, new Vector3(0.0f, -0.62f, 0.2f));
        Blob(_mass, new Vector3(0.3f, 0.3f, 0.3f), vein, new Vector3(0.28f, 0.2f, -1.02f));
        Blob(_mass, new Vector3(0.28f, 0.28f, 0.28f), vein, new Vector3(-0.3f, -0.05f, 1.05f));
        // dim glowing pustules: the only thing you see of it in the dark
        var glowC = new Color(1f, 0.62f, 0.25f);
        foreach (var (x, y, z, r) in new[] { (0.35f, 0.75f, -0.25f, 0.11f), (-0.4f, 0.5f, 0.6f, 0.09f), (0.1f, 0.2f, -1.15f, 0.1f), (-0.55f, 0.05f, -0.2f, 0.08f), (0.55f, -0.1f, 0.5f, 0.09f), (0.0f, 0.95f, 0.1f, 0.1f) })
        {
            var m = Util.SphereMesh(r, glowC, 1.6f); m.Position = new Vector3(x, y, z); _mass.AddChild(m); _pustules.Add(m);
        }
        _glow = new OmniLight3D { Position = new Vector3(0, 1.0f, -0.4f), LightColor = new Color(1f, 0.55f, 0.25f), LightEnergy = 0.35f, OmniRange = 2.5f, ShadowEnabled = false };
        Rig.AddChild(_glow);
    }

    static void Blob(Node3D parent, Vector3 radii, Color c, Vector3 pos)
    {
        var m = new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 9, Rings = 5 }, MaterialOverride = Util.Mat(c) };
        m.Scale = radii; m.Position = pos;
        parent.AddChild(m);
    }

    protected override void BuildBody()
    {
        // solid to miners too (layer World): the tunnel is blocked
        CollisionLayer = Layers.Monsters | Layers.Solid | Layers.World;
        AddBox(new Vector3(1.3f, 2.2f, 2.3f), new Vector3(0, 1.1f, 0));
    }

    // ------------------------------------------------------------------ server
    public override void Stun(float seconds) => base.Stun(Mathf.Min(seconds, MonsterTuning.PlugMaxStun));

    public override void OnNoise(Vector3 pos, float loud, ServerPlayer by)
    {
        if (!IsSrv || Frozen || State == MonsterState.Stunned) return;
        if (GlobalPosition.DistanceTo(pos) > loud * HearMul || Cave.InHall(pos)) return;
        SetGoal(pos);
    }

    public override void Dispatch(Vector3 area) { if (IsSrv && !Frozen) SetGoal(area); }

    void SetGoal(Vector3 pos)
    {
        Goal = SnapGoal(pos); _hasGoal = true; StuckT = 0; _path = null;
        if (State == MonsterState.Idle) SetState(MonsterState.Wander);
    }

    /// <summary>Dynamite: pushed back a few metres along its own trail (never stunned for long).</summary>
    public override void OnExplosion(Vector3 center, float radius, float damage)
    {
        if (!IsSrv) return;
        if (GlobalPosition.DistanceTo(center) > radius + 5f) return;
        float want = MonsterTuning.PlugPushback, acc = 0f;
        var pos = GlobalPosition; var path = new List<Vector3>();
        for (int i = _trail.Count - 1; i >= 0 && acc < want; i--) { acc += pos.DistanceTo(_trail[i]); pos = _trail[i]; path.Add(pos); }
        if (path.Count == 0)
        {
            // no trail yet: slide to the node (within a few metres) farthest from the blast
            int s = Nav.Nearest(Profile, GlobalPosition);
            if (s >= 0)
            {
                var d = Nav.Distances(Profile, s); int best = -1; float bd = -1;
                for (int k = 0; k < d.Length; k++)
                    if (d[k] > 2f && d[k] < 7f && Nav.Pos[k].DistanceTo(center) > bd) { bd = Nav.Pos[k].DistanceTo(center); best = k; }
                if (best >= 0) { var p2 = Nav.FindPath(Profile, GlobalPosition, Nav.Pos[best]); if (p2 != null) path = p2; }
            }
        }
        if (path.Count == 0) return;
        _pushPath = path; _hasGoal = false; _pushPause = 3f;
        Util.Pass("plug_pushed_back", $"id={Id}");
    }

    protected override void ServerThink(float dt)
    {
        if (_pushPath != null)
        {
            // pushed: slides back along its trail
            var tgt = _pushPath[0];
            var np = GlobalPosition.MoveToward(tgt, MonsterTuning.PlugPushSpeed * dt);
            GlobalPosition = np;
            if (np.DistanceTo(tgt) < 0.05f) { _pushPath.RemoveAt(0); if (_pushPath.Count == 0) { _pushPath = null; _trail.Clear(); } }
            return;
        }
        if (State == MonsterState.Stunned) { StunT -= dt; if (StunT <= 0) SetState(MonsterState.Idle); return; }
        if (Frozen) return;
        if (_pushPause > 0) { _pushPause -= dt; return; }
        Crush(dt);
        if (_hasGoal)
        {
            if (State == MonsterState.Idle) SetState(MonsterState.Wander);
            bool arrived = MoveTo(Goal, MonsterTuning.PlugSpeed, dt, false, 1.5f);
            Trail();
            if (arrived || StuckT > 5f) { _hasGoal = false; StuckT = 0; _idleT = 4f + GD.Randf() * 6f; if (State != MonsterState.Attack) SetState(MonsterState.Idle); }
            return;
        }
        // nothing heard: after a rest it drifts to a random tunnel
        if (State == MonsterState.Wander) SetState(MonsterState.Idle);
        _idleT -= dt;
        if (_idleT <= 0)
        {
            var g = Nav.RandomGoal(Profile, GlobalPosition, 20f, 160f, true);
            if (g != null) { Goal = g.Value; _hasGoal = true; _path = null; }
            _idleT = 6f;
        }
    }

    void Trail()
    {
        if (_trail.Count == 0 || _trail[_trail.Count - 1].DistanceTo(GlobalPosition) > 0.5f)
        {
            _trail.Add(GlobalPosition);
            if (_trail.Count > 40) _trail.RemoveAt(0);
        }
    }

    /// <summary>Touching it crushes: 20 damage per second to every miner against it.</summary>
    void Crush(float dt)
    {
        _crushTick -= dt;
        bool any = false;
        foreach (var p in Game.I.Players.Values)
        {
            if (!p.Alive || !p.Participant || Cave.InHall(p.Pos)) continue;
            var rel = p.Pos - GlobalPosition;
            if (Mathf.Abs(rel.Y) > 2f) continue;
            var local = rel.Rotated(Vector3.Up, -Yaw);
            if (Mathf.Abs(local.X) > 0.65f + 0.5f || Mathf.Abs(local.Z) > 1.15f + 0.5f) continue;
            any = true;
            if (_crushTick <= 0)
            {
                float dealt = p.TakeDamage(MonsterTuning.PlugCrushDps * 0.25f, null, "crush");
                Util.Pass("monster_hit_miner", $"kind=plug victim={p.Name} dmg={dealt:0.#} hp={p.Health:0.#}");
            }
        }
        if (_crushTick <= 0) _crushTick = 0.25f;
        if (any && State != MonsterState.Attack) SetState(MonsterState.Attack);
        else if (!any && State == MonsterState.Attack) SetState(_hasGoal ? MonsterState.Wander : MonsterState.Idle);
    }

    // ------------------------------------------------------------------ client
    protected override void OnLeave() { if (Sfx.IsLooping("plug" + Id)) Sfx.StopLoop("plug" + Id); }

    protected override void Animate(float dt)
    {
        float t = Time.GetTicksMsec() / 1000f;
        bool moving = AnimSpeed > 0.15f;
        _step += AnimSpeed * dt * 3f;
        float breathe = Mathf.Sin(t * 1.3f) * 0.025f;
        float squash = moving ? Mathf.Sin(_step * 2.2f) * 0.07f : 0f;
        _mass.Scale = new Vector3(1f - squash * 0.5f + breathe, 1f + breathe * 0.8f - squash * 0.3f, 1f + squash + (State == MonsterState.Attack ? 0.05f * Mathf.Sin(t * 22f) : 0f));
        for (int i = 0; i < _pustules.Count; i++) _pustules[i].Scale = Vector3.One * (1f + 0.25f * Mathf.Sin(t * 2f + i * 1.7f));
        _glow.LightEnergy = 0.3f + 0.12f * Mathf.Sin(t * 1.1f);
        if (State != _prevState)
        {
            if (State == MonsterState.Attack) Sfx.Play(SfxNames.PlugCrush, GlobalPosition + Vector3.Up);
            _prevState = State;
        }
        string key = "plug" + Id;
        if (moving || State == MonsterState.Attack) { if (!Sfx.IsLooping(key)) Sfx.Loop(key, SfxNames.PlugCreep, this); }
        else if (Sfx.IsLooping(key)) Sfx.StopLoop(key);
    }
}
