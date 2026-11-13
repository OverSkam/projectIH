using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>
/// Wicks: a swarm of small pale things (one network object, 10 bodies drawn on every peer) drawn to lit lanterns from far away. Fast, and the only monster that creeps through crawl spaces.
/// On contact it drinks the miner's lantern oil quickly and bites for 4 damage per second; a few seconds after the lantern goes out it loses interest.
/// </summary>
public partial class WicksObj : MonsterObj
{
    readonly List<Node3D> _bits = new();
    readonly List<Node3D> _wings = new();
    float _dmgAcc;
    MonsterState _prevState = MonsterState.Idle;

    public WicksObj()
    {
        WalkSpeed = 3.2f; InvestigateSpeed = 5f; ChaseSpeed = MonsterTuning.WicksSpeed; TurnRate = 9f; MoveAlign = 1.8f;
        BodyRadius = 0.3f; EyeH = 0.8f; HearMul = 0f; Profile = NavProfile.Swarm; AttackRange = MonsterTuning.WicksContact;
        IdleMin = 0.2f; IdleRand = 0.8f;
    }

    protected override void BuildModel()
    {
        var pale = new Color(0.74f, 0.72f, 0.62f);
        var rng = new RandomNumberGenerator { Seed = 4242 };
        for (int i = 0; i < MonsterTuning.WicksCount; i++)
        {
            var b = new Node3D();
            // a plump pale grub with a head, two flickering wings and a tail wick
            var body = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.06f, Height = 0.12f, RadialSegments = 6, Rings = 3 }, MaterialOverride = Util.Mat(pale, 1.1f) };
            body.Scale = new Vector3(1f, 0.9f, 1.8f);
            b.AddChild(body);
            var head = Util.SphereMesh(0.045f, pale, 1.1f); head.Position = new Vector3(0, 0.01f, -0.12f); b.AddChild(head);
            var eyeA = Util.BoxMesh(new Vector3(0.012f, 0.012f, 0.01f), new Color(0.05f, 0.04f, 0.04f)); eyeA.Position = new Vector3(-0.022f, 0.02f, -0.16f); b.AddChild(eyeA);
            var eyeB = Util.BoxMesh(new Vector3(0.012f, 0.012f, 0.01f), new Color(0.05f, 0.04f, 0.04f)); eyeB.Position = new Vector3(0.022f, 0.02f, -0.16f); b.AddChild(eyeB);
            var tail = Util.BoxMesh(new Vector3(0.012f, 0.012f, 0.1f), new Color(0.35f, 0.3f, 0.22f)); tail.Position = new Vector3(0, 0.01f, 0.2f); b.AddChild(tail);
            var wl = new Node3D { Position = new Vector3(-0.05f, 0.05f, 0) }; var wr = new Node3D { Position = new Vector3(0.05f, 0.05f, 0) };
            var wlm = Util.BoxMesh(new Vector3(0.2f, 0.006f, 0.11f), new Color(0.8f, 0.8f, 0.72f), 0.6f); wlm.Position = new Vector3(-0.1f, 0, 0);
            var wrm = Util.BoxMesh(new Vector3(0.2f, 0.006f, 0.11f), new Color(0.8f, 0.8f, 0.72f), 0.6f); wrm.Position = new Vector3(0.1f, 0, 0);
            wl.AddChild(wlm); wr.AddChild(wrm); b.AddChild(wl); b.AddChild(wr);
            _wings.Add(wl); _wings.Add(wr);
            Rig.AddChild(b);
            _bits.Add(b);
        }
    }

    OmniLight3D _glow;

    protected override void BuildBody()
    {
        AddBox(new Vector3(0.7f, 0.7f, 0.7f), new Vector3(0, 0.55f, 0));
        // the swarm glows faintly (pale), so it can be seen in the dark before it arrives
        _glow = new OmniLight3D { Position = new Vector3(0, 0.9f, 0), LightColor = new Color(0.82f, 0.86f, 0.7f), LightEnergy = 0.35f, OmniRange = 3.2f, OmniAttenuation = 1.3f, ShadowEnabled = false };
        Rig.AddChild(_glow);
    }

    // ------------------------------------------------------------------ server: drawn to a lit lantern, and to nothing else
    // The whole rule: a miner whose lantern has been lit for at least a second, within 22 m and at most 30 m of tunnel path, calls them; they fly to where the light is.
    // The moment it goes out they fly to the last lit spot, circle it for 2.5 s and drift off. A dark miner is never tracked, never "felt" by touch.
    Vector3 _lastLit;
    readonly System.Collections.Generic.Dictionary<int, (double at, bool ok)> _pathCache = new();

    protected override float SearchTime => MonsterTuning.WicksCircleSeconds;

    bool PathOk(ServerPlayer p)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_pathCache.TryGetValue(p.Peer, out var c) && now - c.at < 0.6) return c.ok;
        var path = Nav.FindPath(Profile, GlobalPosition, p.Pos);
        bool ok = false;
        if (path != null)
        {
            float len = Flat(path[0] - GlobalPosition).Length();
            for (int i = 1; i < path.Count; i++) len += Flat(path[i] - path[i - 1]).Length();
            ok = len <= MonsterTuning.WicksPath;
        }
        _pathCache[p.Peer] = (now, ok);
        return ok;
    }

    protected override bool CanSee(ServerPlayer p, out float dist)
    {
        dist = GlobalPosition.DistanceTo(p.Pos);
        if (!MonsterSenses.Lit(p)) return false;
        if (Target == p && IsHunting) return dist <= MonsterTuning.WicksSense + 8f;       // already hooked on the light: keeps it while it burns
        if (dist > MonsterTuning.WicksSense || MonsterSenses.LitFor(p) < MonsterTuning.WicksFlash) return false;
        return PathOk(p);
    }

    /// <summary>The light is gone (or out of reach): fly to where it was last lit, circle there, then wander on.</summary>
    void LoseLight()
    {
        Goal = _lastLit; Arrived = false; Timer = 0; StuckT = 0; _path = null;
        Target = null; VisibleNow = false;
        SetState(MonsterState.Investigate);
        Util.Log($"MONSTER: wicks {Id} lost the light");
    }

    protected override void ChaseTick(float dt)
    {
        if (Target == null || !Target.Alive || Cave.InHall(Target.Pos) || !MonsterSenses.Lit(Target) || Flat(Target.Pos - GlobalPosition).Length() > MonsterTuning.WicksSense + 8f) { LoseLight(); return; }
        _lastLit = LastSeen = Target.Pos;
        float d = Flat(Target.Pos - GlobalPosition).Length();
        if (d <= MonsterTuning.WicksContact && Mathf.Abs(Target.Pos.Y - GlobalPosition.Y) < 2.2f) { SetState(MonsterState.Attack); Feed(dt); return; }
        if (State == MonsterState.Attack) SetState(MonsterState.Chase);
        MoveTo(Target.Pos, ChaseSpeed, dt, true, 0.6f);
        if (StuckT > 6f) { Target = null; VisibleNow = false; StuckT = 0; StartPatrol(); }
    }

    protected override void AttackTick(float dt)
    {
        if (State == MonsterState.Windup || State == MonsterState.Recover) { SetState(MonsterState.Chase); return; }
        // latched on a miner: keeps pace with them for as long as the lantern burns
        if (Target == null || !Target.Alive || Cave.InHall(Target.Pos) || !MonsterSenses.Lit(Target)) { LoseLight(); return; }
        _lastLit = Target.Pos;
        float d = Flat(Target.Pos - GlobalPosition).Length();
        if (d > MonsterTuning.WicksContact * 1.6f) { SetState(MonsterState.Chase); return; }
        if (d > 0.5f) MoveTo(Target.Pos, ChaseSpeed, dt, true, 0.4f);
        Feed(dt);
    }

    protected override bool TryStartAttack(ServerPlayer t) => false;

    void Feed(float dt)
    {
        var t = Target;
        float oil0 = t.Oil;
        t.Oil = Mathf.Max(0f, t.Oil - MonsterTuning.WicksOilPerSec * dt);
        if (t.Oil < oil0) t.Dirty = true;
        _dmgAcc += MonsterTuning.WicksDps * dt;
        if (_dmgAcc >= 1f)
        {
            float amount = Mathf.Floor(_dmgAcc); _dmgAcc -= amount;
            float dealt = t.TakeDamage(amount, null, "monster");
            Util.Pass("monster_hit_miner", $"kind=wicks victim={t.Name} dmg={dealt:0.#} hp={t.Health:0.#} oil={t.Oil:0}");
        }
    }

    // ------------------------------------------------------------------ client: the swarm
    protected override void OnLeave() { if (Sfx.IsLooping("wicks" + Id)) Sfx.StopLoop("wicks" + Id); }

    protected override void Animate(float dt)
    {
        float t = Time.GetTicksMsec() / 1000f;
        bool latched = State == MonsterState.Attack;
        bool circling = State == MonsterState.Investigate && AnimSpeed < 0.6f;   // lost the light: orbits the last lit spot
        float speed = Mathf.Clamp(AnimSpeed, 0f, 8f);
        for (int i = 0; i < _bits.Count; i++)
        {
            float ph = i * 2.399f;                                  // golden angle spread
            float w = (0.9f + 0.5f * Mathf.Sin(i * 1.3f)) * (latched ? 2.6f : circling ? 2.0f : 1.4f);
            float a = t * w + ph;
            float r = (latched ? 0.3f : circling ? 1.5f : 0.5f) + 0.22f * Mathf.Sin(t * 1.7f + ph * 3f);
            float h = 0.7f + 0.45f * Mathf.Sin(t * 1.1f + i * 0.9f) + (latched ? 0.2f : 0f);
            var p = new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
            // trail behind when moving fast
            p.Z += Mathf.Min(0.9f, speed * 0.12f) * (0.4f + 0.6f * Mathf.Sin(i * 0.77f));
            _bits[i].Position = p;
            var tan = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
            _bits[i].Rotation = new Vector3(0, Mathf.Atan2(-tan.X, -tan.Z), 0);
        }
        float flap = Mathf.Sin(t * 70f);
        for (int i = 0; i < _wings.Count; i++) _wings[i].Rotation = new Vector3(0, 0, ((i & 1) == 0 ? 1f : -1f) * (0.25f + 0.65f * Mathf.Sin(t * 60f + (i >> 1) * 1.7f)));
        if (State != _prevState)
        {
            if (State == MonsterState.Attack) Sfx.Play(SfxNames.WicksDrain, GlobalPosition + Vector3.Up);
            _prevState = State;
        }
        string key = "wicks" + Id;
        if (State != MonsterState.Stunned)
        {
            if (!Sfx.IsLooping(key)) Sfx.Loop(key, SfxNames.WicksChitter, this, 0f);
            // the chitter rises in pitch and loudness as they come closer
            var me = Game.I.LocalMiner as Node3D ?? Game.I.LocalGhost;
            float d = me != null ? GlobalPosition.DistanceTo(me.GlobalPosition) : 40f;
            float near = 1f - Mathf.Clamp(d / 24f, 0f, 1f);
            Sfx.TuneLoop(key, 0.8f + 0.75f * near * near + (latched ? 0.15f : 0f), -5f + 9f * near);
        }
        else if (Sfx.IsLooping(key)) Sfx.StopLoop(key);
        // while they drink: whichever miner they sit on has a guttering flame (everyone sees it)
        if (latched && Game.I.World != null)
            foreach (var m in Game.I.World.Miners())
                if (m.GlobalPosition.DistanceTo(GlobalPosition) < 2.4f) m.GutterUntil = Time.GetTicksMsec() / 1000.0 + 0.35;
    }
}
