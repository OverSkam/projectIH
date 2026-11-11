using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

// ----------------------------------------------------------------------------- 1. Spike plate
/// <summary>Floor plate: step on it, it clicks, spikes shoot up 0.6 s later, then retract and re-arm.</summary>
public partial class SpikePlateObj : TrapBase
{
    Node3D _spikes, _plate;
    float _t;
    bool _built;

    public override void Build()
    {
        Prelude();
        var plateCol = new Color(0.30f, 0.25f, 0.22f);
        _plate = new Node3D(); AddChild(_plate);
        Box(_plate, new Vector3(1.7f, 0.05f, 1.7f), new Vector3(0, 0.025f, 0), plateCol);
        var rim = new Color(0.12f, 0.1f, 0.09f);
        float h = 0.85f;
        Box(_plate, new Vector3(1.8f, 0.06f, 0.07f), new Vector3(0, 0.03f, h), rim);
        Box(_plate, new Vector3(1.8f, 0.06f, 0.07f), new Vector3(0, 0.03f, -h), rim);
        Box(_plate, new Vector3(0.07f, 0.06f, 1.8f), new Vector3(h, 0.03f, 0), rim);
        Box(_plate, new Vector3(0.07f, 0.06f, 1.8f), new Vector3(-h, 0.03f, 0), rim);
        _spikes = new Node3D { Position = new Vector3(0, -0.95f, 0) };
        AddChild(_spikes);
        for (int x = -1; x <= 1; x++)
            for (int z = -1; z <= 1; z++)
            {
                Box(_plate, new Vector3(0.1f, 0.01f, 0.1f), new Vector3(x * 0.5f, 0.053f, z * 0.5f), new Color(0.03f, 0.03f, 0.03f));
                Cone(_spikes, 0.09f, 0.95f, new Vector3(x * 0.5f, 0.475f, z * 0.5f), new Color(0.72f, 0.72f, 0.78f));
            }
        _built = true;
    }

    protected override void OnState(string old, string now)
    {
        if (!_built) return;
        bool init = old == "";
        switch (now)
        {
            case "armed": if (!init) CreateTween().TweenProperty(_plate, "position:y", 0.0f, 0.2f); break;
            case "click":
                _plate.Position = new Vector3(0, -0.03f, 0);
                TrapSfx.Play(GlobalPosition, "click");
                break;
            case "up":
                CreateTween().TweenProperty(_spikes, "position:y", 0.0f, 0.07f);
                TrapSfx.Play(GlobalPosition, "thud", -4f);
                break;
            case "down":
                CreateTween().TweenProperty(_spikes, "position:y", -0.95f, 0.5f);
                break;
        }
    }

    bool OnPlate(float half, float maxY)
    {
        foreach (var p in TrapUtil.Miners())
            if (Mathf.Abs(p.Pos.X - GlobalPosition.X) < half && Mathf.Abs(p.Pos.Z - GlobalPosition.Z) < half && p.Pos.Y < Floor + maxY && p.Pos.Y > Floor - 0.6f) return true;
        return false;
    }

    protected override void ServerTick(float dt)
    {
        _t -= dt;
        switch (State)
        {
            case "armed":
                if (OnPlate(TrapTuning.SpikeHalf, 0.45f)) { SetState("click"); _t = TrapTuning.SpikeClickDelay; }
                break;
            case "click":
                if (_t <= 0)
                {
                    SetState("up"); _t = TrapTuning.SpikeUpTime;
                    float half = TrapTuning.SpikeHalf + 0.1f;
                    foreach (var p in TrapUtil.Miners())
                        if (Mathf.Abs(p.Pos.X - GlobalPosition.X) < half && Mathf.Abs(p.Pos.Z - GlobalPosition.Z) < half && p.Pos.Y < Floor + 0.8f && p.Pos.Y > Floor - 0.6f)
                            TrapUtil.Hurt(p, TrapTuning.SpikeDamage, null, "spikes", "spikes");
                }
                break;
            case "up": if (_t <= 0) { SetState("down"); _t = TrapTuning.SpikeRearm; } break;
            case "down": if (_t <= 0) SetState("armed"); break;
        }
    }
}

// ----------------------------------------------------------------------------- 3. Gas vent
/// <summary>Floor crack with a faint green glow and wisps; every few seconds it hisses, then spews a green cloud.</summary>
public partial class GasVentObj : TrapBase
{
    CpuParticles3D _wisp, _cloud;
    OmniLight3D _glow;
    MeshInstance3D _sliver;
    float _t, _tick;
    double _nextHiss;
    bool _built;
    static readonly Color Green = new Color(0.35f, 0.85f, 0.25f);

    public override void Build()
    {
        Prelude();
        uint s = (uint)(Id * 2654435761u + 17);
        var dark = new Color(0.04f, 0.04f, 0.04f);
        // jagged crack: several dark slabs radiating, glowing sliver in the middle, little rubble rim
        for (int i = 0; i < 5; i++)
        {
            float a = i * 1.26f + Rand01(ref s) * 0.4f, len = 0.9f + Rand01(ref s) * 0.7f;
            Box(Pivot, new Vector3(len, 0.02f, 0.09f), new Vector3(Mathf.Cos(a) * len * 0.45f, 0.012f, Mathf.Sin(a) * len * 0.45f), dark, 0f, new Vector3(0, -a, 0));
        }
        _sliver = Box(Pivot, new Vector3(0.7f, 0.025f, 0.12f), new Vector3(0, 0.02f, 0), Green, 1.2f, new Vector3(0, 0.4f, 0));
        for (int i = 0; i < 6; i++)
        {
            float a = i * 1.047f;
            Box(Pivot, new Vector3(0.22f, 0.12f, 0.18f), new Vector3(Mathf.Cos(a) * 0.55f, 0.05f, Mathf.Sin(a) * 0.55f), new Color(0.26f, 0.24f, 0.22f), 0f, new Vector3(Rand01(ref s), a, Rand01(ref s)));
        }
        _glow = new OmniLight3D { Position = new Vector3(0, 0.25f, 0), LightColor = Green, LightEnergy = 0.35f, OmniRange = 3.5f };
        AddChild(_glow);
        _wisp = Particles(this, new Vector3(0, 0.15f, 0), new Color(0.5f, 0.8f, 0.4f, 0.5f), 0.07f, 8, 2.2f, 0.5f, Vector3.Up, 25f, new Vector3(0, 0.1f, 0), new Vector3(0.3f, 0.02f, 0.3f), true);
        _wisp.Emitting = true;
        _cloud = Particles(this, new Vector3(0, 0.4f, 0), new Color(0.35f, 0.85f, 0.25f, 0.30f), 0.45f, 70, 2.8f, 2.2f, Vector3.Up, 75f, new Vector3(0, 0.1f, 0), new Vector3(0.6f, 0.1f, 0.6f), true);
        _built = true;
        _t = 3f;
    }

    protected override void OnState(string old, string now)
    {
        if (!_built) return;
        switch (now)
        {
            case "idle":
                _cloud.Emitting = false; _glow.LightEnergy = 0.35f; _wisp.Amount = 8; _wisp.Emitting = true;
                break;
            case "warn":
                _wisp.Amount = 30; _wisp.Speed = 1f; _glow.LightEnergy = 1.2f;
                TrapSfx.Play(GlobalPosition, "hiss", -6f);
                break;
            case "vent":
                _cloud.Emitting = true; _glow.LightEnergy = 3.2f; _glow.OmniRange = TrapTuning.GasRadius + 2.5f;
                TrapSfx.Play(GlobalPosition, "hiss", 2f);
                break;
        }
        if (now != "vent") _glow.OmniRange = 3.5f;
    }

    public override void _Process(double delta)
    {
        if (!_built) return;
        // faint periodic hiss while idle so an attentive player hears the vent
        double now = Time.GetTicksMsec() / 1000.0;
        if (State == "idle" && now > _nextHiss)
        {
            _nextHiss = now + 3.5 + (Id % 4);
            TrapSfx.Play(GlobalPosition, "hiss", -18f);
        }
        if (State == "idle") _glow.LightEnergy = 0.3f + 0.12f * Mathf.Sin((float)now * 3f + Id);
    }

    protected override void ServerTick(float dt)
    {
        _t -= dt;
        switch (State)
        {
            case "idle":
                if (_t <= 0) { SetState("warn"); _t = TrapTuning.GasWarn; }
                break;
            case "warn":
                if (_t <= 0) { SetState("vent"); _t = TrapTuning.GasVent; _tick = 0; }
                break;
            case "vent":
                _tick -= dt;
                if (_tick <= 0)
                {
                    _tick = TrapTuning.GasTick;
                    foreach (var p in TrapUtil.Miners())
                    {
                        var d = new Vector2(p.Pos.X - GlobalPosition.X, p.Pos.Z - GlobalPosition.Z).Length();
                        if (d < TrapTuning.GasRadius && p.Pos.Y > Floor - 0.5f && p.Pos.Y < Floor + 3.2f)
                            TrapUtil.Hurt(p, TrapTuning.GasTickDamage, null, "gas", "gas");
                    }
                }
                if (_t <= 0)
                {
                    SetState("idle");
                    _t = (GB("fast") ? 1.5f : (float)GD.RandRange(TrapTuning.GasIdleMin, TrapTuning.GasIdleMax)) + 0f;
                }
                break;
        }
    }
}

// ----------------------------------------------------------------------------- 4. Dart tripwire
/// <summary>Thin wire at ankle height across the passage. Jumping over it is safe; crouching is not. Trips a wall launcher that fires darts along the corridor.</summary>
public partial class DartWireObj : TrapBase
{
    // layout (local frame: +X = dart travel direction, Z = across)
    float _wp, _wn, _dl, _range;
    MeshInstance3D _wire;
    Node3D _head;
    readonly List<(MeshInstance3D m, float delay)> _darts = new();
    readonly List<float> _dist = new();     // client dart travel distance
    double _volleyT;                        // client volley clock
    bool _volley;
    float _t;
    // server
    readonly List<(Vector3 prev, float delay, bool alive)> _sdarts = new();
    double _svT;

    public override void Build()
    {
        float a = GF("a");
        Prelude(a);
        _wp = GF("wp", 2f); _wn = GF("wn", 2f); _dl = GF("dl", 6f); _range = GF("rg", 12f);
        var iron = new Color(0.18f, 0.17f, 0.17f);
        // wire + pegs
        _wire = Beam(Pivot, new Vector3(0, TrapTuning.DartWireHeight, -_wn), new Vector3(0, TrapTuning.DartWireHeight, _wp), 0.035f, new Color(0.62f, 0.6f, 0.5f), 0.35f);
        Box(Pivot, new Vector3(0.14f, 0.5f, 0.14f), new Vector3(0, 0.25f, _wp + 0.05f), iron);
        Box(Pivot, new Vector3(0.14f, 0.5f, 0.14f), new Vector3(0, 0.25f, -_wn - 0.05f), iron);
        // launcher: a stone post with a dart head, behind the wire (at -X), aimed at +X
        var lp = new Node3D { Position = new Vector3(-_dl, 0, 0) };
        Pivot.AddChild(lp);
        Box(lp, new Vector3(0.6f, 1.0f, 0.6f), new Vector3(0, 0.5f, 0), new Color(0.3f, 0.28f, 0.26f));
        _head = new Node3D { Position = new Vector3(0, 1.3f, 0) }; lp.AddChild(_head);
        Box(_head, new Vector3(0.6f, 0.75f, 0.75f), Vector3.Zero, new Color(0.22f, 0.2f, 0.19f));
        for (int i = 0; i < 3; i++)
            Box(_head, new Vector3(0.12f, 0.12f, 0.12f), new Vector3(0.32f, -0.22f + i * 0.22f, 0), new Color(0.02f, 0.02f, 0.02f));
        // warning smear on the post
        Box(lp, new Vector3(0.62f, 0.08f, 0.62f), new Vector3(0, 0.95f, 0), new Color(0.55f, 0.3f, 0.1f));
    }

    Vector3 DartStart(int i) => Pivot.GlobalTransform * new Vector3(-_dl + 0.45f, 1.1f + (i - 1) * 0.22f, 0);
    Vector3 DartDir => Pivot.GlobalTransform.Basis * Vector3.Right;

    protected override void OnState(string old, string now)
    {
        if (Pivot == null) return;
        switch (now)
        {
            case "fire":
                TrapSfx.Play(GlobalPosition, "twang");
                TrapSfx.Play(GlobalPosition + DartDir * -_dl, "click", -2f);
                _wire.Position = new Vector3(0, 0.06f, 0);
                var tw = CreateTween();
                tw.TweenProperty(_wire, "scale", new Vector3(1.6f, 1.6f, 1f), 0.08f);
                tw.TweenProperty(_wire, "scale", Vector3.One, 0.3f);
                break;
            case "volley":
                _volley = true; _volleyT = 0;
                foreach (var d in _darts) d.m.QueueFree();
                _darts.Clear();
                for (int i = 0; i < TrapTuning.DartCount; i++)
                {
                    var m = Util.BoxMesh(new Vector3(0.04f, 0.04f, 0.45f), new Color(0.9f, 0.85f, 0.6f), 0.8f);
                    m.Visible = false;
                    Pivot.AddChild(m);
                    _darts.Add((m, i * TrapTuning.DartStagger));
                }
                TrapSfx.Play(GlobalPosition + DartDir * -_dl, "whoosh", 2f);
                break;
            case "reload":
                _volley = false;
                foreach (var d in _darts) d.m.QueueFree();
                _darts.Clear();
                break;
            case "armed":
                _wire.Position = Vector3.Zero;
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!_volley) return;
        _volleyT += delta;
        for (int i = 0; i < _darts.Count; i++)
        {
            float tt = (float)_volleyT - _darts[i].delay;
            var m = _darts[i].m;
            if (tt < 0) continue;
            float x = -_dl + 0.45f + TrapTuning.DartSpeed * tt;
            m.Visible = x < -_dl + _range;
            m.Position = new Vector3(x, 1.1f + (i - 1) * 0.22f, 0);
        }
    }

    bool Crossing(ServerPlayer p)
    {
        var a = Pivot.GlobalTransform * new Vector3(0, 0, -_wn);
        var b = Pivot.GlobalTransform * new Vector3(0, 0, _wp);
        return p.Pos.Y < Floor + TrapTuning.DartTriggerHeight && p.Pos.Y > Floor - 0.5f && TrapUtil.DistToSegXZ(p.Pos, a, b) < 0.45f;
    }

    protected override void ServerTick(float dt)
    {
        _t -= dt;
        switch (State)
        {
            case "armed":
                foreach (var p in TrapUtil.Miners())
                    if (Crossing(p)) { SetState("fire"); _t = TrapTuning.DartTelegraph; break; }
                break;
            case "fire":
                if (_t <= 0)
                {
                    SetState("volley"); _t = 1.3f; _svT = 0;
                    _sdarts.Clear();
                    for (int i = 0; i < TrapTuning.DartCount; i++) _sdarts.Add((DartStart(i), i * TrapTuning.DartStagger, true));
                }
                break;
            case "volley":
                _svT += dt;
                for (int i = 0; i < _sdarts.Count; i++)
                {
                    var (prev, delay, alive) = _sdarts[i];
                    if (!alive || _svT < delay) continue;
                    float tt = (float)_svT - delay;
                    var start = DartStart(i);
                    var cur = start + DartDir * (TrapTuning.DartSpeed * tt);
                    if (prev == start && tt < dt * 1.5f) prev = start;
                    bool hit = false;
                    foreach (var p in TrapUtil.Miners())
                    {
                        if (start.Y < p.Pos.Y || start.Y > p.Pos.Y + 1.8f) continue;
                        if (TrapUtil.DistToSegXZ(p.Pos, prev, cur) < 0.45f)
                        {
                            TrapUtil.Hurt(p, TrapTuning.DartDamage, null, "darts", "darts");
                            hit = true; break;
                        }
                    }
                    bool gone = hit || start.DistanceTo(cur) > _range;
                    _sdarts[i] = (cur, delay, !gone);
                }
                if (_t <= 0) { SetState("reload"); _t = TrapTuning.DartReload; }
                break;
            case "reload": if (_t <= 0) SetState("armed"); break;
        }
    }
}
