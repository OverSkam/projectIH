using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Where loot lands: floor spots around an origin that are reachable in a straight line (no wall in between).</summary>
public static class LootPlacement
{
    /// <summary>n landing points (floor level + 0.4, the height of loose pickups) around origin; the fan opens towards prefDir (flat) when given. Server only (needs the physics space).</summary>
    public static List<Vector3> Landings(World w, Vector3 origin, Vector3? prefDir, int n, float minR = 0.7f, float maxR = 1.6f, RandomNumberGenerator rng = null)
    {
        rng ??= new RandomNumberGenerator { Seed = (ulong)(origin.X * 1000 + origin.Z * 7919 + n) };
        var res = new List<Vector3>();
        var space = w.GetWorld3D().DirectSpaceState;
        float baseA = prefDir.HasValue && prefDir.Value.LengthSquared() > 0.01f ? Mathf.Atan2(prefDir.Value.X, prefDir.Value.Z) : rng.Randf() * Mathf.Tau;
        for (int tries = 0; tries < 60 && res.Count < n; tries++)
        {
            float spread = prefDir.HasValue ? 1.9f : Mathf.Pi;
            float a = baseA + rng.RandfRange(-spread, spread);
            float r = rng.RandfRange(minR, maxR) * (tries > 30 ? 0.6f : 1f);
            var c = origin + new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r);
            var q = Queries.Ray(origin + new Vector3(0, 0.5f, 0), c + new Vector3(0, 0.5f, 0), 1);
            if (space.IntersectRay(q).Count > 0) continue;
            var down = Queries.Ray(c + new Vector3(0, 1.0f, 0), c + new Vector3(0, -1.6f, 0), 1);
            var hit = space.IntersectRay(down);
            if (hit.Count == 0) continue;
            var fp = hit["position"].AsVector3();
            if (Math.Abs(fp.Y - origin.Y) > 1.2f) continue;
            var p = new Vector3(c.X, fp.Y + 0.4f, c.Z);
            if (res.Any(o => new Vector2(o.X - p.X, o.Z - p.Z).Length() < 0.42f)) continue;
            res.Add(p);
        }
        while (res.Count < n) res.Add(origin + new Vector3(rng.RandfRange(-0.3f, 0.3f), 0.4f, rng.RandfRange(-0.3f, 0.3f)));
        return res;
    }

    /// <summary>A free floor spot for a relocating mimic / bait: open all round (no wall within 1 m) and not in the Hall.</summary>
    public static bool Clear(World w, Vector3 p, float radius = 1.0f)
    {
        var space = w.GetWorld3D().DirectSpaceState;
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Tau / 8f;
            var from = p + new Vector3(0, 0.6f, 0);
            var q = Queries.Ray(from, from + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius, 1);
            if (space.IntersectRay(q).Count > 0) return false;
        }
        return true;
    }
}

// ----------------------------------------------------------------------------- Geode
/// <summary>A sealed geode on the floor. Cracked in place with the pickaxe in 3 hits: each hit louder, with more light leaking through growing cracks.
/// The result is rolled by the server on the last hit (the cracker's Luck counts) and never leaves the server before that.
/// Data: size=0|1|2;hits=0..2. Server-only: Depth, ForcedResult (tests).</summary>
public partial class GeodeObj : NetObject
{
    public int Size, Hits;
    public int Depth = 1;
    public GeodeResult ForcedResult;      // server only (tests / staging)
    double _lastHit;
    Node3D _seamRoot, _shell;
    readonly List<MeshInstance3D>[] _seams = { new(), new() };
    OmniLight3D _glow;
    MeshInstance3D _halo;
    public override float InteractRange => 3.2f;

    public static string MakeData(int size, int hits) => $"size={size};hits={hits}";
    public static float RadiusOf(int size) => size switch { 0 => 0.27f, 1 => 0.38f, _ => 0.5f };

    void Parse()
    {
        var kv = Kv(Data);
        if (kv.TryGetValue("size", out var s)) int.TryParse(s, out Size);
        if (kv.TryGetValue("hits", out var h)) int.TryParse(h, out Hits);
    }

    public override void Build()
    {
        Parse();
        CollisionLayer = 1 | 4; CollisionMask = 0;
        float r = RadiusOf(Size);
        var rng = new RandomNumberGenerator { Seed = (ulong)(Id * 2654435761u + 5) };
        _shell = new Node3D { Position = new Vector3(0, r * 0.82f, 0), Rotation = new Vector3(0, rng.Randf() * Mathf.Tau, 0) };
        AddChild(_shell);
        var rock = new Color(0.26f, 0.22f, 0.19f);
        var body = new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r * 1.7f, RadialSegments = 7, Rings = 4 }, MaterialOverride = Util.Mat(rock) };
        _shell.AddChild(body);
        for (int i = 0; i < 4; i++)   // rough bumps so it reads as a lump of stone, not a ball
        {
            var b = Util.BoxMesh(new Vector3(r * rng.RandfRange(0.4f, 0.7f), r * rng.RandfRange(0.3f, 0.5f), r * rng.RandfRange(0.4f, 0.6f)), rock.Lerp(new Color(0.4f, 0.34f, 0.3f), rng.Randf() * 0.6f));
            var d = new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(-0.2f, 1), rng.RandfRange(-1, 1)).Normalized();
            b.Position = d * r * 0.78f; b.Rotation = new Vector3(rng.Randf() * 3, rng.Randf() * 3, rng.Randf() * 3);
            _shell.AddChild(b);
        }
        // seams: two sets, visible after hit 1 and hit 2 (the second set is longer and wider). Colour is neutral warm white: it must not hint at the result.
        _seamRoot = new Node3D(); _shell.AddChild(_seamRoot);
        var seamCol = new Color(1f, 0.66f, 0.24f);
        for (int set = 0; set < 2; set++)
            for (int i = 0; i < 5; i++)
            {
                var dir = new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(-0.35f, 1f), rng.RandfRange(-1, 1)).Normalized();
                var pivot = new Node3D { Basis = Basis.LookingAt(dir, Vector3.Up) * new Basis(Vector3.Back, rng.Randf() * Mathf.Tau) };
                float len = r * (set == 0 ? rng.RandfRange(0.6f, 0.9f) : rng.RandfRange(1.0f, 1.5f));
                float wid = 0.03f + set * 0.03f + r * 0.05f;
                var seam = Util.BoxMesh(new Vector3(len, wid, 0.06f), seamCol, 1.5f + set * 1.0f);
                seam.Position = new Vector3(0, 0, -r * 0.86f);
                pivot.AddChild(seam);
                _seamRoot.AddChild(pivot);
                _seams[set].Add(seam);
                seam.Visible = false;
            }
        AddChild(Util.BoxShape(new Vector3(r * 2f, r * 1.8f, r * 2f), new Vector3(0, r * 0.9f, 0)));
        _glow = new OmniLight3D { Position = new Vector3(0, r * 1.1f, 0), LightColor = new Color(1f, 0.82f, 0.5f), LightEnergy = 0f, OmniRange = 2.5f, ShadowEnabled = false, Visible = false };
        AddChild(_glow);
        _halo = LootVis.Halo(new Color(1f, 0.82f, 0.5f), r * 5f, 0.0f, new Vector3(0, r * 0.9f, 0));
        _halo.Visible = false;
        AddChild(_halo);
        Show(Hits, false);
    }

    void Show(int hits, bool flash)
    {
        float r = RadiusOf(Size);
        for (int s = 0; s < 2; s++) foreach (var m in _seams[s]) m.Visible = hits > s;
        _glow.Visible = hits > 0;
        _glow.LightEnergy = hits switch { 0 => 0f, 1 => 1.2f + Size * 0.4f, _ => 3.4f + Size * 1.0f };
        _glow.OmniRange = hits switch { 0 => 2f, 1 => 3.6f, _ => 6f } + Size * 0.6f;
        _halo.Visible = hits > 0;
        LootVis.SetAlpha(_halo, hits switch { 0 => 0f, 1 => 0.3f, _ => 0.62f });
        if (!flash || hits <= 0) return;
        // the hit: a jolt, a spike of light, dust chips
        var tw = CreateTween();
        _shell.Scale = Vector3.One * 1.12f;
        tw.TweenProperty(_shell, "scale", Vector3.One, 0.18f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        float peak = _glow.LightEnergy;
        _glow.LightEnergy = peak * 3.5f;
        tw.Parallel().TweenProperty(_glow, "light_energy", peak, 0.3f);
        LootVis.Sparks(this, new Vector3(0, r * 1.1f, 0), new Color(1f, 0.9f, 0.7f), 8 + hits * 6, 2.2f + hits * 0.8f, 0.5f, 0.07f, 9f);
    }

    public override void ApplyData(string data)
    {
        int old = Hits;
        base.ApplyData(data);
        Parse();
        if (_shell == null) return;
        Show(Hits, Hits > old);
        if (Hits > old && Hits <= LootTuning.HitsToCrack - 1)
        {
            string snd = Hits == 1 ? SfxNames.GeodeHit1 : SfxNames.GeodeHit2;
            Sfx.Play(snd, GlobalPosition + Vector3.Up * 0.3f);
            Util.Pass("geode_hit_seen", $"id={Id} hits={Hits}");
        }
    }

    public override string Prompt(Inventory inv)
    {
        bool pick = Utilities.Get(inv.Held)?.IsPickaxe == true;
        string state = Hits == 0 ? "" : $"  ({Hits}/{LootTuning.HitsToCrack})";
        return pick ? "LMB  Crack the geode" + state : "Hold the pickaxe (1/2) and crack the geode" + state;
    }

    public override void ServerHit(ServerPlayer p, float mining, bool pickaxe)
    {
        if (!pickaxe) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (now - _lastHit < 0.25) return;
        _lastHit = now;
        Parse();
        Hits++;
        Game.ServerNoise(GlobalPosition, LootTuning.HitNoise[Math.Min(Hits, 3) - 1], p);
        if (Hits < LootTuning.HitsToCrack) { Game.I.ServerSetObjectData(Id, MakeData(Size, Hits)); return; }
        Crack(p);
    }

    /// <summary>Last hit: roll, spawn the stones, announce the reveal (burst on every peer), make noise by rarity.</summary>
    public void Crack(ServerPlayer p)
    {
        var g = Game.I;
        var rng = new RandomNumberGenerator(); rng.Randomize();
        var res = ForcedResult ?? LootRolls.RollGeode(rng, Size, Depth, g.Round, p?.Stats.Luck ?? 0f);
        var pos = GlobalPosition;
        var origin = pos + new Vector3(0, 0.3f, 0);
        Vector3? pref = p != null ? (p.Pos - pos) with { Y = 0 } : null;
        int tier = res.Tier;
        g.ServerDespawnObject(Id);
        if (res.Dud != GeodeDud.None)
        {
            if (res.Dud == GeodeDud.Gas) g.ServerSpawnObject("gaspuff", pos + new Vector3(0, 0.2f, 0), "r=" + LootTuning.GasRadius.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            g.Rpc(nameof(Game.ClientGeodeDud), pos, (int)res.Dud);
            Game.ServerNoise(pos, LootTuning.DudNoise, p);
            Util.Pass("geode_cracked", $"dud={res.Dud} by={p?.Name} size={Size}");
            return;
        }
        var land = LootPlacement.Landings(g.World, pos, pref, res.Items.Count, rng: rng);
        var ids = new int[res.Items.Count];
        for (int i = 0; i < ids.Length; i++) ids[i] = g.ServerSpawnObject("pickup", land[i], res.Items[i]);
        var col = LootTuning.ColorOfItem(res.Best);
        g.Rpc(nameof(Game.ClientLootBurst), pos, tier, col, ids, res.Items.ToArray(), land.ToArray());
        Game.ServerNoise(pos, LootTuning.RevealNoise[tier], p);
        LootStats.Revealed(tier, res.Items, LootTuning.RevealNoise[tier]);
        Util.Pass("geode_cracked", $"tier={tier} items={string.Join("+", res.Items)} by={p?.Name} size={Size} noise={LootTuning.RevealNoise[tier]:0}");
    }
}

// ----------------------------------------------------------------------------- Gas puff (dud geode)
/// <summary>A short-lived cloud of gas from a dud geode. Uses the normal gas damage (cause "gas": a gas mask makes the miner immune).</summary>
public partial class GasPuffObj : NetObject
{
    float _r = LootTuning.GasRadius, _life = LootTuning.GasSeconds, _tick;
    CpuParticles3D _cloud;
    OmniLight3D _light;

    public override void Build()
    {
        CollisionLayer = 0; CollisionMask = 0;
        var kv = Kv(Data);
        if (kv.TryGetValue("r", out var rs)) _r = ObjUtil.F(rs, _r);
        var green = new Color(0.45f, 0.9f, 0.25f);
        _light = new OmniLight3D { LightColor = green, LightEnergy = 1.6f, OmniRange = _r + 1.5f, Position = new Vector3(0, 0.6f, 0) };
        AddChild(_light);
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(green.R, green.G, green.B, 0.22f),
            AlbedoTexture = LootVis.Radial, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        };
        _cloud = new CpuParticles3D
        {
            Emitting = true, Amount = 60, Lifetime = 2.6f, Explosiveness = 0.15f, Direction = Vector3.Up, Spread = 70f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.8f,
            Gravity = new Vector3(0, 0.1f, 0), Position = new Vector3(0, 0.3f, 0), EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = _r * 0.45f,
            Mesh = new QuadMesh { Size = new Vector2(1.5f, 1.5f), Material = mat }, ScaleAmountMin = 0.7f, ScaleAmountMax = 1.3f,
        };
        AddChild(_cloud);
        Sfx.Play(SfxNames.GasPuff, GlobalPosition + Vector3.Up * 0.4f);
    }

    public override void _Process(double delta)
    {
        if (_light == null) return;
        _life -= (float)delta;
        _light.LightEnergy = Mathf.Clamp(_life / 1.5f, 0f, 1f) * 1.6f;
        if (_life < 1.2f && _cloud.Emitting) _cloud.Emitting = false;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!ObjUtil.Server) return;
        float dt = (float)delta;
        _tick -= dt;
        if (_tick <= 0)
        {
            _tick = LootTuning.GasTickSeconds;
            foreach (var p in Game.I.Players.Values.ToArray())
            {
                if (!p.Alive || !p.Participant || Game.I.World.Cave.InHall(p.Pos)) continue;
                if (ObjUtil.Flat(p.Pos - GlobalPosition).Length() < _r && Math.Abs(p.Pos.Y - GlobalPosition.Y) < 2.5f)
                {
                    float d = p.TakeDamage(LootTuning.GasTickDamage, null, "gas");
                    if (d > 0) Util.Pass("geode_gas_damage", $"victim={p.Name} dmg={d:0.#}");
                    else Util.Pass("geode_gas_immune", $"victim={p.Name}");
                }
            }
        }
        if (_life <= 0 && Id != 0) { _life = 999; Game.I.ServerDespawnObject(Id); }
    }
}
