using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

// ----------------------------------------------------------------------------- 6. Gate (portcullis) operated by a lever
/// <summary>Portcullis across a passage. Rattles for 1 s, drops (waits until nobody stands under it), stays down for N seconds, rises. Blocks miners and monsters.</summary>
public partial class GateObj : TrapBase, ILeverMech
{
    float _w, _h, _t, _up;
    Node3D _bars;
    CollisionShape3D _shape;
    ServerPlayer _by;

    public bool CanActivate => State == "up";
    public void ServerActivate(ServerPlayer by) { _by = by; SetState("warn"); _t = TrapTuning.GateWarn; }

    public override void Build()
    {
        Prelude("a");
        CollisionLayer = 1 | 4 | 32; CollisionMask = 0;
        _w = GF("w", 4f); _h = GF("h", 3.5f);
        _up = _h - 0.7f;
        var iron = new Color(0.2f, 0.19f, 0.2f);
        foreach (float sz in new[] { -1f, 1f })
            Box(Pivot, new Vector3(0.45f, _h, 0.4f), new Vector3(0, _h / 2f, sz * (_w / 2f - 0.1f)), new Color(0.27f, 0.25f, 0.24f));
        Box(Pivot, new Vector3(0.5f, 0.4f, _w), new Vector3(0, _h - 0.2f, 0), new Color(0.27f, 0.25f, 0.24f));
        _bars = new Node3D(); Pivot.AddChild(_bars);
        int n = (int)(_w / 0.32f);
        for (int i = 0; i <= n; i++)
        {
            float z = -_w / 2f + 0.3f + i * (_w - 0.6f) / n;
            Box(_bars, new Vector3(0.08f, _h - 0.25f, 0.08f), new Vector3(0, (_h - 0.25f) / 2f + 0.25f, z), iron);
            Cone(_bars, 0.06f, 0.35f, new Vector3(0, 0.175f, z), new Color(0.55f, 0.55f, 0.6f), true);
        }
        Box(_bars, new Vector3(0.1f, 0.1f, _w - 0.4f), new Vector3(0, 0.7f, 0), iron);
        Box(_bars, new Vector3(0.1f, 0.1f, _w - 0.4f), new Vector3(0, _h * 0.65f, 0), iron);
        _shape = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.35f, _h, _w) }, Transform = Pivot.Transform * new Transform3D(Basis.Identity, new Vector3(0, _h / 2f, 0)), Disabled = true };
        AddChild(_shape);
        _bars.Position = new Vector3(0, _up, 0);
    }

    protected override void OnState(string old, string now)
    {
        if (_bars == null) return;
        bool init = old == "";
        switch (now)
        {
            case "up": _shape.SetDeferred("disabled", true); _bars.Position = new Vector3(0, _up, 0); break;
            case "warn":
                TrapSfx.Play(GlobalPosition, SfxNames.GateChains, 2f);
                var tw = CreateTween().SetLoops(5);
                tw.TweenProperty(_bars, "position:y", _up - 0.18f, 0.1f);
                tw.TweenProperty(_bars, "position:y", _up, 0.1f);
                break;
            case "down":
                _shape.SetDeferred("disabled", false);
                if (init) _bars.Position = Vector3.Zero;
                else
                {
                    CreateTween().TweenProperty(_bars, "position:y", 0f, 0.18f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                    TrapSfx.Play(GlobalPosition, SfxNames.GateSlam, 4f);
                }
                break;
            case "rising":
                _shape.SetDeferred("disabled", true);
                CreateTween().TweenProperty(_bars, "position:y", _up, TrapTuning.GateRise);
                TrapSfx.Play(GlobalPosition, SfxNames.GateChains, 0f);
                break;
        }
    }

    protected override void ServerTick(float dt)
    {
        _t -= dt;
        switch (State)
        {
            case "warn":
                if (_t > 0) break;
                bool under = false;
                foreach (var p in TrapUtil.Miners())
                {
                    var l = Pivot.ToLocal(p.Pos);
                    if (Mathf.Abs(l.X) < 1.0f && Mathf.Abs(l.Z) < _w / 2f + 0.2f && p.Pos.Y < Floor + 3f) under = true;
                }
                if (!under) { SetState("down"); _t = TrapTuning.GateDown; }   // waits (still open) until the doorway is clear
                break;
            case "down": if (_t <= 0) { SetState("rising"); _t = TrapTuning.GateRise; } break;
            case "rising": if (_t <= 0) SetState("up"); break;
        }
    }
}

// ----------------------------------------------------------------------------- Lever
/// <summary>Wall-less pedestal lever. Linked to its mechanism by a visible chain along the floor and up to the mechanism's anchor.</summary>
public partial class LeverObj : TrapBase
{
    Node3D _arm;
    float _t;
    public override float InteractRange => 3.0f;

    public override void Build()
    {
        Prelude();
        CollisionLayer = 1 | 4; CollisionMask = 0;
        var stone = new Color(0.3f, 0.28f, 0.26f);
        Box(this, new Vector3(0.7f, 0.3f, 0.7f), new Vector3(0, 0.15f, 0), stone);
        Box(this, new Vector3(0.3f, 0.8f, 0.3f), new Vector3(0, 0.7f, 0), new Color(0.22f, 0.2f, 0.19f));
        Box(this, new Vector3(0.5f, 0.12f, 0.12f), new Vector3(0, 1.0f, 0.18f), new Color(0.15f, 0.15f, 0.16f));
        _arm = new Node3D { Position = new Vector3(0, 1.0f, 0.2f) };
        AddChild(_arm);
        Box(_arm, new Vector3(0.07f, 0.07f, 0.85f), new Vector3(0, 0, -0.4f), new Color(0.45f, 0.45f, 0.5f));
        var knobCol = (D.TryGetValue("mk", out var mk) ? mk : "") switch { "gate" => new Color(0.25f, 0.5f, 0.9f), "collapse" => new Color(0.9f, 0.25f, 0.2f), _ => new Color(0.9f, 0.7f, 0.15f) };
        var knob = Util.SphereMesh(0.1f, knobCol, 1.0f); knob.Position = new Vector3(0, 0, -0.85f); _arm.AddChild(knob);
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.7f, 1.2f, 0.7f) }, Position = new Vector3(0, 0.6f, 0) });
        BuildCable();
    }

    void BuildCable()
    {
        if (!D.ContainsKey("ax")) return;
        var chain = new Color(0.34f, 0.33f, 0.32f);
        var mechFloor = ToLocal(new Vector3(GF("fx"), Floor, GF("fz"))) + new Vector3(0, 0.05f, 0);
        var anchor = ToLocal(new Vector3(GF("ax"), GF("ay"), GF("az")));
        var start = new Vector3(0, 0.05f, 0.3f);
        var pts = new List<Vector3> { start, mechFloor, anchor };
        for (int s = 0; s < pts.Count - 1; s++)
        {
            var a = pts[s]; var b = pts[s + 1];
            Beam(this, a, b, 0.035f, new Color(0.12f, 0.11f, 0.1f));
            float len = a.DistanceTo(b); var dir = (b - a) / Mathf.Max(len, 0.01f);
            for (float d = 0; d < len - 0.15f; d += 0.38f) Beam(this, a + dir * d, a + dir * (d + 0.16f), 0.09f, chain, 0.15f);
        }
        Box(this, new Vector3(0.18f, 0.18f, 0.18f), anchor, new Color(0.9f, 0.45f, 0.1f), 0.8f);
        Box(this, new Vector3(0.14f, 0.14f, 0.14f), mechFloor, new Color(0.9f, 0.45f, 0.1f), 0.5f);
    }

    protected override void OnState(string old, string now)
    {
        if (_arm == null) return;
        float target = now == "ready" ? -0.6f : 0.6f;
        if (old == "") _arm.Rotation = new Vector3(target, 0, 0);
        else
        {
            CreateTween().TweenProperty(_arm, "rotation:x", target, 0.25f);
            if (now == "pulled") TrapSfx.Play(GlobalPosition, SfxNames.LeverClunk, 4f);
        }
    }

    public override string Prompt(Inventory inv) => State switch
    {
        "ready" => "E  Pull lever",
        "pulled" => "The lever is recharging",
        _ => "The lever is jammed",
    };

    ILeverMech Mech => Game.I.World.Objects.TryGetValue((int)GF("mid", -1), out var o) ? o as ILeverMech : null;

    public override void ServerInteract(ServerPlayer p)
    {
        if (State != "ready") return;
        var m = Mech;
        if (m == null || !m.CanActivate) return;
        SetState("pulled");
        float mech = GF("cd", TrapTuning.LeverCooldown);
        _t = mech;
        Util.Pass("lever_pulled", $"id={Id} by={p.Name} mech={D.GetValueOrDefault("mk")}");
        m.ServerActivate(p);
    }

    protected override void ServerTick(float dt)
    {
        if (State != "pulled") return;
        _t -= dt;
        var m = Mech;
        if (m is RockfallObj r && r.Flag("lv") && r.St == "rubble") { SetState("spent"); return; }
        if (_t <= 0 && m != null && m.CanActivate) SetState("ready");
    }
}

// ----------------------------------------------------------------------------- 9. Cuttable support (rope bridge support etc.)
/// <summary>A rope/support that a pickaxe hit severs. Server runs OnCut / Severed. Data: "p=x1,y1,z1,x2,y2,z2". (The grey-box cave has no bridge, so none is placed by default.)</summary>
public partial class CuttableObj : TrapBase
{
    public Action<ServerPlayer> OnCut;                       // server callback, set by whoever spawned it
    public static event Action<CuttableObj, ServerPlayer> Severed;
    Vector3 _a, _b;
    Node3D _rope;
    public override float InteractRange => 3.0f;

    public override void Build()
    {
        Prelude();
        CollisionLayer = 4; CollisionMask = 0;
        var v = (D.GetValueOrDefault("p", "0,0,0,0,0,0")).Split(',').Select(x => TrapUtil.ToF(x)).ToArray();
        _a = new Vector3(v[0], v[1], v[2]) - GlobalPosition; _b = new Vector3(v[3], v[4], v[5]) - GlobalPosition;
        _rope = new Node3D(); AddChild(_rope);
        var col = new Color(0.55f, 0.42f, 0.25f);
        Beam(_rope, _a, _b, 0.09f, col);
        float len = _a.DistanceTo(_b); var dir = (_b - _a) / Mathf.Max(len, 0.01f);
        for (float d = 0.2f; d < len; d += 0.5f) Beam(_rope, _a + dir * d, _a + dir * (d + 0.08f), 0.13f, new Color(0.4f, 0.3f, 0.18f));
        var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.35f, 0.35f, Mathf.Max(len, 0.1f)) } };
        cs.Transform = new Transform3D(Basis.LookingAt(dir, Mathf.Abs(dir.Y) > 0.99f ? Vector3.Right : Vector3.Up), (_a + _b) * 0.5f);
        AddChild(cs);
    }

    protected override void OnState(string old, string now)
    {
        if (_rope == null || now != "cut") return;
        if (old == "") { _rope.Visible = false; return; }
        TrapSfx.Play(GlobalPosition, SfxNames.RopeCut, 2f);
        CreateTween().TweenProperty(_rope, "position:y", -2.5f, 0.5f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        CollisionLayer = 0;
    }

    public override string Prompt(Inventory inv) => State == "intact" ? (Utilities.Get(inv.Held)?.IsPickaxe == true ? "LMB  Cut" : "Hold the pickaxe to cut") : null;

    public override void ServerHit(ServerPlayer p, float mining, bool pickaxe)
    {
        if (!pickaxe || State != "intact") return;
        SetState("cut");
        Util.Pass("cuttable_cut", $"id={Id} by={p.Name}");
        OnCut?.Invoke(p);
        Severed?.Invoke(this, p);
    }
}
