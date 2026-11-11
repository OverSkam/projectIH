using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Something a lever can operate.</summary>
public interface ILeverMech
{
    bool CanActivate { get; }
    void ServerActivate(ServerPlayer by);
}

// ----------------------------------------------------------------------------- 2. Rockfall zone (also the lever "ceiling collapse")
/// <summary>Cracked ceiling that trickles dust. Triggered by loud noise, a long random timer, or a lever (lv=1). Rumbles 1.5 s, then crushes the zone and leaves rubble + loose ore.</summary>
public partial class RockfallObj : TrapBase, ILeverMech
{
    float _r, _ch;
    CpuParticles3D _dust, _heavy;
    Node3D _hang;
    readonly List<(MeshInstance3D m, Vector3 end)> _rocks = new();
    float _t, _rand;
    ServerPlayer _by;
    double _nextTick;
    bool _built;

    public bool CanActivate => State == "armed";
    public void ServerActivate(ServerPlayer by) => Trigger(by);

    public override void Build()
    {
        Prelude();
        _r = GF("r", TrapTuning.RockRadius); _ch = GF("ch", 4.5f);
        uint s = (uint)(Id * 2246822519u + 5);
        var dark = new Color(0.03f, 0.03f, 0.03f);
        _hang = new Node3D(); AddChild(_hang);
        // cracks across the ceiling patch
        for (int i = 0; i < 6; i++)
        {
            float a = i * 1.05f + Rand01(ref s) * 0.5f, len = _r * (0.7f + Rand01(ref s) * 0.7f);
            Box(this, new Vector3(len, 0.05f, 0.09f), new Vector3(Mathf.Cos(a) * len * 0.45f, _ch - 0.03f, Mathf.Sin(a) * len * 0.45f), dark, 0f, new Vector3(0, -a, 0));
        }
        // loose chunks hanging from the crack (lighter, so a lantern picks them out)
        for (int i = 0; i < 7; i++)
        {
            float a = Rand01(ref s) * 6.28f, d = Rand01(ref s) * _r * 0.75f, sz = 0.25f + Rand01(ref s) * 0.3f;
            Box(_hang, new Vector3(sz, sz * 1.3f, sz), new Vector3(Mathf.Cos(a) * d, _ch - sz * 0.5f, Mathf.Sin(a) * d), new Color(0.42f, 0.38f, 0.34f), 0f, new Vector3(Rand01(ref s), Rand01(ref s), Rand01(ref s)));
        }
        _dust = Particles(this, new Vector3(0, _ch - 0.1f, 0), new Color(0.72f, 0.68f, 0.6f), 0.025f, 14, 3.0f, 0.2f, Vector3.Down, 15f, new Vector3(0, -2.5f, 0), new Vector3(_r * 0.6f, 0.05f, _r * 0.6f));
        _dust.Emitting = true;
        _heavy = Particles(this, new Vector3(0, _ch - 0.1f, 0), new Color(0.5f, 0.45f, 0.4f), 0.08f, 70, 1.6f, 1f, Vector3.Down, 20f, new Vector3(0, -9f, 0), new Vector3(_r * 0.8f, 0.05f, _r * 0.8f), false, true);
        // pre-computed rocks that will fall
        for (int i = 0; i < 6; i++)
        {
            float a = Rand01(ref s) * 6.28f, d = Rand01(ref s) * _r * 0.65f;
            var sz = new Vector3(0.5f + Rand01(ref s) * 0.5f, 0.35f + Rand01(ref s) * 0.35f, 0.5f + Rand01(ref s) * 0.5f);
            var m = Util.BoxMesh(sz, new Color(0.36f + Rand01(ref s) * 0.08f, 0.33f, 0.3f));
            m.Visible = false;
            var end = new Vector3(Mathf.Cos(a) * d, sz.Y * 0.5f, Mathf.Sin(a) * d);
            m.Rotation = new Vector3(0, Rand01(ref s) * 3f, 0);
            AddChild(m);
            _rocks.Add((m, end));
        }
        _built = true;
        _rand = GB("lv") ? 1e9f : (GB("fast") ? 1e9f : (float)GD.RandRange(TrapTuning.RockRandomMin, TrapTuning.RockRandomMax));
    }

    public override void _Process(double delta)
    {
        if (!_built || State != "armed") return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now > _nextTick) { _nextTick = now + 4 + (Id % 5); TrapSfx.Play(GlobalPosition + Vector3.Up * _ch, "pebble", -10f); }
    }

    protected override void OnState(string old, string now)
    {
        if (!_built) return;
        bool init = old == "";
        switch (now)
        {
            case "armed": _dust.Emitting = true; _heavy.Emitting = false; break;
            case "rumble":
                _heavy.Emitting = true; _dust.Amount = 14;
                TrapSfx.Play(GlobalPosition, "rumble", 4f);
                var sh = CreateTween().SetLoops(12);
                sh.TweenProperty(_hang, "position", new Vector3(0.03f, 0, 0.02f), 0.06f);
                sh.TweenProperty(_hang, "position", new Vector3(-0.03f, 0, -0.02f), 0.06f);
                break;
            case "fall":
                _hang.Visible = false;
                foreach (var (m, end) in _rocks)
                {
                    m.Visible = true; m.Position = new Vector3(end.X, _ch - 0.3f, end.Z);
                    CreateTween().TweenProperty(m, "position:y", end.Y, TrapTuning.RockFallTime).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                }
                break;
            case "rubble":
                _heavy.Emitting = false; _dust.Emitting = false; _hang.Visible = false;
                foreach (var (m, end) in _rocks) { m.Visible = true; m.Position = end; }
                if (!init)
                {
                    TrapSfx.Play(GlobalPosition, "thud", 6f);
                    var burst = Particles(this, new Vector3(0, 0.3f, 0), new Color(0.6f, 0.56f, 0.5f, 0.35f), 0.35f, 24, 1.8f, 1.5f, Vector3.Up, 70f, new Vector3(0, 0f, 0), new Vector3(_r * 0.5f, 0.1f, _r * 0.5f), true);
                    burst.OneShot = true; burst.Explosiveness = 0.9f; burst.Emitting = true;
                }
                // a couple of the boulders block, so the rubble is a real obstacle
                CollisionLayer = 1 | 4;
                for (int i = 0; i < 2 && i < _rocks.Count; i++)
                    AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = ((BoxMesh)_rocks[i].m.Mesh).Size }, Transform = new Transform3D(Basis.FromEuler(_rocks[i].m.Rotation), _rocks[i].end) });
                break;
        }
    }

    public override void DebugForce() => Trigger(null);

    /// <summary>Server: start the rumble. Credit for kills goes to `by`.</summary>
    public void Trigger(ServerPlayer by)
    {
        if (!Srv || State != "armed") return;
        _by = by;
        _t = TrapTuning.RockRumble;
        SetState("rumble");
    }

    protected override void ServerTick(float dt)
    {
        _t -= dt;
        switch (State)
        {
            case "armed":
                _rand -= dt;
                if (_rand <= 0)
                {
                    // random collapse only when somebody is around to meet it
                    bool near = false;
                    foreach (var p in TrapUtil.Miners()) if (p.Pos.DistanceTo(GlobalPosition) < 16f) near = true;
                    if (near) Trigger(null); else _rand = 20f;
                }
                break;
            case "rumble":
                if (_t <= 0) { SetState("fall"); _t = TrapTuning.RockFallTime; }
                break;
            case "fall":
                if (_t <= 0)
                {
                    float dmg = GB("lv") ? TrapTuning.CollapseDamage : TrapTuning.RockCrushDamage;
                    foreach (var p in TrapUtil.Miners())
                    {
                        var d = new Vector2(p.Pos.X - GlobalPosition.X, p.Pos.Z - GlobalPosition.Z).Length();
                        if (d < _r && p.Pos.Y > Floor - 1f && p.Pos.Y < Floor + _ch)
                            TrapUtil.Hurt(p, dmg, _by, "crush", "rockfall");
                    }
                    SetState("rubble");
                    SpawnOre();
                }
                break;
        }
    }

    void SpawnOre()
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(Id * 977 + 3) };
        int depth = Game.I.World.Cave.RoomAt(GlobalPosition)?.Depth ?? 1;
        int n = rng.RandiRange(TrapTuning.RockOreMin, TrapTuning.RockOreMax);
        for (int i = 0; i < n; i++)
        {
            float a = rng.Randf() * 6.28f + i * 3.14f;
            var pos = GlobalPosition + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (_r * 0.95f) + new Vector3(0, 0.45f, 0);
            Game.I.ServerSpawnObject("pickup", pos, "ore:" + Ores.Pick(rng, depth + 1, Game.I.Round));
        }
        Util.Pass("rockfall_ore", $"id={Id} count={n}");
    }

    /// <summary>Server: hear noise. Dynamite and heavy mining set rockfalls off.</summary>
    public static void OnNoise(Vector3 pos, float loudness, ServerPlayer by)
    {
        if (!Srv || Game.I?.World == null || loudness < TrapTuning.RockNoiseMin) return;
        float range = Mathf.Min(loudness, TrapTuning.RockNoiseRange);
        foreach (var o in Game.I.World.Objects.Values.ToArray())
            if (o is RockfallObj r && r.State == "armed" && !r.GB("lv") && r.GlobalPosition.DistanceTo(pos) <= range)
                r.Trigger(by);
    }

    /// <summary>Server: make the ceiling come down at pos (dynamite, scripted events). Uses a nearby armed rockfall zone or creates one.</summary>
    public static void TriggerRockfallAt(Vector3 pos, ServerPlayer by)
    {
        if (!Srv || Game.I?.World == null) return;
        foreach (var o in Game.I.World.Objects.Values.ToArray())
            if (o is RockfallObj r && r.State == "armed" && new Vector2(r.GlobalPosition.X - pos.X, r.GlobalPosition.Z - pos.Z).Length() <= r._r + 2f) { r.Trigger(by); return; }
        var pr = Probe.At(pos);
        var fp = new Vector3(pos.X, pr.FloorY, pos.Z);
        int id = Game.I.ServerSpawnObject("trap_rockfall", fp, Mk(("s", "armed"), ("r", TrapTuning.RockRadius), ("ch", pr.CeilH), ("lv", true)));
        if (Game.I.World.Objects.TryGetValue(id, out var no) && no is RockfallObj nr) nr.Trigger(by);
    }
}

// ----------------------------------------------------------------------------- 5. Trapdoor pit (also the lever trapdoor)
/// <summary>
/// A plank panel in the floor. After a creak it opens and the pit beneath is a real, climbable ramp pit.
/// The cave floor itself is not carved: while the LOCAL miner stands inside the footprint its collision mask drops the world layer
/// and uses the pit layer instead (pit floor/walls/ramp/lid are all on TrapTuning.PitLayer), on every peer.
/// Local frame: +X along the pit (deep end at -X, ramp rises toward +X), Z across.
/// </summary>
public partial class TrapdoorObj : TrapBase, ILeverMech
{
    float _hl, _hw, _depth;
    Node3D _lid, _lidPivot;
    MeshInstance3D _void;
    CollisionShape3D _lidShape;
    float _t, _stand;
    static int _maskFrame = -1;

    public override void DebugForce() { if (State == "closed") { SetState("creak"); _t = TrapTuning.TrapdoorCreak; } }
    public bool CanActivate => State == "closed";
    public void ServerActivate(ServerPlayer by) { if (State == "closed") { SetState("creak"); _t = TrapTuning.TrapdoorCreak; } }

    public override void Build()
    {
        Prelude("a");
        CollisionLayer = TrapTuning.PitLayer; CollisionMask = 0;
        _hl = GF("hl", TrapTuning.PitLength); _hw = GF("hw", TrapTuning.PitWidth); _depth = GF("dp", TrapTuning.PitDepth);
        var rock = new Color(0.2f, 0.18f, 0.17f);
        // --- collision (all on the pit layer) ---
        void Shape(Vector3 size, Vector3 pos, Vector3? rot = null, bool vis = true, Color? col = null)
        {
            var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = size } };
            var lt = new Transform3D(rot.HasValue ? Basis.FromEuler(rot.Value) : Basis.Identity, pos);
            cs.Transform = Pivot.Transform * lt;
            AddChild(cs);
            if (vis) { var m = Util.BoxMesh(size, col ?? rock); m.Transform = lt; Pivot.AddChild(m); }
        }
        Shape(new Vector3(_hl, 0.5f, _hw), new Vector3(0, -_depth - 0.25f, 0));                                   // pit floor
        Shape(new Vector3(_hl + 0.8f, _depth + 0.5f, 0.4f), new Vector3(0, -(_depth + 0.5f) / 2f, _hw / 2f + 0.2f)); // side walls
        Shape(new Vector3(_hl + 0.8f, _depth + 0.5f, 0.4f), new Vector3(0, -(_depth + 0.5f) / 2f, -_hw / 2f - 0.2f));
        Shape(new Vector3(0.4f, _depth + 0.5f, _hw), new Vector3(-_hl / 2f - 0.2f, -(_depth + 0.5f) / 2f, 0));     // deep end wall
        // ramp from (x0, -depth) up to (hl/2, 0)
        float x0 = -_hl / 2f + 1f, x1 = _hl / 2f;
        float ang = Mathf.Atan2(_depth, x1 - x0), len = Mathf.Sqrt((x1 - x0) * (x1 - x0) + _depth * _depth);
        var mid = new Vector3((x0 + x1) / 2f, -_depth / 2f, 0);
        var n = new Vector3(Mathf.Sin(ang), -Mathf.Cos(ang), 0);   // push the slab below the walking surface
        Shape(new Vector3(len + 0.6f, 0.4f, _hw), mid + n * 0.2f, new Vector3(0, 0, ang), true, new Color(0.3f, 0.24f, 0.16f));
        // lid (solid while closed)
        _lidShape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(_hl, 0.3f, _hw) }, Transform = Pivot.Transform * new Transform3D(Basis.Identity, new Vector3(0, -0.15f, 0)) };
        AddChild(_lidShape);

        // --- visuals above the floor ---
        _void = Box(Pivot, new Vector3(_hl, 0.02f, _hw), new Vector3(0, 0.015f, 0), new Color(0, 0, 0)); _void.Visible = false;
        var iron = new Color(0.14f, 0.13f, 0.13f);
        Box(Pivot, new Vector3(_hl + 0.2f, 0.05f, 0.1f), new Vector3(0, 0.025f, _hw / 2f + 0.05f), iron);
        Box(Pivot, new Vector3(_hl + 0.2f, 0.05f, 0.1f), new Vector3(0, 0.025f, -_hw / 2f - 0.05f), iron);
        Box(Pivot, new Vector3(0.1f, 0.05f, _hw), new Vector3(_hl / 2f + 0.05f, 0.025f, 0), iron);
        Box(Pivot, new Vector3(0.1f, 0.05f, _hw), new Vector3(-_hl / 2f - 0.05f, 0.025f, 0), iron);
        _lidPivot = new Node3D { Position = new Vector3(0, 0.03f, _hw / 2f) };  // hinge on the +Z long edge
        Pivot.AddChild(_lidPivot);
        _lid = new Node3D { Position = new Vector3(0, 0, -_hw / 2f) };
        _lidPivot.AddChild(_lid);
        var wood = new Color(0.27f, 0.18f, 0.11f);
        Box(_lid, new Vector3(_hl, 0.04f, _hw), Vector3.Zero, wood);
        int planks = 8;
        for (int i = 1; i < planks; i++) Box(_lid, new Vector3(0.04f, 0.045f, _hw), new Vector3(-_hl / 2f + i * _hl / planks, 0.002f, 0), new Color(0.1f, 0.07f, 0.05f));
        foreach (float bx in new[] { -_hl * 0.3f, _hl * 0.3f })
        {
            Box(_lid, new Vector3(0.18f, 0.05f, _hw), new Vector3(bx, 0.004f, 0), iron);
            Box(_lid, new Vector3(0.1f, 0.06f, 0.1f), new Vector3(bx, 0.01f, _hw * 0.35f), new Color(0.4f, 0.35f, 0.25f), 0.2f);
            Box(_lid, new Vector3(0.1f, 0.06f, 0.1f), new Vector3(bx, 0.01f, -_hw * 0.35f), new Color(0.4f, 0.35f, 0.25f), 0.2f);
        }
    }

    protected override void OnState(string old, string now)
    {
        if (_lid == null) return;
        bool init = old == "";
        switch (now)
        {
            case "closing":
                CreateTween().TweenProperty(_lidPivot, "rotation:x", 0f, 0.4f);
                break;
            case "closed":
                _lidShape.SetDeferred("disabled", false);
                _void.Visible = false;
                _lidPivot.Rotation = Vector3.Zero;
                break;
            case "creak":
                TrapSfx.Play(GlobalPosition, "creak", 4f);
                var tw = CreateTween().SetLoops(6);
                tw.TweenProperty(_lidPivot, "rotation:x", -0.03f, 0.075f);
                tw.TweenProperty(_lidPivot, "rotation:x", 0.0f, 0.075f);
                break;
            case "open":
                _lidShape.SetDeferred("disabled", true);
                _void.Visible = true;
                TrapSfx.Play(GlobalPosition, "slam", 0f);
                if (init) _lidPivot.Rotation = new Vector3(-1.5f, 0, 0);
                else CreateTween().TweenProperty(_lidPivot, "rotation:x", -1.5f, 0.3f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                break;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        base._PhysicsProcess(delta);
        // collision override for the local miner (every peer)
        var m = Game.I?.LocalMiner;
        if (m == null || !GodotObject.IsInstanceValid(m) || Pivot == null) return;
        var l = Pivot.ToLocal(m.GlobalPosition);
        bool inside = Mathf.Abs(l.X) <= _hl / 2f + 0.05f && Mathf.Abs(l.Z) <= _hw / 2f + 0.05f && l.Y > -_depth - 1.5f && l.Y < 3.5f;
        int frame = (int)Engine.GetPhysicsFrames();
        if (inside) { m.CollisionMask = TrapTuning.PitLayer; _maskFrame = frame; }
        else if (_maskFrame != frame) m.CollisionMask = 1u | TrapTuning.PitLayer;
    }

    public override void _ExitTree()
    {
        var m = Game.I?.LocalMiner;
        if (m != null && GodotObject.IsInstanceValid(m)) m.CollisionMask = 1;
    }

    bool InLocal(ServerPlayer p, out Vector3 l)
    {
        l = Pivot.ToLocal(p.Pos);
        return Mathf.Abs(l.X) <= _hl / 2f && Mathf.Abs(l.Z) <= _hw / 2f;
    }

    protected override void ServerTick(float dt)
    {
        _t -= dt;
        switch (State)
        {
            case "closed":
                if (GB("lv") || _t > 0) { _stand = 0; break; }
                bool on = false;
                foreach (var p in TrapUtil.Miners())
                {
                    if (Util.HasArg("trapdebug") && Engine.GetPhysicsFrames() % 120 == 0) Util.Log($"TRAPDBG {p.Name} pos={p.Pos} local={Pivot.ToLocal(p.Pos)} obj={GlobalPosition} hl={_hl} hw={_hw}");
                    if (InLocal(p, out var l) && l.X < 0.5f && p.Pos.Y < Floor + 0.5f && p.Pos.Y > Floor - 0.5f) on = true;
                }
                _stand = on ? _stand + dt : 0;
                if (_stand >= TrapTuning.TrapdoorStand) { _stand = 0; SetState("creak"); _t = TrapTuning.TrapdoorCreak; }
                break;
            case "creak":
                if (_t <= 0) { SetState("open"); _t = GB("lv") ? TrapTuning.TrapdoorLeverOpen : TrapTuning.TrapdoorOpen; }
                break;
            case "open":
                if (_t <= 0)
                {
                    bool occupied = false;
                    foreach (var p in TrapUtil.Miners()) if (InLocal(p, out var l) && p.Pos.Y < Floor - 0.3f) occupied = true;
                    if (!occupied) { SetState("closing"); _t = 0.5f; }
                }
                break;
            case "closing":
                if (_t <= 0) { SetState("closed"); _t = TrapTuning.TrapdoorRearm; }
                break;
        }
    }
}
