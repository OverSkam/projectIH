using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace AppStein;

public enum MonsterState { Idle, Wander, Investigate, Chase, Windup, Attack, Recover, Return, Dispersed, Asleep, Awake, Stunned }

/// <summary>
/// Shared base of every monster. Monsters are UNKILLABLE: nothing a miner carries damages them; some things (golem back crystal, dynamite, bear traps) stun them briefly. AI runs on the server only (_PhysicsProcess); position replicates through
/// Game.ServerMonsterSync (unreliable, 10 Hz) and is interpolated on clients; state/health/extra travel in Data.
/// Visuals are built in code by BuildModel() and animated procedurally by Animate() on every peer.
/// </summary>
public abstract partial class MonsterObj : NetObject
{
    public static readonly List<MonsterObj> Live = new(); // server-side instances
    static readonly HashSet<string> _seen = new();

    public MonsterState State = MonsterState.Idle;
    public int Extra;                       // subclass-defined small int carried in Data (dragon stage)
    public float Yaw;
    /// <summary>Debug/test: stops all AI and movement.</summary>
    public bool Frozen;
    /// <summary>Test: the highest horizontal speed seen in one server physics tick (teleports over 30 m/s ignored).</summary>
    public float MaxStepSpeed;
    /// <summary>Debug/test: never sees anyone (still hears).</summary>
    public bool Blind;

    // ---- tuning set by subclasses
    protected float WalkSpeed = 1.5f, InvestigateSpeed = 2f, ChaseSpeed = 3f, TurnRate = 3f, MoveAlign = 0.6f;
    protected float SightDark = MonsterTuning.SightDark, SightLantern = MonsterTuning.SightLantern, HearMul = 1f;
    /// <summary>Detection radius against a miner WITHOUT a lit lantern: standing still / walking / sprinting.</summary>
    protected float DetectStill = MonsterTuning.DetectStill, DetectWalk = MonsterTuning.DetectWalk, DetectSprint = MonsterTuning.DetectSprint;
    protected float IdleMin = 0.5f, IdleRand = 1.5f;
    protected bool PatrolTunnelsOnly;
    /// <summary>Where this monster may walk (ceiling, crawls, squeezes): its collision must fit there.</summary>
    protected NavProfile Profile = NavProfile.Person;
    protected float SightCos = -1f;         // -1 = full circle
    protected float AttackRange = 2f, AttackAlign = 0.3f, BodyRadius = 0.6f, EyeH = 1.5f, WanderRadius = 10f;
    protected bool Flying;
    protected float Hover = 0f;

    protected Node3D Rig;
    double _stateStart;
    static double Now => Time.GetTicksMsec() / 1000.0;
    public float StateAge => (float)(Now - _stateStart);
    protected static bool IsSrv => Net.I != null && Net.I.IsServer;
    public Vector3 Forward => new Vector3(-Mathf.Sin(Yaw), 0, -Mathf.Cos(Yaw));
    public Vector3 Home;
    protected CaveInfo Cave => Game.I.World.Cave;

    // ---- server state
    protected ServerPlayer Target;
    protected Vector3 LastSeen, Goal;
    protected bool VisibleNow, Arrived;
    protected float LostT, Timer, StuckT, StunT;
    float _senseT, _syncT, _pathT, _dbgT, _groundTop = 1.2f;
    protected List<Vector3> _path;
    Vector3 _pathGoal;
    bool _hadData;

    // ---- client state
    Vector3 _netPos; float _netYaw; bool _netInit;
    Vector3 _lastPos; protected float AnimSpeed, AnimPhase;
    protected float Flash;
    Vector3 _firstPos; bool _movedReported;
    protected int Stage => Extra;

    protected abstract void BuildModel();
    protected abstract void BuildBody();
    /// <summary>Per-frame procedural animation (all peers).</summary>
    protected abstract void Animate(float dt);

    public static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    public static string MakeData(MonsterState st, int extra = 0) => $"st={(int)st};x={extra}";

    // ================================================================= NetObject
    public override void Build()
    {
        CollisionLayer = Layers.Monsters | Layers.Solid;
        CollisionMask = 0;
        Rig = new Node3D { Name = "Rig" };
        AddChild(Rig);
        BuildModel();
        BuildBody();
        _netPos = GlobalPosition; _lastPos = _netPos; _firstPos = _netPos;
        if (IsSrv)
        {
            Home = GlobalPosition;
            Live.Add(this);
            ServerInit();
        }
        else Util.Pass("monster_replicated", $"kind={Kind} id={Id}");
    }

    public override void _ExitTree() { Live.Remove(this); OnLeave(); }
    /// <summary>Client/server: the node leaves the tree (stop loops).</summary>
    protected virtual void OnLeave() { }

    public override void ApplyData(string data)
    {
        base.ApplyData(data);
        var kv = Kv(data);
        var old = State;
        if (kv.TryGetValue("st", out var s) && int.TryParse(s, out var si)) State = (MonsterState)si;
        if (kv.TryGetValue("x", out var x)) int.TryParse(x, out Extra);
        if (!_hadData || old != State) _stateStart = Now;
        if (_hadData && State == MonsterState.Stunned && old != MonsterState.Stunned) Flash = 0.25f;
        if (!IsSrv && _hadData && old != State && _seen.Add(Kind + ":" + State)) Util.Pass("monster_state_seen", $"kind={Kind} st={State}");
        _hadData = true;
    }

    public void SetNetState(Vector3 pos, float yaw)
    {
        _netPos = pos; _netYaw = yaw; _netInit = true;
    }

    protected void Broadcast() => Game.I.ServerSetObjectData(Id, MakeData(State, Extra));

    protected void SetState(MonsterState s)
    {
        if (State == s) return;
        State = s; _stateStart = Now;
        Broadcast();
    }

    // ================================================================= physics tick
    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (!IsInsideTree()) return;
        if (IsSrv)
        {
            var before = GlobalPosition;
            ServerThink(dt);
            if (dt > 0.0001f) { float sp = Flat(GlobalPosition - before).Length() / dt; if (sp < 30f && sp > MaxStepSpeed) MaxStepSpeed = sp; }
            if (Util.HasArg("mondebug") && (_dbgT -= dt) <= 0) { _dbgT = 1f; Util.Log($"MONDEBUG {Kind}#{Id} st={State} pos={GlobalPosition} yaw={Yaw:0.00} stuck={StuckT:0.0} goal={Goal}"); }
            Rotation = new Vector3(0, Yaw, 0);
            _syncT -= dt;
            if (_syncT <= 0) { _syncT = MonsterTuning.SyncInterval; Game.I.ServerMonsterSync(Id, GlobalPosition, Yaw); }
        }
        else if (_netInit)
        {
            GlobalPosition = GlobalPosition.Lerp(_netPos, Mathf.Min(1f, dt * 12f));
            Yaw = Mathf.LerpAngle(Yaw, _netYaw, Mathf.Min(1f, dt * 12f));
            Rotation = new Vector3(0, Yaw, 0);
            if (!_movedReported && GlobalPosition.DistanceTo(_firstPos) > 2f)
            {
                _movedReported = true;
                Util.Pass("monster_moved_on_client", $"kind={Kind} d={GlobalPosition.DistanceTo(_firstPos):0.0}");
            }
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (dt <= 0 || Rig == null) return;
        var p = GlobalPosition;
        float sp = new Vector3(p.X - _lastPos.X, 0, p.Z - _lastPos.Z).Length() / dt;
        _lastPos = p;
        AnimSpeed = Mathf.Lerp(AnimSpeed, Mathf.Min(sp, 12f), Mathf.Min(1f, dt * 8f));
        if (Flash > 0) Flash -= dt;
        Animate(dt);
    }

    // ================================================================= server AI
    protected virtual void ServerInit() { }
    protected MonsterNav Nav => MonsterNav.For(Cave);
    /// <summary>Test hook: stand still (Idle, senses on) for this long before patrolling again.</summary>
    public void TestHold(float seconds) { Timer = seconds; Target = null; VisibleNow = false; StuckT = 0; if (State == MonsterState.Stunned) return; if (State != MonsterState.Idle) { State = MonsterState.Wander; SetState(MonsterState.Idle); } }
    public ServerPlayer Stalking => Target;
    public Vector3 GoalPos => Goal;
    /// <summary>True while it is out patrolling / investigating (not busy with a target, an attack or a stun).</summary>
    public bool IsRoaming => State == MonsterState.Idle || State == MonsterState.Wander || State == MonsterState.Investigate || State == MonsterState.Return;
    public bool IsHunting => State == MonsterState.Chase || State == MonsterState.Windup || State == MonsterState.Attack || State == MonsterState.Recover;

    /// <summary>Melee never hurts a monster. Subclasses may react (the golem is stunned by a hit on its back crystal).</summary>
    public override void ServerHit(ServerPlayer p, float mining, bool pickaxe) { }

    /// <summary>Server: the monster stops and staggers for <paramref name="seconds"/> (golem crystal, dynamite, bear trap). Never damage.</summary>
    public virtual void Stun(float seconds)
    {
        if (!IsSrv || State == MonsterState.Stunned) return;
        StunT = seconds; Target = null; VisibleNow = false;
        SetState(MonsterState.Stunned);
        Util.Pass("monster_stunned", $"kind={Kind} id={Id} secs={seconds:0.#}");
    }

    protected virtual void ServerThink(float dt)
    {
        if (State == MonsterState.Stunned)
        {
            StunT -= dt;
            if (StunT <= 0) { Timer = 1f; StuckT = 0; SetState(MonsterState.Idle); }
            return;
        }
        if (Frozen) return;
        Sense(dt);
        switch (State)
        {
            case MonsterState.Idle:
                Timer -= dt;
                if (Timer <= 0) StartPatrol();
                break;
            case MonsterState.Wander:
                Timer -= dt;
                if (MoveTo(Goal, WalkSpeed, dt, false, 1.2f) || StuckT > 3f || Timer <= 0) GoIdle();
                break;
            case MonsterState.Investigate:
                if (!Arrived)
                {
                    if (StuckT > 3f) StartPatrol();   // cannot get there (a crawl, a vein, rubble): give up at once
                    else if (MoveTo(Goal, InvestigateSpeed, dt, false, 1.5f)) { Arrived = true; Timer = MonsterTuning.SearchSeconds; }
                }
                else
                {
                    Yaw += 0.9f * dt; // look around
                    Timer -= dt;
                    if (Timer <= 0) StartPatrol();
                }
                break;
            case MonsterState.Chase: ChaseTick(dt); break;
            case MonsterState.Windup:
            case MonsterState.Attack:
            case MonsterState.Recover: AttackTick(dt); break;
            case MonsterState.Return: StartPatrol(); break;
        }
    }

    protected void GoIdle() { Timer = IdleMin + GD.Randf() * IdleRand; StuckT = 0; SetState(MonsterState.Idle); }

    /// <summary>Walk to a random far node of the cave (monsters roam the whole nav graph, not around a home).</summary>
    protected virtual void StartPatrol()
    {
        Target = null; VisibleNow = false;
        var g = Nav.RandomGoal(Profile, GlobalPosition, MonsterTuning.PatrolMinPath, 220f, PatrolTunnelsOnly);
        if (g == null) { Timer = 2f; if (State != MonsterState.Idle) SetState(MonsterState.Idle); return; }
        Goal = g.Value; Timer = 150f; StuckT = 0; _path = null;
        SetState(MonsterState.Wander);
    }

    /// <summary>Server: head toward a rough area (director / test). Senses still take over on the way.</summary>
    public virtual void Dispatch(Vector3 area)
    {
        if (!IsSrv || Frozen || State == MonsterState.Stunned) return;
        Goal = SnapGoal(area); Arrived = false; Timer = 0; StuckT = 0; _path = null;
        Target = null; VisibleNow = false;
        if (State == MonsterState.Investigate) { Broadcast(); return; }
        State = MonsterState.Wander; SetState(MonsterState.Investigate);
    }

    /// <summary>A goal the monster can actually walk to (never inside the Hall buffer).</summary>
    protected Vector3 SnapGoal(Vector3 g)
    {
        int k = Nav.Nearest(Profile, g, 12f);
        return k >= 0 ? Nav.Pos[k] : g;
    }

    protected virtual bool WanderOk(Vector3 g)
    {
        if (HallBlock(g)) return false;
        return Flying || GroundAt(g) != null;
    }

    protected virtual void ChaseTick(float dt)
    {
        var pos = GlobalPosition;
        if (Target != null && (!Target.Alive || Cave.InHall(Target.Pos))) { Target = null; VisibleNow = false; }
        if (VisibleNow && Target != null)
        {
            float d = Flat(Target.Pos - pos).Length();
            if (d <= AttackRange)
            {
                float want = Mathf.Atan2(-(Target.Pos.X - pos.X), -(Target.Pos.Z - pos.Z));
                Yaw = RotateToward(Yaw, want, TurnRate * dt);
                if (Mathf.Abs(Wrap(want - Yaw)) <= AttackAlign && TryStartAttack(Target)) return;
                return;
            }
        }
        var goal = VisibleNow && Target != null ? Target.Pos : LastSeen;
        bool arrived = MoveTo(goal, ChaseSpeed, dt, true, 0.7f);
        LostT += VisibleNow ? 0 : dt;
        if (LostT > MonsterTuning.LoseSightSeconds || (arrived && !VisibleNow) || StuckT > 3f)
        {
            Goal = LastSeen; Arrived = false; Timer = 0; StuckT = 0;
            SetState(MonsterState.Investigate);
        }
    }

    protected virtual bool TryStartAttack(ServerPlayer t) => false;
    protected virtual void AttackTick(float dt) { SetState(MonsterState.Chase); }
    /// <summary>Wisp has no melee; subclasses may react when a new target is first sighted.</summary>
    protected virtual void OnSighted(ServerPlayer p) { }

    // ---------------------------------------------------------------- senses
    protected static bool HoldsLight(ServerPlayer p) => MonsterSenses.Lit(p);

    /// <summary>Sight: a lit lantern in line of sight is seen from far; without one a miner is noticed only close by (2 m if standing still, more when moving).</summary>
    protected virtual bool CanSee(ServerPlayer p, out float dist)
    {
        var pos = GlobalPosition;
        dist = pos.DistanceTo(p.Pos);
        float range;
        if (MonsterSenses.Lit(p)) range = SightLantern;
        else { float sp = MonsterSenses.Speed(p); range = sp < MonsterTuning.StillSpeed ? DetectStill : sp > MonsterTuning.SprintSpeedThreshold ? DetectSprint : DetectWalk; }
        if (dist > range) return false;
        if (SightCos > -0.99f && dist > 4f)
        {
            var d = Flat(p.Pos - pos).Normalized();
            if (d.Dot(Forward) < SightCos) return false;
        }
        return !Ray(pos + new Vector3(0, EyeH, 0), p.Pos + new Vector3(0, 1.2f, 0));
    }

    protected void Sense(float dt)
    {
        if (!VisibleNow && State == MonsterState.Chase) { }
        _senseT -= dt;
        if (_senseT > 0) return;
        _senseT = 0.2f;
        ServerPlayer best = null; float bd = float.MaxValue;
        if (!Blind)
            foreach (var p in Game.I.Players.Values)
            {
                if (!p.Alive || !p.Participant || Cave.InHall(p.Pos)) continue;
                if (CanSee(p, out var d) && d < bd) { best = p; bd = d; }
            }
        VisibleNow = best != null;
        if (best == null) return;
        Target = best; LastSeen = best.Pos; LostT = 0;
        if (State == MonsterState.Idle || State == MonsterState.Wander || State == MonsterState.Investigate || State == MonsterState.Return)
        {
            StuckT = 0; OnSighted(best);
            SetState(MonsterState.Chase);
        }
    }

    public virtual void OnNoise(Vector3 pos, float loud, ServerPlayer by)
    {
        if (!IsSrv || Frozen || State == MonsterState.Stunned || State == MonsterState.Dispersed) return;
        if (State == MonsterState.Chase || State == MonsterState.Windup || State == MonsterState.Attack || State == MonsterState.Recover) return;
        if (GlobalPosition.DistanceTo(pos) > loud * HearMul) return;
        if (Cave.InHall(pos)) return;
        Goal = SnapGoal(pos); Arrived = false; Timer = 0; StuckT = 0; _path = null;
        if (State == MonsterState.Investigate) return;
        SetState(MonsterState.Investigate);
        Util.Log($"MONSTER: heard kind={Kind} id={Id}");
    }

    // ---------------------------------------------------------------- movement
    protected static Vector3 Flat(Vector3 v) => new Vector3(v.X, 0, v.Z);
    protected static float Wrap(float a) => Mathf.PosMod(a + Mathf.Pi, Mathf.Tau) - Mathf.Pi;
    protected static float RotateToward(float from, float to, float step) => Wrap(from + Mathf.Clamp(Wrap(to - from), -step, step));
    public float StuckSeconds => StuckT;

    protected bool Ray(Vector3 a, Vector3 b, uint mask = Layers.World)
    {
        var q = PhysicsRayQueryParameters3D.Create(a, b, mask);
        q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        bool hit = GetWorld3D().DirectSpaceState.IntersectRay(q).Count > 0;
        q.Dispose();
        return hit;
    }

    protected float? GroundAt(Vector3 p)
    {
        var q = PhysicsRayQueryParameters3D.Create(p + new Vector3(0, _groundTop, 0), p + new Vector3(0, -2.2f, 0), Layers.World);
        q.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
        var r = GetWorld3D().DirectSpaceState.IntersectRay(q);
        q.Dispose();
        if (r.Count == 0) return null;
        return r["position"].AsVector3().Y;
    }

    protected bool HallBlock(Vector3 p)
    {
        var zone = Cave.HallZone.Grow(MonsterTuning.HallBuffer);
        return zone.HasPoint(new Vector3(p.X, Cave.HallZone.Position.Y + 1f, p.Z));
    }

    /// <summary>Kept for the Dragon (its lair needs 5 m of headroom); other monsters use <see cref="Profile"/>.</summary>
    protected float Headroom = 0f;

    /// <summary>May the monster stand at np? Never inside the Hall, never in rock, never through thin walls; ceiling / crawl / squeeze rules come from <see cref="Profile"/>.</summary>
    protected virtual bool Allowed(Vector3 np, bool chasing)
    {
        if (HallBlock(np) && !HallBlock(GlobalPosition)) return false;
        if (HallBlock(np) && HallBlock(GlobalPosition) && Flat(np - Cave.HallZone.GetCenter()).LengthSquared() < Flat(GlobalPosition - Cave.HallZone.GetCenter()).LengthSquared()) return false;
        if (ModularCave.Last is { } mc)
        {
            int i = (int)Math.Round(np.X / 2f), j = (int)Math.Round(np.Z / 2f);
            ref var c = ref mc.At(i, j);
            if (!c.Open || c.Thin) return false;
            if (c.Crawl ? !Profile.Crawl : c.Ceil < Mathf.Max(Profile.MinCeil, Headroom)) return false;
            if (!Profile.Squeeze && Cave.InSqueeze(np + new Vector3(0, 0.8f, 0))) return false;
        }
        return true;
    }

    bool ClearLine(Vector3 a, Vector3 b)
    {
        var d = Flat(b - a);
        if (d.LengthSquared() < 0.01f) return true;
        var side = new Vector3(-d.Z, 0, d.X).Normalized() * 0.3f;
        var up = new Vector3(0, 0.9f, 0);
        return !Ray(a + up, b + up) && !Ray(a + up + side, b + up + side) && !Ray(a + up - side, b + up - side);
    }

    bool FreeAhead(Vector3 pos, Vector3 dir)
    {
        float len = Mathf.Min(BodyRadius, 0.5f) + 0.55f;
        return !Ray(pos + new Vector3(0, 0.5f, 0), pos + new Vector3(0, 0.5f, 0) + dir * len)
            && !Ray(pos + new Vector3(0, 1.4f, 0), pos + new Vector3(0, 1.4f, 0) + dir * len);
    }

    /// <summary>Steps toward goal along the monster's own route (nav graph, profile-aware; a short direct run when the line is clear). True when within 'arrive' metres.</summary>
    protected bool MoveTo(Vector3 goal, float speed, float dt, bool chasing, float arrive = 0.9f)
    {
        var pos = GlobalPosition;
        float dist = Flat(goal - pos).Length();
        if (dist <= arrive) { StuckT = 0; return true; }
        _groundTop = Profile.Crawl ? 0.7f : 1.2f;
        Vector3 wp = goal;
        if (!(dist < 5f && ClearLine(pos, goal)))
        {
            _pathT -= dt;
            if (_path == null || _pathT <= 0f || Flat(_pathGoal - goal).Length() > 2.5f)
            {
                _path = Nav.FindPath(Profile, pos, goal) ?? Nav.FindPathNear(Profile, pos, goal); _pathGoal = goal; _pathT = 2.5f;
                if (_path == null) { StuckT += dt; return false; }     // no route for this body
                // FindPath starts at the nearest node, which may lie behind us: skip nodes we are already past.
                while (_path.Count > 1 && Flat(_path[1] - pos).Length() < Flat(_path[1] - _path[0]).Length()) _path.RemoveAt(0);
            }
            while (_path.Count > 1 && Flat(_path[0] - pos).Length() < 0.9f) _path.RemoveAt(0);
            if (_path.Count == 1 && Flat(_path[0] - pos).Length() < 0.9f)
            {
                if (dist < 3.5f || !chasing) { StuckT = 0; return true; }   // end of the route: close enough
                wp = goal;
            }
            else wp = _path[0];
        }
        else _path = null;
        var dir = Flat(wp - pos);
        if (dir.LengthSquared() < 0.0001f) return false;
        dir = dir.Normalized();
        float want = Mathf.Atan2(-dir.X, -dir.Z);
        if (!FreeAhead(pos, dir))
        {
            foreach (var a in new[] { 0.6f, -0.6f, 1.2f, -1.2f })
            {
                var alt = dir.Rotated(Vector3.Up, a);
                if (FreeAhead(pos, alt)) { want = Mathf.Atan2(-alt.X, -alt.Z); break; }
            }
        }
        Yaw = RotateToward(Yaw, want, TurnRate * dt);
        float diff = Mathf.Abs(Wrap(want - Yaw));
        if (diff > MoveAlign) return false;
        float scale = Mathf.Clamp(1.15f - diff / MoveAlign, 0.35f, 1f);
        if (!Step(pos + Forward * speed * scale * dt, chasing, dt)) StuckT += dt; else StuckT = 0;
        return false;
    }

    protected bool Step(Vector3 np, bool chasing, float dt)
    {
        var pos = GlobalPosition;
        if (!StepTo(np, chasing, dt))
        {
            // slide along walls: try each axis alone
            var d = np - pos;
            if (Mathf.Abs(d.X) > 0.0001f && StepTo(new Vector3(np.X, np.Y, pos.Z), chasing, dt)) return true;
            if (Mathf.Abs(d.Z) > 0.0001f && StepTo(new Vector3(pos.X, np.Y, np.Z), chasing, dt)) return true;
            return false;
        }
        return true;
    }

    bool StepTo(Vector3 np, bool chasing, float dt)
    {
        var pos = GlobalPosition;
        if (!Allowed(np, chasing)) return false;
        var gy = GroundAt(np);
        if (!Flying)
        {
            if (gy == null || Mathf.Abs(gy.Value - pos.Y) > 0.9f) return false;
            np.Y = Mathf.MoveToward(pos.Y, gy.Value, 6f * dt);
        }
        else
        {
            if (Ray(pos + new Vector3(0, 0.6f, 0), np + new Vector3(0, 0.6f, 0))) return false;   // flyers do not pass through rock either
            float target = (gy ?? pos.Y - Hover) + Hover;
            np.Y = Mathf.MoveToward(pos.Y, target, 1.5f * dt);
        }
        GlobalPosition = np;
        return true;
    }

    // ================================================================= helpers for models
    protected static Node3D Pivot(Node3D parent, Vector3 pos)
    {
        var n = new Node3D { Position = pos };
        parent.AddChild(n);
        return n;
    }

    protected static MeshInstance3D Box(Node3D parent, Vector3 size, Color c, Vector3 pos, float emission = 0f, Vector3? rot = null)
    {
        var m = Util.BoxMesh(size, c, emission);
        m.Position = pos;
        if (rot.HasValue) m.Rotation = rot.Value;
        parent.AddChild(m);
        return m;
    }

    protected void AddBox(Vector3 size, Vector3 pos)
    {
        AddChild(Util.BoxShape(size, pos));
    }
}
