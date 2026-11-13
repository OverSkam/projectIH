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
        NetObject.Factories["plug"] = () => new PlugObj();
        NetObject.Factories["wicks"] = () => new WicksObj();
        NetObject.Factories["lamplighter"] = () => new LamplighterObj();
        NetObject.Factories["assessor"] = () => new AssessorObj();
        Game.MarkerSpawners["monster"] = SpawnMonsterMarker;
        Game.MarkerSpawners["dragon"] = SpawnDragonMarker;
        Game.NoiseMade += OnNoise;
        Game.RoundPopulated += OnRoundPopulated;
    }

    // ------------------------------------------------------------------ spawning
    public static T Spawn<T>(string kind, Vector3 pos) where T : MonsterObj
    {
        var g = Game.I;
        var st = kind == "dragon" ? MonsterState.Asleep : MonsterState.Idle;
        int id = g.ServerSpawnObject(kind, pos, MonsterObj.MakeData(st));
        return g.World.Objects.TryGetValue(id, out var o) ? o as T : null;
    }

    /// <summary>The old 'monster' markers no longer place monsters (roaming monsters spawn over the whole cave, see <see cref="SpawnRoamers"/>); the draws are kept so later random numbers stay the same.</summary>
    static void SpawnMonsterMarker(Game g, RoomInfo room, SpawnMarker m, RandomNumberGenerator rng)
    {
        rng.Randf(); rng.Randf(); rng.Randf(); rng.Randf(); rng.Randf();
    }

    /// <summary>The kinds to spawn this round, in order: the guaranteed ones first, the rest by weight.</summary>
    public static List<string> Plan(int miners, int round, RandomNumberGenerator rng)
    {
        int total = MonsterTuning.RoamTarget(miners, round);
        var list = new List<string>();
        foreach (var (kind, minRound, per) in MonsterTuning.Guaranteed)
            if (round >= minRound) for (int i = 0; i < per && list.Count < total; i++) list.Add(kind);
        int ri = Math.Clamp(round - 1, 0, 4);
        while (list.Count < total)
        {
            float sum = 0; foreach (var (_, w) in MonsterTuning.FillWeights) sum += w[Math.Min(ri, w.Length - 1)];
            float roll = rng.Randf() * sum, acc = 0; string pick = "golem";
            foreach (var (k, w) in MonsterTuning.FillWeights) { acc += w[Math.Min(ri, w.Length - 1)]; if (roll <= acc) { pick = k; break; } }
            list.Add(pick);
        }
        return list;
    }

    static NavProfile ProfileOf(string kind) => kind switch { "wicks" => NavProfile.Swarm, "lamplighter" or "assessor" => NavProfile.Person, _ => NavProfile.Tall };

    /// <summary>Server: places the round's roaming monsters over the cave, away from the Hall (>= 25 m of path) and apart from each other.</summary>
    public static List<MonsterObj> SpawnRoamers(Game g, RandomNumberGenerator rng)
    {
        var res = new List<MonsterObj>();
        var cave = g.World.Cave;
        var nav = MonsterNav.For(cave);
        int miners = g.Players.Values.Count(p => p.Participant);
        var plan = Plan(Math.Max(1, miners), g.Round, rng);
        var taken = new List<Vector3>();
        // tunnel-bound kinds first (they have the fewest legal places)
        foreach (var kind in plan.OrderBy(k => k == "plug" ? 0 : k == "lamplighter" ? 1 : 2))
        {
            var cells = nav.SpawnCells(ProfileOf(kind), 1, MonsterTuning.SpawnHallPath, MonsterTuning.SpawnSpacing, rng, taken, tunnelsOnly: kind == "plug" || kind == "lamplighter");
            if (cells.Count == 0) cells = nav.SpawnCells(ProfileOf(kind), 1, MonsterTuning.SpawnHallPath, MonsterTuning.SpawnSpacing, rng, taken);
            if (cells.Count == 0) { Util.Log($"MONSTER: no spawn cell for {kind}"); continue; }
            taken.Add(cells[0]);
            var m = Spawn<MonsterObj>(kind, cells[0]);
            if (m == null) continue;
            m.Yaw = rng.Randf() * Mathf.Tau;
            res.Add(m);
        }
        Util.Log($"MONSTER: round {g.Round} miners={miners} spawned {res.Count}: " + string.Join(",", res.Select(m => m.Kind).GroupBy(k => k).Select(k => k.Key + "=" + k.Count())));
        return res;
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
        MonsterSenses.Reset();
        if (g.World == null) return;
        if (!Util.HasArg("nomonsters")) SpawnRoamers(g, rng);
        g.World.AddChild(new MonsterDirector { Name = "MonsterDirector" });
    }
}

/// <summary>Server-only per-round node: burning ticks, debug spawns (--spawnmonster) and test scenarios (--montest).</summary>
public partial class MonsterDirector : Node
{
    readonly List<(string kind, string at)> _debug = new();
    float _nearTimer, _checkT, _encT;
    double _lastEncounter, _t;
    bool _density;
    public static int Sent; public static double SentAt = -1;
    float _quiet = MonsterTuning.DirectorQuiet, _check = MonsterTuning.DirectorCheck;
    IEnumerator<MonsterTest.Wait> _scenario;
    MonsterTest.Wait _cur;
    double _waitStart;

    public override void _Ready()
    {
        if (!Net.I.IsServer) return;
        _density = !Util.HasArg("nomonsters") || Util.HasArg("mondirector");
        if (float.TryParse(Util.Arg("dirquiet", ""), out var dq)) _quiet = dq;
        if (float.TryParse(Util.Arg("dircheck", ""), out var dc)) _check = dc;
        _checkT = _check; _lastEncounter = 0; Sent = 0; SentAt = -1;
        MonsterStage.Begin(this);
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
        _t += dt;
        MonsterSenses.Tick(dt);
        MonsterModule.TickBurn(dt);
        Density(dt);
        MonsterStage.Tick(this, dt);
        RunDebugSpawns(dt);
        RunScenario();
    }

    /// <summary>The density director: every DirectorCheck seconds, if no living miner had a monster within 15 m for DirectorQuiet seconds, send the nearest roaming monster toward a random miner's rough area.</summary>
    void Density(float dt)
    {
        if (!_density) return;
        var g = Game.I;
        var cave = g.World.Cave;
        _encT -= dt;
        if (_encT <= 0)
        {
            _encT = 0.5f;
            foreach (var p in g.Players.Values)
            {
                if (!p.Alive || !p.Participant || cave.InHall(p.Pos)) continue;
                foreach (var m in MonsterObj.Live)
                    if (m.State != MonsterState.Asleep && m.GlobalPosition.DistanceTo(p.Pos) <= MonsterTuning.EncounterRadius) { _lastEncounter = _t; break; }
            }
        }
        _checkT -= dt;
        if (_checkT > 0) return;
        _checkT = _check;
        if (_t - _lastEncounter < _quiet) return;
        var miners = g.Players.Values.Where(p => p.Alive && p.Participant && !cave.InHall(p.Pos)).ToList();
        if (miners.Count == 0) return;
        var target = miners[(int)(GD.Randi() % (uint)miners.Count)];
        var ang = GD.Randf() * Mathf.Tau;
        var area = target.Pos + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * (MonsterTuning.DirectorScatter * (0.5f + 0.5f * GD.Randf()));
        MonsterObj best = null; float bd = float.MaxValue;
        foreach (var m in MonsterObj.Live)
        {
            if (m is DragonObj || m is PlugObj || m is AssessorObj || !m.IsRoaming || m.Frozen) continue;
            float d = m.GlobalPosition.DistanceTo(area);
            if (d < bd) { bd = d; best = m; }
        }
        if (best == null) return;
        best.Dispatch(area); Sent++; if (SentAt < 0) SentAt = _t;
        _lastEncounter = _t;
        Util.Pass("director_sent_monster", $"kind={best.Kind} id={best.Id} target={target.Name} quiet={_quiet:0}");
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
                var p = g.Players.Values.FirstOrDefault(q => q.Alive && q.Participant && !g.World.Cave.InHall(q.Pos));
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
