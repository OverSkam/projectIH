using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Registers the monster NetObject kinds, marker spawners, noise subscription and per-round director.</summary>
[ContentModule]
public static class MonsterModule
{
    public static void Register()
    {
        NetObject.Factories["golem"] = () => new GolemObj();
        NetObject.Factories["ember"] = () => new EmberObj();
        NetObject.Factories["wisp"] = () => new WispObj();
        NetObject.Factories["dragon"] = () => new DragonObj();
        Game.MarkerSpawners["monster"] = SpawnMonsterMarker;
        Game.MarkerSpawners["dragon"] = SpawnDragonMarker;
        Game.NoiseMade += OnNoise;
        Game.RoundPopulated += OnRoundPopulated;
    }

    // ------------------------------------------------------------------ spawning
    public static float HpOf(string kind) => kind switch
    {
        "golem" => MonsterTuning.GolemHp, "ember" => MonsterTuning.EmberHp, _ => 1f,
    };

    public static T Spawn<T>(string kind, Vector3 pos) where T : MonsterObj
    {
        var g = Game.I;
        var st = kind == "dragon" ? MonsterState.Asleep : MonsterState.Idle;
        int id = g.ServerSpawnObject(kind, pos, MonsterObj.MakeData(st, HpOf(kind), HpOf(kind)));
        return g.World.Objects.TryGetValue(id, out var o) ? o as T : null;
    }

    static void SpawnMonsterMarker(Game g, RoomInfo room, SpawnMarker m, RandomNumberGenerator rng)
    {
        // Always draw the same number of values so the result is deterministic whatever branch is taken.
        float roll = rng.Randf(), pick = rng.Randf(), extraRoll = rng.Randf();
        float ox = rng.RandfRange(-2f, 2f), oz = rng.RandfRange(-2f, 2f);
        if (Util.HasArg("nomonsters")) return;
        int round = g.Round;
        if (g.World.Cave.InCamp(m.Pos)) return;
        float chance = Mathf.Clamp(MonsterTuning.SpawnChanceBase + MonsterTuning.SpawnChancePerRound * (round - 1) + MonsterTuning.SpawnChancePerDepth * (room.Depth - 1), 0f, 0.95f);
        if (roll > chance) return;
        SpawnOne(g, room, m.Pos, round, pick);
        if (round >= 4 && extraRoll < 0.35f) SpawnOne(g, room, m.Pos + new Vector3(ox, 0, oz) + new Vector3(Math.Sign(ox + 0.001f) * 2f, 0, 0), round, 1f - pick);
    }

    static void SpawnOne(Game g, RoomInfo room, Vector3 pos, int round, float pick)
    {
        float wg = MonsterTuning.GolemWeight;
        float we = round >= 2 || room.Depth >= 3 ? MonsterTuning.EmberWeightLate : (room.Depth >= 2 ? MonsterTuning.EarlyRareWeight : 0f);
        float ww = round >= 2 ? MonsterTuning.WispWeightLate : (room.Depth >= 2 ? MonsterTuning.EarlyRareWeight : 0f);
        if (round >= 3) { wg *= 0.7f; }
        float r = pick * (wg + we + ww);
        string kind = r < wg ? "golem" : (r < wg + we ? "ember" : "wisp");
        Spawn<MonsterObj>(kind, pos);
    }

    static void SpawnDragonMarker(Game g, RoomInfo room, SpawnMarker m, RandomNumberGenerator rng)
    {
        float[] rolls = new float[MonsterTuning.DragonHoardCount * 2];
        for (int i = 0; i < rolls.Length; i++) rolls[i] = rng.Randf();
        if (Util.HasArg("nomonsters")) return;
        SpawnDragon(m.Pos, rolls);
    }

    public static DragonObj SpawnDragon(Vector3 pos, float[] rolls = null)
    {
        var d = Spawn<DragonObj>("dragon", pos);
        string[] hoard = { "gold", "gold", "ruby", "diamond", "gold", "ruby", "diamond", "ruby" };
        int n = MonsterTuning.DragonHoardCount;
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.Tau / n + (rolls != null ? rolls[i] * 0.3f : 0f);
            float r = 4.6f + (rolls != null ? rolls[n + i] * 1.2f : 0f);
            var p = pos + new Vector3(Mathf.Cos(a) * r, 0.45f, Mathf.Sin(a) * r);
            Game.I.ServerSpawnObject("pickup", p, "ore:" + hoard[i % hoard.Length]);
        }
        return d;
    }

    // ------------------------------------------------------------------ noise
    static void OnNoise(Vector3 pos, float loud, ServerPlayer by)
    {
        if (Net.I == null || !Net.I.IsServer) return;
        foreach (var m in Live()) m.OnNoise(pos, loud, by);
    }

    static MonsterObj[] Live() => MonsterObj.Live.ToArray();

    /// <summary>Server: call from explosions (dynamite etc.). Gas wisps within 'radius' disperse for 'seconds'.</summary>
    public static void ExplosionAt(Vector3 pos, float radius, float seconds = 20f)
    {
        foreach (var m in Live())
            if (m is WispObj w && w.GlobalPosition.DistanceTo(pos) <= radius + 2f) w.Disperse(seconds);
    }

    // ------------------------------------------------------------------ burning status
    static readonly Dictionary<int, float> _burn = new();
    public static bool IsBurning(int peer) => _burn.ContainsKey(peer);

    public static void Ignite(ServerPlayer p, float seconds)
    {
        if (p == null || !p.Alive) return;
        if (Game.I.World.Cave.InWater(p.Pos + new Vector3(0, 0.5f, 0))) return;
        if (!_burn.TryGetValue(p.Peer, out var cur) || cur < seconds) _burn[p.Peer] = seconds;
    }

    static float _burnTick;
    public static void TickBurn(float dt)
    {
        if (_burn.Count == 0) return;
        _burnTick -= dt;
        bool tick = _burnTick <= 0; if (tick) _burnTick = 0.5f;
        foreach (var peer in _burn.Keys.ToArray())
        {
            if (!Game.I.Players.TryGetValue(peer, out var p) || !p.Alive || Game.I.World.Cave.InWater(p.Pos + new Vector3(0, 0.5f, 0))) { _burn.Remove(peer); continue; }
            _burn[peer] -= dt;
            if (tick) p.TakeDamage(MonsterTuning.BurnDps * 0.5f, null, "fire");
            if (_burn.TryGetValue(peer, out var left) && left <= 0) _burn.Remove(peer);
        }
    }

    // ------------------------------------------------------------------ per-round director
    static void OnRoundPopulated(Game g, RandomNumberGenerator rng)
    {
        _burn.Clear();
        if (g.World == null) return;
        g.World.AddChild(new MonsterDirector { Name = "MonsterDirector" });
    }
}

/// <summary>Server-only per-round node: burning ticks, debug spawns (--spawnmonster) and test scenarios (--montest).</summary>
public partial class MonsterDirector : Node
{
    readonly List<(string kind, string at)> _debug = new();
    float _nearTimer;
    IEnumerator<MonsterTest.Wait> _scenario;
    MonsterTest.Wait _cur;
    double _waitStart;

    public override void _Ready()
    {
        if (!Net.I.IsServer) return;
        foreach (var part in Util.Arg("spawnmonster", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('@');
            _debug.Add((kv[0], kv.Length > 1 ? kv[1] : "near"));
        }
        var name = Util.Arg("montest", "");
        if (name != "" && Game.I.Round == 1)
        {
            _scenario = MonsterTest.Create(name)?.GetEnumerator();
            if (_scenario == null) Util.Fail("montest_unknown", name);
            else { Advance(); }
        }
    }

    void Advance()
    {
        if (_scenario.MoveNext()) { _cur = _scenario.Current; _waitStart = Time.GetTicksMsec() / 1000.0; }
        else { _scenario = null; }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Net.I.IsServer) return;
        float dt = (float)delta;
        MonsterModule.TickBurn(dt);
        RunDebugSpawns(dt);
        RunScenario();
    }

    void RunScenario()
    {
        if (_scenario == null) return;
        double now = Time.GetTicksMsec() / 1000.0;
        if (_cur.Cond())
        {
            if (_cur.Name != null) Util.Pass(_cur.Name);
            Advance();
        }
        else if (now - _waitStart > _cur.Timeout)
        {
            Util.Fail(_cur.Name ?? "montest_step", "timeout");
            _scenario = null;
            MonsterTest.Finish(this, false);
        }
        if (_scenario == null && !MonsterTest.Finished) MonsterTest.Finish(this, true);
    }

    void RunDebugSpawns(float dt)
    {
        if (_debug.Count == 0) return;
        var g = Game.I;
        for (int i = _debug.Count - 1; i >= 0; i--)
        {
            var (kind, at) = _debug[i];
            Vector3? pos = null;
            if (at == "near")
            {
                var p = g.Players.Values.FirstOrDefault(q => q.Alive && q.Participant && !g.World.Cave.InCamp(q.Pos));
                if (p != null)
                {
                    _nearTimer += dt;
                    if (_nearTimer > 1.5f) pos = p.Pos + new Vector3(-Mathf.Sin(p.Yaw), 0, -Mathf.Cos(p.Yaw)) * (kind == "dragon" ? 12f : 7f);
                }
            }
            else
            {
                var xz = at.Split(':');
                if (xz.Length == 2 && float.TryParse(xz[0], out var x) && float.TryParse(xz[1], out var z)) pos = new Vector3(x, 0, z);
            }
            if (pos == null) continue;
            _debug.RemoveAt(i);
            var mon = kind == "dragon" ? MonsterModule.SpawnDragon(pos.Value) : MonsterModule.Spawn<MonsterObj>(kind, pos.Value);
            if (mon != null)
            {
                if (Util.HasArg("monfrozen")) mon.Frozen = true;   // debug: monsters do not move/attack
                if (Util.HasArg("monyaw") && float.TryParse(Util.Arg("monyaw"), out var yaw)) mon.Yaw = yaw;
            }
            Util.Log($"MONSTER: debug spawned {kind} at {pos.Value}");
        }
    }
}
