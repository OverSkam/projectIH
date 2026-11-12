using System;
using Godot;

namespace AppStein;

/// <summary>
/// Dragon: sleeps on its hoard. A noise meter (fed by Game.NoiseMade) drives visible stages (Extra): 0 asleep, 1 stirring,
/// 2 eye open, 3 awake. When awake it breathes a telegraphed fire cone at miners in its lair (and a short way into the
/// adjoining tunnel), then settles back. Unkillable; never leaves its lair.
/// </summary>
public partial class DragonObj : MonsterObj
{
    public float Meter;
    public Aabb Lair;
    float _awakeT, _breathTick, _noTarget;
    int _announcedStage;
    Node3D _neck, _jaw, _flame;
    MeshInstance3D _eyeL, _eyeR, _mouthGlow, _flameOuter, _flameInner, _chest;
    OmniLight3D _fireLight;
    static StandardMaterial3D _fireMat, _fireMat2;
    float _neckAngle;

    public DragonObj()
    {
        TurnRate = MonsterTuning.DragonTurn; BodyRadius = 2f; EyeH = 2f; Headroom = 5f;
    }

    protected override void ServerInit()
    {
        var r = Cave.RoomAt(GlobalPosition);
        Lair = r != null ? r.Bounds : new Aabb(GlobalPosition - new Vector3(10, 1, 10), new Vector3(20, 8, 20));
        Meter = 0; Extra = 0; State = MonsterState.Asleep;
        Yaw = 0;
    }

    Vector3 Mouth => GlobalPosition + Forward * 3.6f + new Vector3(0, 1.9f, 0);
    bool InLair(Vector3 p, float grow = 0f) => Lair.Grow(grow).HasPoint(new Vector3(p.X, Lair.Position.Y + 1f, p.Z));

    protected override bool Allowed(Vector3 np, bool chasing) => InLair(np, -2.5f) && !HallBlock(np);


    // ------------------------------------------------------------------ noise meter
    public override void OnNoise(Vector3 pos, float loud, ServerPlayer by)
    {
        if (!IsSrv || Frozen) return;
        float d = GlobalPosition.DistanceTo(pos);
        if (d > loud) return;
        if (State != MonsterState.Asleep) { _awakeT = MonsterTuning.DragonAwakeSeconds; return; }
        Meter = Mathf.Min(MonsterTuning.DragonMeterMax, Meter + (loud - 0.7f * d) * MonsterTuning.DragonNoiseGain);
        Util.Log($"MONSTER: dragon meter={Meter:0.#}");
        UpdateStage();
    }

    void UpdateStage()
    {
        int st = Meter >= MonsterTuning.DragonMeterMax ? 3 : Meter >= MonsterTuning.DragonEye ? 2 : Meter >= MonsterTuning.DragonStir ? 1 : 0;
        if (st == 3) { Wake(); return; }
        if (st != Extra) { Extra = st; Broadcast(); }
        if (st > _announcedStage)
        {
            _announcedStage = st;
            if (st == 1) Game.I.ServerAnnounce("The ground trembles. Something huge breathes in the dark...");
            if (st == 2) Game.I.ServerAnnounce("A low growl rolls through the tunnels. Something has opened an eye.");
            Util.Pass("dragon_stage", $"stage={st}");
        }
    }

    void Wake()
    {
        Extra = 3; _awakeT = MonsterTuning.DragonAwakeSeconds; _noTarget = 0; _announcedStage = 3;
        SetStateForce(MonsterState.Awake);
        Game.I.ServerAnnounce("THE DRAGON WAKES!");
        Util.Pass("dragon_woke", $"meter={Meter:0.#}");
    }

    void SetStateForce(MonsterState s) { State = MonsterState.Asleep; SetState(s); }

    // ------------------------------------------------------------------ server tick
    protected override void ServerThink(float dt)
    {
        if (Frozen) return;
        var pos = GlobalPosition;
        switch (State)
        {
            case MonsterState.Asleep:
                if (Meter > 0)
                {
                    Meter = Mathf.Max(0, Meter - MonsterTuning.DragonDecay * dt);
                    int st = Meter >= MonsterTuning.DragonEye ? 2 : Meter >= MonsterTuning.DragonStir ? 1 : 0;
                    if (st != Extra) { Extra = st; _announcedStage = Math.Min(_announcedStage, st); Broadcast(); }
                }
                break;
            case MonsterState.Awake:
                {
                    _awakeT -= dt;
                    var t = BestTarget();
                    if (t == null)
                    {
                        _noTarget += dt;
                        if (_awakeT <= 0 || _noTarget > 6f) { Settle(); }
                        break;
                    }
                    _noTarget = 0;
                    var rel = Flat(t.Pos - pos);
                    float want = Mathf.Atan2(-rel.X, -rel.Z);
                    Yaw = RotateToward(Yaw, want, TurnRate * dt);
                    float ang = Mathf.Abs(Wrap(want - Yaw));
                    bool inRange = rel.Length() <= MonsterTuning.DragonBreathRange - 2f;
                    if (!inRange) Step(pos + Forward * MonsterTuning.DragonWalk * dt, false, dt); // shuffles closer, stays in its lair
                    else if (ang < 0.25f && !Ray(Mouth, t.Pos + new Vector3(0, 1.2f, 0))) { SetState(MonsterState.Windup); }
                    break;
                }
            case MonsterState.Windup:
                {
                    var t = BestTarget();
                    if (t != null)
                    {
                        var rel = Flat(t.Pos - pos);
                        Yaw = RotateToward(Yaw, Mathf.Atan2(-rel.X, -rel.Z), TurnRate * 0.4f * dt);
                    }
                    if (StateAge >= MonsterTuning.DragonWindup) { _breathTick = 0; SetState(MonsterState.Attack); }
                    break;
                }
            case MonsterState.Attack:
                _breathTick -= dt;
                if (_breathTick <= 0) { _breathTick = 0.2f; Breathe(0.2f); }
                if (StateAge >= MonsterTuning.DragonBreathTime) { _awakeT = MonsterTuning.DragonAwakeSeconds; SetState(MonsterState.Recover); }
                break;
            case MonsterState.Recover:
                if (StateAge >= MonsterTuning.DragonRecover) SetState(MonsterState.Awake);
                break;
        }
    }

    ServerPlayer BestTarget()
    {
        ServerPlayer best = null; float bd = float.MaxValue;
        foreach (var p in Game.I.Players.Values)
        {
            if (!p.Alive || !p.Participant || Cave.InHall(p.Pos)) continue;
            if (!InLair(p.Pos, MonsterTuning.DragonTunnelReach)) continue;
            float d = Flat(p.Pos - GlobalPosition).Length();
            if (d < bd) { bd = d; best = p; }
        }
        return best;
    }

    void Settle()
    {
        Meter = 0; Extra = 0; _announcedStage = 0; _noTarget = 0;
        SetState(MonsterState.Asleep);
        Broadcast();
        Game.I.ServerAnnounce("The dragon settles back onto its hoard.");
        Util.Pass("dragon_settled");
    }

    void Breathe(float dt)
    {
        var mouth = Mouth; var fwd = Forward;
        foreach (var p in Game.I.Players.Values)
        {
            if (!p.Alive || !p.Participant || Cave.InHall(p.Pos)) continue;
            var chest = p.Pos + new Vector3(0, 1.0f, 0);
            var rel = chest - mouth;
            float d = rel.Length();
            if (d > MonsterTuning.DragonBreathRange || d < 0.1f) continue;
            if (Mathf.Acos(Mathf.Clamp(fwd.Dot(rel / d), -1f, 1f)) > MonsterTuning.DragonBreathHalfAngle) continue;
            if (Ray(mouth, chest)) continue;
            float dealt = p.TakeDamage(MonsterTuning.DragonBreathDps * dt, null, "fire");
            MonsterModule.Ignite(p, MonsterTuning.DragonBreathBurn);
            Util.Pass("monster_hit_miner", $"kind=dragon victim={p.Name} dmg={dealt:0.#} hp={p.Health:0.#}");
        }
    }

    // ------------------------------------------------------------------ model
    static StandardMaterial3D FireMat(bool inner)
    {
        if (inner ? _fireMat2 != null : _fireMat != null) return inner ? _fireMat2 : _fireMat;
        var m = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = inner ? new Color(1f, 0.9f, 0.4f, 0.75f) : new Color(1f, 0.38f, 0.05f, 0.55f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        if (inner) _fireMat2 = m; else _fireMat = m;
        return m;
    }

    protected override void BuildModel()
    {
        var scale = new Color(0.30f, 0.12f, 0.10f);
        var scaleL = new Color(0.42f, 0.17f, 0.13f);
        var belly = new Color(0.55f, 0.38f, 0.2f);
        var bone = new Color(0.75f, 0.7f, 0.6f);
        // body (curled up, resting)
        _chest = Box(Rig, new Vector3(3.2f, 2.2f, 4.6f), scale, new Vector3(0, 1.4f, 1.0f));
        Box(Rig, new Vector3(2.6f, 0.9f, 4.0f), belly, new Vector3(0, 0.55f, 1.0f));
        Box(Rig, new Vector3(2.4f, 0.6f, 3.6f), scaleL, new Vector3(0, 2.6f, 1.0f));
        // back spikes
        for (int i = 0; i < 6; i++) Box(Rig, new Vector3(0.3f, 0.7f, 0.3f), bone, new Vector3(0, 3.1f, -0.8f + i * 0.75f), 0, new Vector3(0.3f, 0, 0));
        // legs
        foreach (var (x, z) in new[] { (-1.7f, -0.6f), (1.7f, -0.6f), (-1.7f, 2.4f), (1.7f, 2.4f) })
        {
            Box(Rig, new Vector3(0.9f, 1.2f, 1.2f), scale, new Vector3(x, 0.6f, z));
            Box(Rig, new Vector3(1.0f, 0.3f, 1.5f), bone, new Vector3(x, 0.15f, z - 0.2f));
        }
        // folded wings
        Box(Rig, new Vector3(0.3f, 2.6f, 3.4f), scaleL, new Vector3(-1.75f, 2.6f, 1.2f), 0, new Vector3(0, 0, 0.45f));
        Box(Rig, new Vector3(0.3f, 2.6f, 3.4f), scaleL, new Vector3(1.75f, 2.6f, 1.2f), 0, new Vector3(0, 0, -0.45f));
        // tail curling around
        Box(Rig, new Vector3(1.6f, 1.2f, 2.0f), scale, new Vector3(0.6f, 0.8f, 4.0f), 0, new Vector3(0, 0.2f, 0));
        Box(Rig, new Vector3(1.1f, 0.9f, 2.0f), scale, new Vector3(1.8f, 0.6f, 5.5f), 0, new Vector3(0, 0.5f, 0));
        Box(Rig, new Vector3(0.7f, 0.6f, 2.0f), scale, new Vector3(3.0f, 0.5f, 5.2f), 0, new Vector3(0, 1.1f, 0));
        // neck + head (raised when awake)
        _neck = Pivot(Rig, new Vector3(0, 1.8f, -1.1f));
        Box(_neck, new Vector3(1.1f, 1.1f, 2.4f), scale, new Vector3(0, 0, -1.1f));
        var head = Pivot(_neck, new Vector3(0, 0, -2.4f));
        Box(head, new Vector3(1.7f, 1.1f, 2.0f), scaleL, new Vector3(0, 0, -0.7f));
        Box(head, new Vector3(1.3f, 0.5f, 1.2f), scaleL, new Vector3(0, -0.15f, -1.9f)); // snout
        _jaw = Pivot(head, new Vector3(0, -0.45f, -0.2f));
        Box(_jaw, new Vector3(1.3f, 0.3f, 2.2f), scale, new Vector3(0, 0, -1.0f));
        Box(_jaw, new Vector3(1.1f, 0.15f, 1.9f), belly, new Vector3(0, 0.15f, -1.0f));
        for (int i = 0; i < 4; i++) Box(_jaw, new Vector3(0.12f, 0.3f, 0.12f), bone, new Vector3(-0.5f + (i % 2) * 1.0f, 0.3f, -0.9f - (i / 2) * 0.7f));
        Box(head, new Vector3(0.3f, 0.3f, 0.9f), bone, new Vector3(-0.55f, 0.65f, 0.2f), 0, new Vector3(-0.6f, 0, 0));
        Box(head, new Vector3(0.3f, 0.3f, 0.9f), bone, new Vector3(0.55f, 0.65f, 0.2f), 0, new Vector3(-0.6f, 0, 0));
        _eyeL = Box(head, new Vector3(0.28f, 0.2f, 0.3f), new Color(1f, 0.25f, 0.05f), new Vector3(-0.6f, 0.3f, -1.1f), 0f);
        _eyeR = Box(head, new Vector3(0.28f, 0.2f, 0.3f), new Color(1f, 0.25f, 0.05f), new Vector3(0.6f, 0.3f, -1.1f), 0f);
        _mouthGlow = Box(head, new Vector3(0.8f, 0.25f, 0.8f), new Color(1f, 0.5f, 0.1f), new Vector3(0, -0.2f, -2.1f), 0f);
        // flame cone (hidden until breathing); axis along forward (-Z) from the mouth
        _flame = Pivot(head, new Vector3(0, -0.2f, -2.4f));
        float len = MonsterTuning.DragonBreathRange;
        _flameOuter = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = Mathf.Tan(MonsterTuning.DragonBreathHalfAngle) * len, BottomRadius = 0.2f, Height = len, RadialSegments = 10, Rings = 1 }, MaterialOverride = FireMat(false) };
        _flameOuter.Rotation = new Vector3(-Mathf.Pi / 2f, 0, 0); _flameOuter.Position = new Vector3(0, 0, -len / 2f);
        _flame.AddChild(_flameOuter);
        _flameInner = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = Mathf.Tan(MonsterTuning.DragonBreathHalfAngle) * len * 0.5f, BottomRadius = 0.12f, Height = len * 0.8f, RadialSegments = 8, Rings = 1 }, MaterialOverride = FireMat(true) };
        _flameInner.Rotation = new Vector3(-Mathf.Pi / 2f, 0, 0); _flameInner.Position = new Vector3(0, 0, -len * 0.4f);
        _flame.AddChild(_flameInner);
        _flame.Visible = false;
        _fireLight = new OmniLight3D { Position = new Vector3(0, 0, -3f), LightColor = new Color(1f, 0.5f, 0.15f), LightEnergy = 0f, OmniRange = 22f, ShadowEnabled = false };
        _flame.AddChild(_fireLight);
        Box(Rig, new Vector3(0.1f, 0.1f, 0.1f), Colors.Black, Vector3.Zero); // keeps Rig non-empty
    }

    protected override void BuildBody()
    {
        AddBox(new Vector3(3.6f, 3.0f, 5.0f), new Vector3(0, 1.5f, 1.0f));
        AddBox(new Vector3(1.8f, 1.4f, 4.5f), new Vector3(0, 1.0f, -2.9f));
    }

    protected override void Animate(float dt)
    {
        float t = Time.GetTicksMsec() / 1000f;
        float age = StateAge;
        int stage = Extra;
        bool awake = State == MonsterState.Awake || State == MonsterState.Windup || State == MonsterState.Attack || State == MonsterState.Recover;
        float neckTarget = 0f; float jaw = 0f; float eye = 0f; float breathRate = 1.1f; float breathAmp = 0.03f; float tremor = 0f;
        if (!awake)
        {
            neckTarget = stage >= 2 ? 0.15f : 0f;
            eye = stage >= 2 ? 1f : (stage == 1 ? 0.35f : 0f);
            breathRate = stage == 0 ? 1.1f : 2.6f;
            breathAmp = stage == 0 ? 0.03f : 0.06f;
            tremor = stage >= 1 ? 0.03f : 0f;
        }
        else
        {
            neckTarget = 0.7f; eye = 1f; breathRate = 3f; breathAmp = 0.05f;
            if (State == MonsterState.Windup) { float k = Mathf.Clamp(age / MonsterTuning.DragonWindup, 0f, 1f); neckTarget = 0.7f + 0.5f * k; jaw = 0.5f * k; tremor = 0.03f * k; }
            if (State == MonsterState.Attack) { neckTarget = 0.35f; jaw = 0.75f; }
            if (State == MonsterState.Recover) { neckTarget = 0.5f; jaw = 0.2f; }
        }
        _neckAngle = Mathf.Lerp(_neckAngle, neckTarget, 1f - Mathf.Exp(-4f * dt));
        _neck.Rotation = new Vector3(_neckAngle, 0, 0);
        _jaw.Rotation = new Vector3(Mathf.Lerp(_jaw.Rotation.X, jaw, 1f - Mathf.Exp(-10f * dt)), 0, 0);
        _chest.Scale = new Vector3(1f + breathAmp * 0.5f * Mathf.Sin(t * breathRate * 2f), 1f + breathAmp * Mathf.Sin(t * breathRate * 2f), 1f);
        Rig.Position = new Vector3(Mathf.Sin(t * 40f) * tremor * 0.3f, 0, Mathf.Cos(t * 37f) * tremor * 0.3f);
        // eyes: slit-dim when asleep, blazing when open
        float e = Mathf.Clamp(eye, 0f, 1f);
        _eyeL.Scale = new Vector3(1f, Mathf.Lerp(0.15f, 1f, e), 1f); _eyeR.Scale = _eyeL.Scale;
        var eyeMat = Util.Mat(new Color(1f, 0.25f, 0.05f), e > 0.5f ? 5f : (e > 0.2f ? 1.2f : 0f));
        _eyeL.MaterialOverride = eyeMat; _eyeR.MaterialOverride = eyeMat;
        // mouth glow builds up during the wind-up (telegraph), flame during the breath
        bool wind = State == MonsterState.Windup, breath = State == MonsterState.Attack;
        _mouthGlow.MaterialOverride = Util.Mat(new Color(1f, 0.5f, 0.1f), wind ? 2f + 4f * Mathf.Clamp(age / MonsterTuning.DragonWindup, 0f, 1f) : breath ? 6f : 0f);
        _flame.Visible = breath;
        _flame.Rotation = new Vector3(-_neckAngle, 0, 0);
        if (breath)
        {
            float fl = 1f + 0.08f * Mathf.Sin(t * 40f);
            _flameOuter.Scale = new Vector3(fl, 1f, fl); _flameInner.Scale = new Vector3(1f / fl, 1f, 1f / fl);
        }
        _fireLight.LightEnergy = breath ? 5f * (0.85f + 0.15f * Mathf.Sin(t * 33f)) : 0f;
    }
}
