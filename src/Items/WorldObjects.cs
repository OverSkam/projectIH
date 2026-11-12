using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

static class ObjUtil
{
    public static bool Server => Net.I != null && Net.I.IsServer && Game.I != null && Game.I.World != null && Game.I.Phase == GamePhase.Round;
    public static float F(string s, float def = 0f) => float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : def;
    public static string Fmt(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    public static Vector3 DirVec(int dir) => dir switch { 0 => new Vector3(0, 0, 1), 1 => new Vector3(1, 0, 0), 2 => new Vector3(0, 0, -1), _ => new Vector3(-1, 0, 0) };
    public static Vector3 Flat(Vector3 v) => new Vector3(v.X, 0, v.Z);
}

// ----------------------------------------------------------------------------- Chest
/// <summary>Lootable chest. Contents are rolled on the server when opened (luck of the opener) and go straight into the opener's inventory.</summary>
public partial class ChestObj : NetObject
{
    public List<string> ForcedLoot;   // server only (tests)
    public int Depth = 1;
    Node3D _lid;
    bool _open;
    public override float InteractRange => 3.0f;

    public override void Build()
    {
        CollisionLayer = 1 | 4; CollisionMask = 0;
        var wood = new Color(0.45f, 0.28f, 0.12f);
        var band = new Color(0.75f, 0.6f, 0.15f);
        var body = Util.BoxMesh(new Vector3(1.1f, 0.55f, 0.7f), wood); body.Position = new Vector3(0, 0.275f, 0); AddChild(body);
        _lid = new Node3D { Position = new Vector3(0, 0.55f, 0.35f) };
        var lid = Util.BoxMesh(new Vector3(1.14f, 0.22f, 0.74f), wood); lid.Position = new Vector3(0, 0.11f, -0.35f); _lid.AddChild(lid);
        var lb = Util.BoxMesh(new Vector3(1.16f, 0.05f, 0.1f), band, 0.5f); lb.Position = new Vector3(0, 0.12f, -0.35f); _lid.AddChild(lb);
        AddChild(_lid);
        foreach (float x in new[] { -0.35f, 0.35f }) { var b = Util.BoxMesh(new Vector3(0.1f, 0.58f, 0.74f), band, 0.4f); b.Position = new Vector3(x, 0.28f, 0); AddChild(b); }
        var lockM = Util.BoxMesh(new Vector3(0.14f, 0.16f, 0.06f), new Color(1f, 0.85f, 0.2f), 1.5f); lockM.Position = new Vector3(0, 0.5f, -0.38f); AddChild(lockM);
        AddChild(Util.BoxShape(new Vector3(1.1f, 0.8f, 0.7f), new Vector3(0, 0.4f, 0)));
        AddChild(new OmniLight3D { LightColor = new Color(1f, 0.8f, 0.4f), LightEnergy = 0.7f, OmniRange = 4f, Position = new Vector3(0, 1f, -0.5f) });
    }

    public override void ApplyData(string data)
    {
        base.ApplyData(data);
        _open = Kv(data).GetValueOrDefault("open", "0") == "1";
        if (_lid != null) _lid.Rotation = new Vector3(_open ? -1.2f : 0f, 0, 0);
    }

    public override string Prompt(Inventory inv) => _open ? "Empty chest" : "E  Open chest";

    public override void ServerInteract(ServerPlayer p)
    {
        if (_open) return;
        Game.I.ServerSetObjectData(Id, "open=1");
        var rng = new RandomNumberGenerator(); rng.Randomize();
        var items = ForcedLoot ?? Loot.RollChest(rng, p.Stats.Luck, Depth, Game.I.Round);
        var names = new List<string>();
        foreach (var it in items)
        {
            names.Add(PickupObj.NameOf(it));
            if (!p.TryAdd(it)) ItemsServer.DropItem(GlobalPosition + new Vector3(rng.RandfRange(-0.6f, 0.6f), 1.0f, rng.RandfRange(-0.6f, 0.6f)), it);
        }
        Game.I.ServerNotice(p, "Chest: " + string.Join("  |  ", items.Select(ItemInfo.Line)));
        Util.Pass("chest_opened", $"opener={p.Name} loot={string.Join("+", items)} luck={p.Stats.Luck}");
    }
}

// ----------------------------------------------------------------------------- Shrine (stein trial pedestal)
public partial class ShrineObj : NetObject
{
    public const float BloodCost = 35f, TimedSeconds = 22f, DoorPeriod = 1.4f;
    string _type = "plate", _stein = "regen", _state = "locked";
    int _dir;
    Node3D _orb;
    public ShrinePlateObj Plate, Start;
    public ShrineBarrierObj Barrier;
    float _timeLeft, _cooldown, _cycle;
    bool _doorOpen;
    public override float InteractRange => 3.0f;

    public static string MakeData(string type, string stein, string state, int dir) => $"type={type};stein={stein};state={state};dir={dir}";

    void Parse()
    {
        var kv = Kv(Data);
        _type = kv.GetValueOrDefault("type", "plate"); _stein = kv.GetValueOrDefault("stein", "regen");
        _state = kv.GetValueOrDefault("state", "locked"); int.TryParse(kv.GetValueOrDefault("dir", "0"), out _dir);
    }

    public override void Build()
    {
        Parse();
        CollisionLayer = 1 | 4; CollisionMask = 0;
        var stone = new Color(0.32f, 0.3f, 0.3f);
        var ped = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.45f, BottomRadius = 0.6f, Height = 1.0f, RadialSegments = 8 }, MaterialOverride = Util.Mat(stone) };
        ped.Position = new Vector3(0, 0.5f, 0); AddChild(ped);
        var runeCol = _type == "blood" ? new Color(0.9f, 0.1f, 0.1f) : _type == "timed" ? new Color(1f, 0.75f, 0.1f) : new Color(0.2f, 0.6f, 1f);
        var ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.7f, OuterRadius = 0.82f, Rings = 16, RingSegments = 4 }, MaterialOverride = Util.Mat(runeCol, 2f) };
        ring.Position = new Vector3(0, 0.05f, 0); AddChild(ring);
        _orb = ItemVisuals.Stein(_stein); _orb.Position = new Vector3(0, 1.4f, 0); AddChild(_orb);
        AddChild(new OmniLight3D { LightColor = runeCol, LightEnergy = 1.0f, OmniRange = 7f, Position = new Vector3(0, 1.8f, 0) });
        AddChild(Util.BoxShape(new Vector3(1.0f, 1.1f, 1.0f), new Vector3(0, 0.55f, 0)));
    }

    public override void ApplyData(string data)
    {
        base.ApplyData(data);
        Parse();
        if (_orb != null) _orb.Visible = _state != "done";
    }

    string StName => Steins.Get(_stein)?.Name ?? _stein;
    public override string Prompt(Inventory inv)
    {
        if (_state == "done") return "The pedestal is empty.";
        switch (_type)
        {
            case "blood": return $"E  Pay {BloodCost:0} health for\n{ItemInfo.Full("stein:" + _stein)}";
            case "timed": return _state == "running" ? $"E  Take it now!\n{ItemInfo.Full("stein:" + _stein)}" : $"Locked: start the trial at the switch.\nIt holds: {ItemInfo.Full("stein:" + _stein)}";
            default: return _state == "open" ? $"E  Take\n{ItemInfo.Full("stein:" + _stein)}" : $"Locked: another miner must stand on the plate.\nIt holds: {ItemInfo.Full("stein:" + _stein)}";
        }
    }

    void SetState(string s)
    {
        _state = s;
        Game.I.ServerSetObjectData(Id, MakeData(_type, _stein, s, _dir));
        Start?.SetBusy(s == "running" || s == "done" || _cooldown > 0);
    }

    public override void ServerInteract(ServerPlayer p)
    {
        if (_state == "done") return;
        var rng = new RandomNumberGenerator(); rng.Randomize();
        if (_type == "blood")
        {
            if (p.Health <= BloodCost + 1f) { Game.I.ServerNotice(p, "You are too weak to pay the altar."); return; }
            p.Health -= BloodCost; p.Dirty = true;
            Award(p, rng);
        }
        else if (_type == "plate")
        {
            if (_state != "open") { Game.I.ServerNotice(p, "The pedestal is locked. Another miner must stand on the plate."); return; }
            if (Plate != null && ObjUtil.Flat(p.Pos - Plate.GlobalPosition).Length() < 1.3f) { Game.I.ServerNotice(p, "Not while you stand on the plate."); return; }
            Award(p, rng);
        }
        else
        {
            if (_state != "running") { Game.I.ServerNotice(p, "Start the trial at the switch first."); return; }
            Award(p, rng);
        }
    }

    void Award(ServerPlayer p, RandomNumberGenerator rng)
    {
        string st = Loot.LuckUpgrade(_stein, p.Stats.Luck, rng, Game.I.Round);
        ItemsServer.GiveStein(p, st);
        SetState("done");
        Game.I.ServerNotice(p, "You take: " + ItemInfo.Line("stein:" + st));
        if (Barrier != null) { Game.I.ServerDespawnObject(Barrier.Id); Barrier = null; }
        Util.Pass("shrine_taken", $"type={_type} stein={st} by={p.Name}");
    }

    public void ServerStartTrial(ServerPlayer p)
    {
        if (_type != "timed" || _state != "locked" || _cooldown > 0) return;
        _timeLeft = TimedSeconds; _cycle = 0; _doorOpen = true;
        Barrier?.SetOpen(true);
        SetState("running");
        Game.I.ServerAnnounce("A trial has begun in the dark...");
        Util.Pass("shrine_trial_started", $"by={p.Name}");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!ObjUtil.Server || _state == "done") return;
        float dt = (float)delta;
        if (_cooldown > 0) { _cooldown -= dt; if (_cooldown <= 0) Start?.SetBusy(false); }
        if (_type == "plate" && Plate != null)
        {
            string want = Plate.Pressed ? "open" : "locked";
            if (_state != want) SetState(want);
        }
        else if (_type == "timed" && _state == "running")
        {
            _timeLeft -= dt; _cycle += dt;
            bool open = ((int)(_cycle / DoorPeriod) % 2) == 0;
            if (open != _doorOpen) { _doorOpen = open; Barrier?.SetOpen(open); }
            if (_timeLeft <= 0)
            {
                _cooldown = 5f;
                Barrier?.SetOpen(false);
                SetState("locked");
                Util.Pass("shrine_trial_failed");
            }
        }
    }
}

/// <summary>Floor plate (plate trial) or start switch (timed trial) belonging to a shrine.</summary>
public partial class ShrinePlateObj : NetObject
{
    public ShrineObj Shrine;
    public bool Pressed;
    string _mode = "plate";
    bool _busy;
    MeshInstance3D _disc, _button;
    public override float InteractRange => 3.0f;

    public override void Build()
    {
        var kv = Kv(Data); _mode = kv.GetValueOrDefault("mode", "plate");
        if (_mode == "plate")
        {
            CollisionLayer = 0; CollisionMask = 0;
            _disc = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.95f, BottomRadius = 1.0f, Height = 0.1f, RadialSegments = 12 }, MaterialOverride = Util.Mat(new Color(0.25f, 0.3f, 0.4f)) };
            _disc.Position = new Vector3(0, 0.05f, 0); AddChild(_disc);
            AddChild(new OmniLight3D { LightColor = new Color(0.3f, 0.6f, 1f), LightEnergy = 0.6f, OmniRange = 4f, Position = new Vector3(0, 1f, 0) });
        }
        else
        {
            CollisionLayer = 1 | 4; CollisionMask = 0;
            var post = Util.BoxMesh(new Vector3(0.3f, 1.0f, 0.3f), new Color(0.3f, 0.28f, 0.25f)); post.Position = new Vector3(0, 0.5f, 0); AddChild(post);
            _button = Util.BoxMesh(new Vector3(0.22f, 0.12f, 0.22f), new Color(1f, 0.7f, 0.1f), 2f); _button.Position = new Vector3(0, 1.05f, 0); AddChild(_button);
            AddChild(Util.BoxShape(new Vector3(0.5f, 1.2f, 0.5f), new Vector3(0, 0.6f, 0)));
            AddChild(new OmniLight3D { LightColor = new Color(1f, 0.7f, 0.1f), LightEnergy = 0.8f, OmniRange = 5f, Position = new Vector3(0, 1.5f, 0) });
        }
    }

    public override void ApplyData(string data)
    {
        base.ApplyData(data);
        var kv = Kv(data);
        bool pressed = kv.GetValueOrDefault("pressed", "0") == "1";
        _busy = kv.GetValueOrDefault("busy", "0") == "1";
        if (_disc != null) _disc.MaterialOverride = Util.Mat(pressed ? new Color(0.3f, 0.8f, 1f) : new Color(0.25f, 0.3f, 0.4f), pressed ? 2f : 0f);
        if (_button != null) _button.MaterialOverride = Util.Mat(_busy ? new Color(0.3f, 0.2f, 0.1f) : new Color(1f, 0.7f, 0.1f), _busy ? 0f : 2f);
    }

    public override string Prompt(Inventory inv) => _mode == "start" ? (_busy ? "The trial is running." : "E  Start the trial") : null;

    public override void ServerInteract(ServerPlayer p)
    {
        if (_mode == "start") Shrine?.ServerStartTrial(p);
    }

    public void SetBusy(bool busy) { if (_mode == "start") Game.I.ServerSetObjectData(Id, $"mode=start;busy={(busy ? 1 : 0)}"); }

    public override void _PhysicsProcess(double delta)
    {
        if (_mode != "plate" || !ObjUtil.Server) return;
        bool pressed = false;
        foreach (var p in Game.I.Players.Values)
        {
            if (!p.Alive || !p.Participant) continue;
            if (ObjUtil.Flat(p.Pos - GlobalPosition).Length() < 1.0f && Math.Abs(p.Pos.Y - GlobalPosition.Y) < 1.5f) { pressed = true; break; }
        }
        if (pressed != Pressed)
        {
            Pressed = pressed;
            Game.I.ServerSetObjectData(Id, $"mode=plate;pressed={(pressed ? 1 : 0)}");
            Util.Pass(pressed ? "plate_pressed" : "plate_released");
        }
    }
}

/// <summary>Walled enclosure around a timed-trial pedestal. Its door cycles open/closed.</summary>
public partial class ShrineBarrierObj : NetObject
{
    CollisionShape3D _doorShape;
    MeshInstance3D _door;
    int _dir;
    bool _open;

    public override void Build()
    {
        var kv = Kv(Data); int.TryParse(kv.GetValueOrDefault("dir", "0"), out _dir);
        CollisionLayer = 1; CollisionMask = 0;
        RotationDegrees = new Vector3(0, _dir * 90f, 0);
        var wall = new Color(0.2f, 0.18f, 0.17f);
        const float H = 2.6f, T = 0.3f, R = 1.9f;
        void Seg(Vector3 c, Vector3 size)
        {
            var m = Util.BoxMesh(size, wall); m.Position = c; AddChild(m);
            AddChild(Util.BoxShape(size, c));
        }
        Seg(new Vector3(0, H / 2, -R), new Vector3(2 * R + T, H, T));
        Seg(new Vector3(-R, H / 2, 0), new Vector3(T, H, 2 * R));
        Seg(new Vector3(R, H / 2, 0), new Vector3(T, H, 2 * R));
        Seg(new Vector3(-(R + 0.8f) / 2f - 0.0f, H / 2, R), new Vector3(R - 0.8f, H, T));
        Seg(new Vector3((R + 0.8f) / 2f, H / 2, R), new Vector3(R - 0.8f, H, T));
        _door = Util.BoxMesh(new Vector3(1.6f, H, T), new Color(0.55f, 0.1f, 0.08f), 0.4f); _door.Position = new Vector3(0, H / 2, R); AddChild(_door);
        _doorShape = Util.BoxShape(new Vector3(1.6f, H, T), new Vector3(0, H / 2, R)); AddChild(_doorShape);
    }

    public override void ApplyData(string data)
    {
        base.ApplyData(data);
        _open = Kv(data).GetValueOrDefault("open", "0") == "1";
        if (_door != null) { _door.Visible = !_open; _doorShape.SetDeferred(CollisionShape3D.PropertyName.Disabled, _open); }
    }

    public void SetOpen(bool open) => Game.I.ServerSetObjectData(Id, $"open={(open ? 1 : 0)};dir={_dir}");
}

// ----------------------------------------------------------------------------- Dynamite
public partial class DynamiteObj : NetObject
{
    public ServerPlayer Thrower;   // server only
    float _fuse = 3f, _t;
    OmniLight3D _light;
    bool _done;

    public override void Build()
    {
        CollisionLayer = 0; CollisionMask = 0;
        _fuse = ObjUtil.F(Kv(Data).GetValueOrDefault("fuse", "3"), 3f);
        for (int i = 0; i < 3; i++)
        {
            var s = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.3f, RadialSegments = 8 }, MaterialOverride = Util.Mat(new Color(0.8f, 0.15f, 0.1f), 0.3f) };
            s.Position = new Vector3(-0.05f + i * 0.05f, 0.04f, 0); s.Rotation = new Vector3(0, 0, Mathf.Pi / 2f); AddChild(s);
        }
        AddChild(Util.SphereMesh(0.03f, new Color(1f, 0.8f, 0.2f), 4f));
        _light = new OmniLight3D { LightColor = new Color(1f, 0.6f, 0.2f), LightEnergy = 1.5f, OmniRange = 5f, Position = new Vector3(0, 0.2f, 0) };
        AddChild(_light);
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_light != null) _light.LightEnergy = 1f + Mathf.Abs(Mathf.Sin(_t * 14f)) * 2f;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!ObjUtil.Server || _done) return;
        _fuse -= (float)delta;
        if (_fuse > 0) return;
        _done = true;
        var pos = GlobalPosition + new Vector3(0, 0.3f, 0);
        Game.I.ServerDespawnObject(Id);
        Game.I.ServerExplode(pos, Thrower);
    }
}

// ----------------------------------------------------------------------------- Bear trap
public partial class BearTrapObj : NetObject
{
    double _armAt, _removeAt;
    bool _closed;
    Node3D _jawA, _jawB;

    public override void Build()
    {
        CollisionLayer = 0; CollisionMask = 0;
        var iron = new Color(0.22f, 0.21f, 0.2f);
        AddChild(Util.BoxMesh(new Vector3(0.7f, 0.04f, 0.5f), new Color(0.25f, 0.18f, 0.12f)));
        _jawA = new Node3D { Position = new Vector3(0, 0.02f, -0.22f) };
        _jawB = new Node3D { Position = new Vector3(0, 0.02f, 0.22f) };
        foreach (var (j, s) in new[] { (_jawA, 1f), (_jawB, -1f) })
        {
            var bar = Util.BoxMesh(new Vector3(0.62f, 0.03f, 0.04f), iron); bar.Position = new Vector3(0, 0, s * 0.2f); j.AddChild(bar);
            for (int i = 0; i < 6; i++) { var t = Util.BoxMesh(new Vector3(0.03f, 0.07f, 0.03f), iron); t.Position = new Vector3(-0.25f + i * 0.1f, 0.03f, s * 0.2f); j.AddChild(t); }
            AddChild(j);
        }
        _armAt = ItemsServer.Now + 1.0;
        SetJaws(false);
    }

    void SetJaws(bool closed)
    {
        _closed = closed;
        if (_jawA == null) return;
        _jawA.Rotation = new Vector3(closed ? 0f : -1.0f, 0, 0);
        _jawB.Rotation = new Vector3(closed ? 0f : 1.0f, 0, 0);
        _jawA.Position = new Vector3(0, 0.02f, closed ? -0.05f : -0.22f);
        _jawB.Position = new Vector3(0, 0.02f, closed ? 0.05f : 0.22f);
    }

    public override void ApplyData(string data)
    {
        base.ApplyData(data);
        SetJaws(Kv(data).GetValueOrDefault("armed", "1") == "0");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!ObjUtil.Server) return;
        double now = ItemsServer.Now;
        if (_closed) { if (_removeAt > 0 && now > _removeAt) { _removeAt = 0; Game.I.ServerDespawnObject(Id); } return; }
        if (now < _armAt) return;
        foreach (var p in Game.I.Players.Values.ToArray())
        {
            if (!p.Alive || !p.Participant || (Game.I.World.Cave.InHall(p.Pos) && !Util.HasArg("itest"))) continue;   // traps never bite inside the Hall (item test stages them there)
            if (ObjUtil.Flat(p.Pos - GlobalPosition).Length() < 0.8f && Math.Abs(p.Pos.Y - GlobalPosition.Y) < 1.2f)
            {
                Trigger();
                Game.I.ServerRootPlayer(p, 3f);
                p.TakeDamage(15f, null, "beartrap");
                Util.Pass("beartrap_triggered", $"victim={p.Name} hp={p.Health:0}");
                return;
            }
        }
        foreach (var o in Game.I.World.Objects.Values)
            if (o is IRootable r && ObjUtil.Flat(o.GlobalPosition - GlobalPosition).Length() < 1.0f)
            {
                Trigger();
                r.ServerRoot(3f);   // stuns a monster, never hurts it
                Util.Pass("beartrap_triggered", $"victim=monster:{o.Kind}");
                return;
            }
    }

    void Trigger()
    {
        _removeAt = ItemsServer.Now + 10;
        Game.I.ServerSetObjectData(Id, "armed=0");
        Game.ServerNoise(GlobalPosition, 10f, null);
    }
}

// ----------------------------------------------------------------------------- Rope
/// <summary>Climbable rope hanging from its origin (the anchor) down `len` metres. Anyone climbs it; a pickaxe hit cuts it.</summary>
public partial class RopeObj : NetObject
{
    public float Len = 3f;

    public override void Build()
    {
        Len = ObjUtil.F(Kv(Data).GetValueOrDefault("len", "3"), 3f);
        CollisionLayer = 4; CollisionMask = 0;   // not solid for miners (mask 1), but hit by attack rays
        var brown = new Color(0.6f, 0.45f, 0.2f);
        var rope = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = Len, RadialSegments = 6 }, MaterialOverride = Util.Mat(brown, 0.15f) };
        rope.Position = new Vector3(0, -Len / 2f, 0); AddChild(rope);
        AddChild(Util.BoxMesh(new Vector3(0.3f, 0.12f, 0.12f), new Color(0.3f, 0.2f, 0.1f)));
        for (float y = 0.5f; y < Len; y += 0.5f) { var k = Util.SphereMesh(0.06f, brown, 0.1f); k.Position = new Vector3(0, -y, 0); AddChild(k); }
        AddChild(Util.BoxShape(new Vector3(0.2f, Len, 0.2f), new Vector3(0, -Len / 2f, 0)));
    }

    public override string Prompt(Inventory inv) => "Rope: W climb, S down, Space let go. Pickaxe cuts it.";

    public override void ServerHit(ServerPlayer p, float mining, bool pickaxe)
    {
        if (!pickaxe) return;
        Game.I.ServerDespawnObject(Id);
        Game.ServerNoise(GlobalPosition, 6f, p);
        Util.Pass("rope_cut", $"by={p.Name}");
    }

    /// <summary>True if a body at feet position `pos` is within the climbing zone of this rope.</summary>
    public bool Contains(Vector3 pos)
    {
        var a = GlobalPosition;
        if (ObjUtil.Flat(pos - a).Length() > 0.75f) return false;
        return pos.Y >= a.Y - Len - 0.3f && pos.Y <= a.Y + 0.5f;
    }
    public float TopY => GlobalPosition.Y;
}

// ----------------------------------------------------------------------------- Test scaffolding
/// <summary>Debug-only raised platform (rope test). Data: "w=4;h=2.6".</summary>
public partial class DbgPlatformObj : NetObject
{
    public override void Build()
    {
        var kv = Kv(Data); float w = ObjUtil.F(kv.GetValueOrDefault("w", "4"), 4f), h = ObjUtil.F(kv.GetValueOrDefault("h", "2.6"), 2.6f);
        CollisionLayer = 1; CollisionMask = 0;
        var m = Util.BoxMesh(new Vector3(w, h, w), new Color(0.35f, 0.3f, 0.25f)); m.Position = new Vector3(0, h / 2f, 0); AddChild(m);
        AddChild(Util.BoxShape(new Vector3(w, h, w), new Vector3(0, h / 2f, 0)));
    }
}

/// <summary>Debug-only thin wall on the ThinWall layer (Ethereal test).</summary>
public partial class DbgThinWallObj : NetObject
{
    public override void Build()
    {
        CollisionLayer = Layers.ThinWall; CollisionMask = 0;
        var m = Util.BoxMesh(new Vector3(4f, 3f, 0.3f), new Color(0.4f, 0.5f, 0.6f), 0.3f); m.Position = new Vector3(0, 1.5f, 0); AddChild(m);
        AddChild(Util.BoxShape(new Vector3(4f, 3f, 0.3f), new Vector3(0, 1.5f, 0)));
    }
}
