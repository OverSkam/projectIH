using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// The bright chest (kind "bchest"): a metal-bound chest that glows in the dark. Hold E for 2.5 s to open it (interrupted by damage or by moving away).
/// Tiers: 0 wooden, 1 iron, 2 gilded. About 20% are mimics (never the chest nearest the Hall): identical while closed except that the glow breathes slowly
/// and a miner who stands still within 2 m hears faint breathing. Opening (or hitting) a mimic makes it snap: it bites and holds the miner for 4 s (30 damage,
/// cannot move, the lantern goes out), shrieks (huge noise), then releases, spits out 1-2 stones as bait, sprouts legs and scuttles away to settle elsewhere as a chest again.
/// A second miner hitting it during the bite frees the victim. Unkillable; dynamite / bear trap stun it 3 s.
/// Data: t=tier;s=closed|hold|open|bite|run|stun;mim=1 (the public tell);v=victim peer.
/// </summary>
public partial class GlowChestObj : NetObject, IRootable, IExplodable
{
    // ---- shared (every peer)
    public int Tier;
    public string State = "closed";
    public bool MimicTell;      // public tell flag (Data mim=1): the glow breathes
    public int VictimPeer;
    public override float InteractRange => 3.0f;

    // ---- server only
    public bool Mimic;
    public int Depth = 1;
    public List<string> ForcedLoot;
    public bool Rooted;   // (test) never relocates
    public bool Frozen;   // (staging) no server logic: stays in the staged state
    public float VisScale => _scale;
    ServerPlayer _opener, _victim;
    float _runYaw;
    float _holdT, _biteT, _biteTick, _popT, _stun, _noiseT, _moveSend, _runT;
    double _holdStartAt;
    string _stunBack = "closed";
    List<string> _pending = new(); List<Vector3> _pendingLand = new(); int _popIdx, _popTotal;
    List<Vector3> _path = new();
    Vector3 _biteSpot;
    readonly RandomNumberGenerator _rng = new();

    // ---- client visuals
    Node3D _lid, _model;
    OmniLight3D _light;
    MeshInstance3D _halo, _column, _rim, _inside, _tongue;
    readonly List<MeshInstance3D> _glowParts = new();
    readonly List<Node3D> _legs = new();
    CpuParticles3D _motes;
    string _shown = "closed";
    double _t, _stillT, _nextBreath, _nextScuttle;
    float _lidAngle, _baseLight, _baseHalo, _yawTarget;
    float _scale = 1f;
    Vector3 _renderPos;

    public bool Closed => State == "closed";
    public bool IsOpen => State == "open";
    public bool Busy => State is "bite" or "run" or "stun";

    public static string MakeData(int tier, string state, bool tell, int victim = 0) => $"t={tier};s={state};mim={(tell ? 1 : 0)};v={victim}";

    void Parse()
    {
        var kv = Kv(Data);
        if (kv.TryGetValue("t", out var t)) int.TryParse(t, out Tier);
        State = kv.GetValueOrDefault("s", "closed");
        MimicTell = kv.GetValueOrDefault("mim", "0") == "1";
        if (kv.TryGetValue("v", out var v)) int.TryParse(v, out VictimPeer);
    }

    // =================================================================== visuals
    public override void Build()
    {
        Parse();
        _rng.Randomize();
        CollisionLayer = 1 | 4; CollisionMask = 0;
        _scale = Tier switch { 0 => 1.0f, 1 => 1.1f, _ => 1.22f };
        _model = new Node3D { Scale = Vector3.One * _scale, Rotation = new Vector3(0, ((Id * 37) % 4) * Mathf.Pi / 2f + ((Id * 13) % 7 - 3) * 0.05f, 0) };
        AddChild(_model);
        Color wood = Tier switch { 0 => new Color(0.34f, 0.19f, 0.08f), 1 => new Color(0.26f, 0.28f, 0.33f), _ => new Color(0.7f, 0.52f, 0.12f) };
        Color band = Tier switch { 0 => new Color(0.18f, 0.17f, 0.16f), 1 => new Color(0.38f, 0.4f, 0.45f), _ => new Color(0.95f, 0.78f, 0.2f) };
        Color gold = new Color(1f, 0.78f, 0.22f);
        float seamE = Tier switch { 0 => 3.2f, 1 => 4.6f, _ => 6.5f };
        Color lightCol = Tier == 2 ? new Color(1f, 0.82f, 0.45f) : new Color(1f, 0.74f, 0.36f);

        var body = Util.BoxMesh(new Vector3(1.1f, 0.55f, 0.7f), wood, Tier == 2 ? 0.35f : 0f); body.Position = new Vector3(0, 0.275f, 0); _model.AddChild(body);
        foreach (float x in new[] { -0.4f, 0.4f })
        {
            var b = Util.BoxMesh(new Vector3(0.1f, 0.58f, 0.74f), band, Tier == 2 ? 0.8f : 0f); b.Position = new Vector3(x, 0.28f, 0); _model.AddChild(b);
        }
        // the hinge is at the back (+Z); the lid extends to the front (-Z)
        _lid = new Node3D { Position = new Vector3(0, 0.55f, 0.35f) };
        _model.AddChild(_lid);
        var lid = Util.BoxMesh(new Vector3(1.14f, 0.2f, 0.74f), wood, Tier == 2 ? 0.35f : 0f); lid.Position = new Vector3(0, 0.1f, -0.35f); _lid.AddChild(lid);
        var dome = Util.BoxMesh(new Vector3(1.06f, 0.08f, 0.62f), wood, 0f); dome.Position = new Vector3(0, 0.22f, -0.35f); _lid.AddChild(dome);
        foreach (float x in new[] { -0.4f, 0.4f })
        {
            var b = Util.BoxMesh(new Vector3(0.1f, 0.3f, 0.78f), band, Tier == 2 ? 0.8f : 0f); b.Position = new Vector3(x, 0.14f, -0.35f); _lid.AddChild(b);
        }
        // light leaking through the lid gap, gold bands and the keyhole: the seams that make it glow in the dark
        _rim = Util.BoxMesh(new Vector3(1.12f, 0.035f, 0.72f), gold, seamE); _rim.Position = new Vector3(0, 0.545f, 0); _model.AddChild(_rim); _glowParts.Add(_rim);
        foreach (float x in new[] { -0.4f, 0.4f })
        {
            var s = Util.BoxMesh(new Vector3(0.025f, 0.5f, 0.76f), gold, seamE * 0.8f); s.Position = new Vector3(x, 0.28f, 0); _model.AddChild(s); _glowParts.Add(s);
        }
        var lockPlate = Util.BoxMesh(new Vector3(0.18f, 0.2f, 0.05f), gold, seamE); lockPlate.Position = new Vector3(0, 0.5f, -0.38f); _model.AddChild(lockPlate); _glowParts.Add(lockPlate);
        var key = Util.BoxMesh(new Vector3(0.04f, 0.09f, 0.06f), new Color(1f, 0.95f, 0.7f), seamE * 1.6f); key.Position = new Vector3(0, 0.49f, -0.4f); _model.AddChild(key); _glowParts.Add(key);
        foreach (float x in new[] { -0.55f, 0.55f }) foreach (float z in new[] { -0.35f, 0.35f })
        {
            var c = Util.BoxMesh(new Vector3(0.1f, 0.12f, 0.1f), band, Tier == 0 ? 0f : 1f); c.Position = new Vector3(x, 0.06f, z); _model.AddChild(c);
        }
        // inside: a pool of light, seen when the lid is up
        _inside = Util.BoxMesh(new Vector3(1.0f, 0.06f, 0.6f), new Color(1f, 0.85f, 0.4f), 6f); _inside.Position = new Vector3(0, 0.52f, 0); _inside.Visible = false; _model.AddChild(_inside);

        AddChild(Util.BoxShape(new Vector3(1.15f * _scale, 0.8f * _scale, 0.75f * _scale), new Vector3(0, 0.4f * _scale, 0)));
        _baseLight = LootTuning.ChestLight[Tier];
        _light = new OmniLight3D { LightColor = lightCol, LightEnergy = _baseLight, OmniRange = LootTuning.ChestRange[Tier], Position = new Vector3(0, 0.95f * _scale, 0), ShadowEnabled = false };
        AddChild(_light);
        _baseHalo = Tier switch { 0 => 0.32f, 1 => 0.5f, _ => 0.72f };
        _halo = LootVis.Halo(lightCol, (Tier switch { 0 => 3.6f, 1 => 5.4f, _ => 7.5f }), _baseHalo, new Vector3(0, 0.55f * _scale, 0));
        AddChild(_halo);
        // a tall soft column of glow: the beacon that shows over a pillar or down a tunnel (always upright, faces the viewer)
        float colH = Tier switch { 0 => 3.6f, 1 => 4.8f, _ => 6.5f };
        _column = LootVis.Halo(lightCol, 1f, _baseHalo * 0.8f, new Vector3(0, colH * 0.45f, 0));
        ((QuadMesh)_column.Mesh).Size = new Vector2(colH * 0.32f, colH);
        ((StandardMaterial3D)_column.MaterialOverride).BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY;
        AddChild(_column);
        if (Tier > 0 && DisplayServer.GetName() != "headless")
        {
            _motes = new CpuParticles3D
            {
                Emitting = true, Amount = Tier == 2 ? 14 : 7, Lifetime = 2.8f, Direction = Vector3.Up, Spread = 20f, InitialVelocityMin = 0.15f, InitialVelocityMax = 0.4f,
                Gravity = Vector3.Zero, Position = new Vector3(0, 0.6f, 0), EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(0.45f, 0.1f, 0.3f),
                Mesh = new QuadMesh { Size = new Vector2(0.07f, 0.07f), Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = gold, AlbedoTexture = LootVis.Radial, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles } },
            };
            AddChild(_motes);
        }
        BuildMimicParts(wood);
        _shown = State;
        ApplyState(State, true);
    }

    void BuildMimicParts(Color wood)
    {
        // identical from outside while the chest is closed: teeth and legs only show once it moves or bites (hidden, so no tell in the mesh)
        var tooth = new Color(0.95f, 0.92f, 0.8f);
        for (int i = 0; i < 7; i++)
        {
            float x = -0.46f + i * 0.153f;
            var up = Util.BoxMesh(new Vector3(0.1f, 0.24f, 0.1f), tooth, 0.4f); up.Position = new Vector3(x, -0.02f, -0.7f); up.Rotation = new Vector3(0, 0, 0.0f); _lid.AddChild(up);
            var dn = Util.BoxMesh(new Vector3(0.1f, 0.22f, 0.1f), tooth, 0.4f); dn.Position = new Vector3(x + 0.07f, 0.62f, -0.36f); _model.AddChild(dn);
            up.Visible = dn.Visible = false; up.Name = "Tooth"; dn.Name = "Tooth";
        }
        _tongue = Util.BoxMesh(new Vector3(0.5f, 0.05f, 0.5f), new Color(0.75f, 0.1f, 0.14f), 1.5f); _tongue.Position = new Vector3(0, 0.5f, -0.05f); _tongue.Visible = false; _model.AddChild(_tongue);
        var legCol = new Color(0.14f, 0.09f, 0.06f);
        for (int i = 0; i < 6; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f; float z = -0.3f + (i / 2) * 0.3f;
            var pivot = new Node3D { Position = new Vector3(side * 0.5f, 0.2f, z), Visible = false };
            var thigh = Util.BoxMesh(new Vector3(0.3f, 0.06f, 0.06f), legCol); thigh.Position = new Vector3(side * 0.14f, 0.02f, 0); thigh.Rotation = new Vector3(0, 0, side * -0.4f);
            var shin = Util.BoxMesh(new Vector3(0.06f, 0.3f, 0.06f), legCol); shin.Position = new Vector3(side * 0.3f, -0.12f, 0);
            pivot.AddChild(thigh); pivot.AddChild(shin); _model.AddChild(pivot); _legs.Add(pivot);
        }
    }

    void SetMimicParts(bool teeth, bool legs)
    {
        foreach (var c in _lid.GetChildren()) if (c is MeshInstance3D m && m.Name == "Tooth") m.Visible = teeth;
        foreach (var c in _model.GetChildren()) if (c is MeshInstance3D m && m.Name == "Tooth") m.Visible = teeth;
        _tongue.Visible = teeth;
        foreach (var l in _legs) l.Visible = legs;
    }

    public override void ApplyData(string data)
    {
        base.ApplyData(data);
        Parse();
        if (_model == null) return;
        string old = _shown;   // (the server sets State before it replicates, so compare with what this peer last showed)
        _shown = State;
        ApplyState(State, false, old);
    }

    void ApplyState(string st, bool initial, string old = "closed")
    {
        bool changed = !initial && st != old;
        Vector3 pos = GlobalPosition + Vector3.Up * 0.5f;
        if (changed)
        {
            switch (st)
            {
                case "hold": Sfx.Play(SfxNames.ChestCreak, pos); break;
                case "open":
                    Sfx.Play(SfxNames.ChestBurst, pos);
                    LootFx.ChestBurst(this);
                    Util.Pass("bchest_open_seen", $"id={Id} tier={Tier}");
                    break;
                case "bite":
                    Sfx.Play(SfxNames.MimicSnap, pos); Sfx.Play(SfxNames.MimicShriek, pos);
                    LootFx.Shake(GlobalPosition, 18f, 0.09f, 0.9f);
                    Util.Pass("mimic_bite_seen", $"id={Id} victim={VictimPeer}");
                    if (VictimPeer == Net.I.MyId) { Loot2Ui.I?.Flash(new Color(0.8f, 0.05f, 0.05f), 0.5f, 0.6f); LootFx.Shake(GlobalPosition, 99f, 0.12f, 1.0f); }
                    break;
                case "run": Util.Pass("mimic_run_seen", $"id={Id}"); break;
                case "closed" when old is "run" or "stun": Util.Pass("mimic_settled_seen", $"id={Id}"); break;
            }
        }
        SetMimicParts(st == "bite", st is "run" or "stun");
        _inside.Visible = st == "open";
        if (st == "bite") _inside.MaterialOverride = Util.Mat(new Color(1f, 0.08f, 0.06f), 7f);
        else if (st == "open") _inside.MaterialOverride = Util.Mat(new Color(1f, 0.85f, 0.4f), 6f);
    }

    public override string Prompt(Inventory inv)
    {
        if (State == "closed") return "E  Hold to open the chest";
        if (State == "hold") return "E  Hold to open the chest";
        if (State == "open") return "Empty chest";
        return null;
    }

    public override void _Process(double delta)
    {
        if (_model == null) return;
        float dt = (float)delta; _t += dt;
        float pulse = 1f; float haloPulse = 1f;
        if (MimicTell && State == "closed")
        {   // the tell: the glow slowly breathes (period ~4 s). Real chests burn steady.
            float b = 0.5f + 0.5f * Mathf.Sin((float)_t * 1.55f);
            pulse = 0.72f + 0.45f * b; haloPulse = 0.6f + 0.8f * b;
        }
        float lidTarget = 0f, lightMul = 1f;
        switch (State)
        {
            case "hold":
                lidTarget = 0.03f + 0.02f * Mathf.Sin((float)_t * 40f); lightMul = 1.3f + 0.2f * Mathf.Sin((float)_t * 9f);
                _model.Position = new Vector3(0.012f * Mathf.Sin((float)_t * 55f), 0, 0);
                break;
            case "open": lidTarget = -1.95f; lightMul = 0.32f; _model.Position = Vector3.Zero; break;
            case "bite":
                lidTarget = -(0.55f + 0.95f * Mathf.Abs(Mathf.Sin((float)_t * 7.5f)));
                lightMul = 2.4f + 1.2f * Mathf.Sin((float)_t * 20f);
                _model.Position = new Vector3(0.02f * Mathf.Sin((float)_t * 45f), 0.04f * Mathf.Abs(Mathf.Sin((float)_t * 7.5f)), 0.02f * Mathf.Cos((float)_t * 38f));
                break;
            case "run":
                lidTarget = -0.18f + 0.12f * Mathf.Sin((float)_t * 18f); lightMul = 0.9f;
                _model.Position = new Vector3(0, 0.07f + 0.04f * Mathf.Abs(Mathf.Sin((float)_t * 14f)), 0);
                for (int i = 0; i < _legs.Count; i++) _legs[i].Rotation = new Vector3(0.5f * Mathf.Sin((float)_t * 16f + i * 1.7f), 0, 0.08f * Mathf.Sin((float)_t * 16f + i));
                if (_t > _nextScuttle) { _nextScuttle = _t + 0.7; Sfx.Play(SfxNames.MimicScuttle, GlobalPosition + Vector3.Up * 0.3f, -3f); }
                break;
            case "stun":
                lidTarget = -0.25f; lightMul = 0.25f + 0.5f * Mathf.Abs(Mathf.Sin((float)_t * 12f));
                _model.Position = new Vector3(0.03f * Mathf.Sin((float)_t * 30f), 0, 0);
                break;
            default: _model.Position = Vector3.Zero; break;
        }
        _lidAngle = Mathf.Lerp(_lidAngle, lidTarget, Mathf.Min(1f, dt * (State == "open" ? 5f : State == "bite" ? 28f : 12f)));
        _lid.Rotation = new Vector3(_lidAngle, 0, 0);
        float e = _baseLight * lightMul * pulse;
        _light.LightEnergy = e;
        if (State == "bite") _light.LightColor = new Color(1f, 0.15f, 0.1f); else if (State != "open") _light.LightColor = Tier == 2 ? new Color(1f, 0.82f, 0.45f) : new Color(1f, 0.74f, 0.36f);
        LootVis.SetAlpha(_halo, _baseHalo * haloPulse * (State == "open" ? 0.4f : 1f) * (State == "bite" ? 1.2f : 1f));
        LootVis.SetAlpha(_column, _baseHalo * 0.8f * haloPulse * (State == "open" ? 0.25f : 1f));
        if (State == "run" && _renderPos != Vector3.Zero)
        {
            var d = _renderPos - GlobalPosition;
            if (d.LengthSquared() > 0.0004f) GlobalPosition = GlobalPosition.Lerp(_renderPos, Mathf.Min(1f, dt * 14f));
        }
        _model.Rotation = new Vector3(0, Mathf.LerpAngle(_model.Rotation.Y, _yawTarget != 0 ? _yawTarget : _model.Rotation.Y, Mathf.Min(1f, dt * 8f)), 0);
        BreathTell(dt);
    }

    /// <summary>The tell: a miner who stands still within 2 m of a mimic hears faint breathing.</summary>
    void BreathTell(float dt)
    {
        if (!MimicTell || State != "closed") { _stillT = 0; return; }
        var lm = Game.I?.LocalMiner;
        if (lm == null || !GodotObject.IsInstanceValid(lm)) return;
        bool near = lm.GlobalPosition.DistanceTo(GlobalPosition) < LootTuning.BreathRange;
        float speed = new Vector2(lm.Velocity.X, lm.Velocity.Z).Length();
        _stillT = near && speed < 0.3f ? _stillT + dt : 0f;
        if (_stillT > 1.0f && _t > _nextBreath)
        {
            _nextBreath = _t + 3.0;
            Sfx.Play(SfxNames.MimicBreath, GlobalPosition + Vector3.Up * 0.4f);
            Util.Pass("mimic_breath_heard", $"id={Id}");
        }
    }

    /// <summary>Client: a position update while the mimic scuttles.</summary>
    public void ClientMove(Vector3 pos, float yaw) { _renderPos = pos; _yawTarget = yaw; if (State != "run") GlobalPosition = pos; }
    public void ClientSettle(Vector3 pos, float yaw) { _renderPos = Vector3.Zero; GlobalPosition = pos; }

    // =================================================================== server
    void SetState(string s, int victim = 0)
    {
        State = s; VictimPeer = victim;
        Game.I.ServerSetObjectData(Id, MakeData(Tier, s, Mimic, victim));
    }

    public void ServerMakeMimic(bool on)
    {
        Mimic = on;
        Game.I.ServerSetObjectData(Id, MakeData(Tier, State, on, VictimPeer));
    }

    public override void ServerInteract(ServerPlayer p)
    {
        // a tap does nothing: the chest needs the 2.5 s hold (RequestChestHold)
        if (Closed) Game.I.ServerNotice(p, "Hold E to open the chest.");
    }

    public bool ServerStartHold(ServerPlayer p)
    {
        if (!Closed || _stun > 0f || _opener != null || !Game.I.CanAct(p) || ObjUtil.Flat(p.Pos - GlobalPosition).Length() > LootTuning.HoldReach + 0.8f)
        {
            Game.I.RpcId(p.Peer, nameof(Game.ClientChestHold), Id, 0);   // refused: the client's bar must not hang
            return false;
        }
        _opener = p; _holdT = 0; _holdStartAt = Time.GetTicksMsec() / 1000.0; _noiseT = 0;
        SetState("hold");
        Game.ServerNoise(GlobalPosition, 9f, p);
        Game.I.RpcId(p.Peer, nameof(Game.ClientChestHold), Id, 1);
        Util.Pass("bchest_hold_started", $"id={Id} by={p.Name} tier={Tier}");
        return true;
    }

    public void ServerStopHold(ServerPlayer p, string why)
    {
        if (_opener == null || _opener != p) return;
        _opener = null; _holdT = 0;
        SetState("closed");
        Game.I.RpcId(p.Peer, nameof(Game.ClientChestHold), Id, 0);
        Util.Pass("bchest_hold_cancelled", $"id={Id} by={p.Name} why={why}");
    }

    public float HoldProgress => _opener != null ? _holdT / LootTuning.HoldSeconds : 0f;

    public override void ServerHit(ServerPlayer p, float mining, bool pickaxe)
    {
        if (Mimic && (State is "closed" or "hold"))
        {
            if (_opener != null) { var o = _opener; ServerStopHold(o, "mimic_hit"); }
            Snap(p, "hit");
        }
        else if (State == "bite" && _victim != null && p != _victim)
        {
            Util.Pass("mimic_victim_freed", $"id={Id} victim={_victim.Name} by={p.Name}");
            Release("freed");
        }
        else if (State == "closed") Game.ServerNoise(GlobalPosition, 6f, p);
    }

    void Snap(ServerPlayer p, string how)
    {
        _victim = p; _biteT = 0; _biteTick = 0.35f; _biteSpot = GlobalPosition;
        SetState("bite", p.Peer);
        Game.I.ServerRootPlayer(p, LootTuning.BiteSeconds);
        p.Items.LanternOutUntil = Time.GetTicksMsec() / 1000.0 + LootTuning.BiteSeconds + 0.2;
        Game.ServerNoise(GlobalPosition, LootTuning.ShriekNoise, null);
        LootStats.Shriek(LootTuning.ShriekNoise);
        Util.Pass("mimic_bite", $"id={Id} victim={p.Name} how={how} noise={LootTuning.ShriekNoise:0}");
    }

    void Release(string why)
    {
        var v = _victim; _victim = null;
        if (v != null) Game.I.ServerRootPlayer(v, 0.01f);
        if (v != null) v.Items.LanternOutUntil = 0.0001;   // the lantern comes back as soon as the jaws open
        // bait: 1-2 stones spat out where it sat
        var items = LootRolls.RollBait(_rng);
        var land = LootPlacement.Landings(Game.I.World, _biteSpot, v != null ? (v.Pos - _biteSpot) with { Y = 0 } : null, items.Count, 0.9f, 1.8f, _rng);
        for (int i = 0; i < items.Count; i++)
        {
            int pid = Game.I.ServerSpawnObject("pickup", land[i], items[i]);
            Game.I.Rpc(nameof(Game.ClientChestPop), Id, i, items.Count, pid, land[i], items[i]);
        }
        Util.Pass("mimic_released", $"id={Id} why={why} bait={items.Count}");
        StartRun();
    }

    void StartRun()
    {
        _path.Clear(); _runT = 0;
        var dest = Rooted ? null : PickSpot();
        if (dest == null) { Settle(); return; }
        _path = Game.I.World.Cave.FindPath(GlobalPosition, dest.Value);
        if (_path.Count < 2) { Settle(); return; }
        SetState("run");
    }

    /// <summary>A free floor spot (nav node with open space all round) 10-45 m from here, outside the Hall and away from other chests.</summary>
    Vector3? PickSpot()
    {
        var cave = Game.I.World.Cave; var w = Game.I.World;
        var others = w.Objects.Values.Where(o => o != this && (o is GlowChestObj || o is GeodeObj || o is ShrineObj)).Select(o => o.GlobalPosition).ToList();
        for (int tries = 0; tries < 60; tries++)
        {
            int i = _rng.RandiRange(0, cave.NavNodes.Count - 1);
            var p = cave.NavNodes[i];
            float d = ObjUtil.Flat(p - GlobalPosition).Length();
            if (d < 10f || d > 45f || cave.InHall(p) || cave.InHall(p + Vector3.Up)) continue;
            if (others.Any(o => ObjUtil.Flat(o - p).Length() < 5f)) continue;
            if (!LootPlacement.Clear(w, p + new Vector3(0, 0.05f, 0), 1.0f)) continue;
            return p;
        }
        return null;
    }

    void Settle()
    {
        var cave = Game.I.World.Cave;
        var room = cave.RoomAt(GlobalPosition);
        Depth = room?.Depth ?? Depth;
        _path.Clear();
        Game.I.Rpc(nameof(Game.ClientChestSettle), Id, GlobalPosition, 0f);
        SetState("closed");
        Util.Pass("mimic_settled", $"id={Id} pos={GlobalPosition.X:0.0},{GlobalPosition.Z:0.0}");
    }

    public void ServerRoot(float seconds) => Stun(LootTuning.MimicStun);
    public void OnExplosion(Vector3 center, float radius, float damage) => Stun(LootTuning.MimicStun);

    void Stun(float seconds)
    {
        if (!Mimic || !ObjUtil.Server || State == "open" || State == "stun") return;
        if (_opener != null) ServerStopHold(_opener, "stunned");
        string back = State;
        if (State == "bite") { Release("stunned"); back = State; }
        _stunBack = back == "stun" ? "closed" : back; _stun = seconds;
        SetState("stun");
        Util.Pass("mimic_stunned", $"id={Id} secs={seconds:0.#} back={_stunBack}");
    }

    /// <summary>Staging only (--l2stage=openchest:tier@...): opens the chest as if p held E for 2.5 s.</summary>
    public void ServerDebugOpen(ServerPlayer p) => Open(p);

    void Open(ServerPlayer p)
    {
        SetState("open");
        var g = Game.I;
        Game.ServerNoise(GlobalPosition, LootTuning.ChestOpenNoise[Tier], p);
        var items = ForcedLoot ?? LootRolls.RollChest(_rng, Tier, Depth, g.Round, p.Stats.Luck);
        var land = LootPlacement.Landings(g.World, GlobalPosition, (p.Pos - GlobalPosition) with { Y = 0 }, items.Count, 0.9f, 1.9f, _rng);
        _pending = items; _pendingLand = land; _popIdx = 0; _popTotal = items.Count; _popT = 0.55f;
        Util.Pass("bchest_opened", $"id={Id} tier={Tier} opener={p.Name} items={string.Join("+", items)} luck={p.Stats.Luck}");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!ObjUtil.Server || Frozen) return;
        float dt = (float)delta;
        var g = Game.I;
        if (_stun > 0f)
        {
            _stun -= dt;
            if (_stun <= 0f)
            {
                if (_stunBack == "run") { if (_path.Count > 1) SetState("run"); else Settle(); }
                else SetState("closed");
            }
            return;
        }
        switch (State)
        {
            case "hold":
                {
                    var p = _opener;
                    if (p == null) { SetState("closed"); break; }
                    if (!g.Players.ContainsKey(p.Peer) || !g.CanAct(p)) { ServerStopHold(p, "cannot_act"); break; }
                    if (p.LastDamageAt > _holdStartAt) { ServerStopHold(p, "damaged"); break; }
                    if (ObjUtil.Flat(p.Pos - GlobalPosition).Length() > LootTuning.HoldReach) { ServerStopHold(p, "moved_away"); break; }
                    _holdT += dt; _noiseT -= dt;
                    if (_noiseT <= 0) { _noiseT = 0.9f; Game.ServerNoise(GlobalPosition, 8f, p); }
                    if (_holdT >= LootTuning.HoldSeconds)
                    {
                        _opener = null; _holdT = 0;
                        g.RpcId(p.Peer, nameof(Game.ClientChestHold), Id, 2);
                        if (Mimic) Snap(p, "hold_done"); else Open(p);
                    }
                    break;
                }
            case "open":
                if (_popIdx < _pending.Count)
                {
                    _popT -= dt;
                    if (_popT <= 0)
                    {
                        string item = _pending[_popIdx]; var land = _pendingLand[_popIdx];
                        int pid = g.ServerSpawnObject("pickup", land, item);
                        if (item.StartsWith("stein:"))
                        {
                            g.Rpc(nameof(Game.ClientLootBurst), GlobalPosition + new Vector3(0, 0.5f, 0), 3, LootTuning.ColorOfItem(item), new[] { pid }, new[] { item }, new[] { land });
                            Game.ServerNoise(GlobalPosition, LootTuning.RevealNoise[3], null);
                            LootStats.Revealed(3, new List<string> { item }, LootTuning.RevealNoise[3]);
                        }
                        else g.Rpc(nameof(Game.ClientChestPop), Id, _popIdx, _popTotal, pid, land, item);
                        _popIdx++; _popT = LootTuning.PopInterval;
                        if (_popIdx >= _pending.Count) Util.Pass("bchest_items_out", $"id={Id} n={_pending.Count}");
                    }
                }
                break;
            case "bite":
                {
                    var v = _victim;
                    if (v == null || !g.Players.ContainsKey(v.Peer) || !v.Alive || v.Downed) { Release(v == null ? "gone" : "victim_down"); break; }
                    _biteT += dt; _biteTick -= dt;
                    if (_biteTick <= 0)
                    {
                        _biteTick = LootTuning.BiteTick;
                        float d = v.TakeDamage(LootTuning.BiteDamage * LootTuning.BiteTick / LootTuning.BiteSeconds, null, "monster");
                        LootStats.BiteDamage += d;
                    }
                    if (_biteT >= LootTuning.BiteSeconds) Release("done");
                    break;
                }
            case "run":
                {
                    _runT += dt;
                    float step = LootTuning.MimicSpeed * dt;
                    var pos = GlobalPosition;
                    while (step > 0f && _path.Count > 0)
                    {
                        var wp = _path[0];
                        var to = new Vector3(wp.X - pos.X, 0, wp.Z - pos.Z);
                        float len = to.Length();
                        if (len <= step) { pos = new Vector3(wp.X, wp.Y, wp.Z); step -= len; _path.RemoveAt(0); }
                        else { pos += to / len * step; pos.Y = Mathf.Lerp(pos.Y, wp.Y, 0.2f); step = 0; }
                    }
                    var move = pos - GlobalPosition;
                    GlobalPosition = pos;
                    _moveSend -= dt;
                    if (move.LengthSquared() > 1e-8f) _runYaw = Mathf.Atan2(-move.X, -move.Z);
                    if (_moveSend <= 0) { _moveSend = 0.08f; g.Rpc(nameof(Game.ClientChestMove), Id, pos, _runYaw); }
                    if (_path.Count == 0 || _runT > 25f) Settle();
                    break;
                }
        }
    }
}
