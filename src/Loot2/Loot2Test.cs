using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace AppStein;

/// <summary>Server-side counters the tests read (what the last reveals were, noise radii, mimic damage).</summary>
public static class LootStats
{
    public static readonly List<(int tier, float noise, List<string> items)> Reveals = new();
    public static readonly List<float> Shrieks = new();
    public static float BiteDamage;
    public static void Revealed(int tier, List<string> items, float noise) => Reveals.Add((tier, noise, new List<string>(items)));
    public static void Shriek(float r) => Shrieks.Add(r);
}

/// <summary>
/// Test and staging code for step 2 (command line only):
///   --l2check                        headless self-check of the roll tables (seeded: junk share, stein rates, depth / round / Luck effects, 2000-roll Luck comparison)
///   --l2test=main                    host runs the scripted scenarios (see Loot2Director); clients run with --bot=l2_alice (Alice) / --bot=l2_host (host)
///   --l2stage=geode:1:2@0,0,-3;chest:2@2,0,-3;mimic:1@..;chestopen:1@..;bite:1@..;burst:3:stein:luck@..   place objects relative to the centre of room --l2room=N (screenshots)
///   --l2burst=<seconds>              the staged bursts go off this long after the round starts
///   --l2nomimic                      no mimics among the generated chests
/// </summary>
public static class Loot2Debug
{
    public static void Register()
    {
        if (Util.HasArg("l2check"))
        {
            int bad = Loot2Check.Run();
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree != null) tree.ProcessFrame += () => tree.Quit(bad == 0 ? 0 : 1);
            return;
        }
        if (Util.HasArg("l2test") && Util.HasArg("host"))
            Game.I.CallDeferred(Node.MethodName.AddChild, new Loot2Director { Name = "Loot2Director" });
        if (Util.HasArg("l2stage")) Game.RoundPopulated += OnRoundPopulated;
    }

    static Vector3 V3(string s)
    {
        var p = s.Split(',');
        return new Vector3(float.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture));
    }

    static void OnRoundPopulated(Game g, RandomNumberGenerator rng)
    {
        if (!Net.I.IsServer) return;
        int room = int.TryParse(Util.Arg("l2room", Util.Arg("camroom", "1")), out var rn) ? rn : 1;
        var anchor = g.World.Cave.Rooms[Mathf.Clamp(room, 0, g.World.Cave.Rooms.Count - 1)].Center with { Y = 0 };
        foreach (var tok in Util.Arg("l2stage").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int at = tok.IndexOf('@');
            var spec = tok.Substring(0, at).Split(':'); var pos = anchor + V3(tok.Substring(at + 1));
            switch (spec[0])
            {
                case "geode":
                    g.ServerSpawnObject("geode", pos, GeodeObj.MakeData(int.Parse(spec[1]), spec.Length > 2 ? int.Parse(spec[2]) : 0)); break;
                case "chest": case "mimic": case "chestopen": case "bite": case "stun":
                    {
                        string st = spec[0] switch { "chestopen" => "open", "bite" => "bite", "stun" => "stun", _ => "closed" };
                        int id = g.ServerSpawnObject("bchest", pos, GlowChestObj.MakeData(int.Parse(spec[1]), st, spec[0] is "mimic" or "bite" or "stun", 0));
                        if (g.World.Objects.TryGetValue(id, out var o) && o is GlowChestObj c) { c.Mimic = spec[0] is "mimic" or "bite" or "stun"; c.Frozen = true; }
                        break;
                    }
                case "openchest":
                    {
                        int id = g.ServerSpawnObject("bchest", pos, GlowChestObj.MakeData(int.Parse(spec[1]), "closed", false));
                        var chest = (GlowChestObj)g.World.Objects[id];
                        float when = float.TryParse(Util.Arg("l2burst", "2"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w2) ? w2 : 2f;
                        g.GetTree().CreateTimer(when).Timeout += () => { if (g.World != null && g.World.Objects.ContainsKey(id)) chest.ServerDebugOpen(g.Players.Values.First()); };
                        break;
                    }
                case "burst":
                    {
                        int tier = int.Parse(spec[1]); string item = string.Join(":", spec.Skip(2));
                        float when = float.TryParse(Util.Arg("l2burst", "2"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w) ? w : 2f;
                        g.GetTree().CreateTimer(when).Timeout += () =>
                        {
                            if (g.World == null) return;
                            var land = LootPlacement.Landings(g.World, pos, null, 1, 0.8f, 1.2f);
                            int pid = g.ServerSpawnObject("pickup", land[0], item);
                            g.Rpc(nameof(Game.ClientLootBurst), pos + new Vector3(0, 0.3f, 0), tier, LootTuning.ColorOfItem(item), new[] { pid }, new[] { item }, new[] { land[0] });
                        };
                        break;
                    }
            }
        }
    }
}

/// <summary>`--l2check`: the roll tables, seeded. Prints PASS/FAIL lines.</summary>
public static class Loot2Check
{
    public static int Run()
    {
        int bad = 0;
        void Chk(string name, bool ok, string d = "") { if (ok) Util.Pass("l2check_" + name, d); else { bad++; Util.Fail("l2check_" + name, d); } }
        var rng = new RandomNumberGenerator { Seed = 20260101 };

        // dud share ~10%
        int duds = 0, N = 100000;
        for (int i = 0; i < N; i++) if (LootRolls.RollGeode(rng, 1, 1, 1, 0).Dud != GeodeDud.None) duds++;
        Chk("dud_rate", Math.Abs(duds / (float)N - LootTuning.DudChance) < 0.01f, $"duds={duds / (float)N:0.000}");

        // mostly junk (coal / iron) in small geodes at depth 1
        int junk = 0, total = 0;
        for (int i = 0; i < N; i++) { var r = LootRolls.RollGeode(rng, 0, 1, 1, 0); foreach (var it in r.Items) { total++; if (it == "ore:coal" || it == "ore:iron") junk++; } }
        Chk("mostly_junk", junk / (float)total > 0.72f, $"junk={junk / (float)total:0.000}");

        // large geodes: ~4% common, ~1% rare, ~0.3% legendary (legendary only deeper)
        int c = 0, ra = 0, le = 0, lShallow = 0, M = 400000;
        for (int i = 0; i < M; i++)
        {
            var r = LootRolls.RollGeode(rng, 2, 4, 1, 0);
            if (r.Items.Count == 1 && r.Items[0].StartsWith("stein:")) { var rar = Steins.Get(r.Items[0].Substring(6)).Rarity; if (rar == Rarity.Common) c++; else if (rar == Rarity.Rare) ra++; else le++; }
            var s = LootRolls.RollGeode(rng, 2, 1, 1, 0);
            if (s.Items.Count == 1 && s.Items[0].StartsWith("stein:") && Steins.Get(s.Items[0].Substring(6)).Rarity == Rarity.Legendary) lShallow++;
        }
        float pc = c / (float)M, pr = ra / (float)M, pl = le / (float)M;
        Chk("stein_common_rate", Math.Abs(pc - 0.04f) < 0.006f, $"common={pc:0.0000} (target 0.04 among non-duds... overall)");
        Chk("stein_rare_rate", Math.Abs(pr - 0.01f) < 0.003f, $"rare={pr:0.0000}");
        Chk("stein_legendary_rate", Math.Abs(pl - 0.003f) < 0.0016f, $"legendary={pl:0.0000}");
        Chk("legendary_only_deeper", lShallow == 0, $"shallow_legendary={lShallow}");
        // small and medium geodes never hold a stein
        bool noSmallStein = true;
        for (int i = 0; i < 20000; i++) foreach (var sz0 in new[] { 0, 1 }) if (LootRolls.RollGeode(rng, sz0, 5, 3, 1).Items.Any(x => x.StartsWith("stein:"))) noSmallStein = false;
        Chk("only_large_hold_steins", noSmallStein);

        double Mean(int size, int depth, int round, float luck, int n, ulong seed)
        {
            var r2 = new RandomNumberGenerator { Seed = seed }; double sum = 0;
            for (int i = 0; i < n; i++) sum += LootRolls.RollGeode(r2, size, depth, round, luck).Tribute;
            return sum / n;
        }
        double d1 = Mean(1, 1, 1, 0, 20000, 7), d4 = Mean(1, 4, 1, 0, 20000, 7), r5 = Mean(1, 1, 5, 0, 20000, 7), big = Mean(2, 1, 1, 0, 20000, 7);
        Chk("depth_improves", d4 > d1 * 1.2, $"depth1={d1:0.00} depth4={d4:0.00}");
        Chk("round_improves", r5 > d1 * 1.1, $"round1={d1:0.00} round5={r5:0.00}");
        Chk("size_improves", big > d1 * 1.5, $"medium={d1:0.00} large={big:0.00}");
        // Luck: 2000 seeded rolls with and without the Luck stein (same random stream)
        double l0 = Mean(1, 1, 1, 0f, 2000, 99), l1 = Mean(1, 1, 1, 1f, 2000, 99);
        int rare0 = 0, rare1 = 0;
        var ra0 = new RandomNumberGenerator { Seed = 99 }; var ra1 = new RandomNumberGenerator { Seed = 99 };
        for (int i = 0; i < 2000; i++)
        {
            if (LootRolls.RollGeode(ra0, 1, 1, 1, 0f).Items.Any(x => LootTuning.TierOfItem(x) >= 2)) rare0++;
            if (LootRolls.RollGeode(ra1, 1, 1, 1, 1f).Items.Any(x => LootTuning.TierOfItem(x) >= 2)) rare1++;
        }
        Chk("luck_improves_rolls", l1 > l0 * 1.15 && rare1 > rare0, $"mean tribute luck0={l0:0.00} luck1={l1:0.00} ruby+ rolls luck0={rare0} luck1={rare1} of 2000");

        // chests: 3-6 stones, better with tier, stein chance by tier
        bool sizes = true; double[] ct = new double[3]; int[] steins = new int[3];
        for (int t = 0; t < 3; t++)
        {
            var r3 = new RandomNumberGenerator { Seed = (ulong)(5 + t) };
            for (int i = 0; i < 4000; i++)
            {
                var l = LootRolls.RollChest(r3, t, 3, 2, 0);
                int stones = l.Count(x => x.StartsWith("ore:"));
                if (stones < 3 || stones > 6) sizes = false;
                ct[t] += l.Where(x => x.StartsWith("ore:")).Sum(x => Ores.WeightOf(x.Substring(4)));
                if (l.Any(x => x.StartsWith("stein:"))) steins[t]++;
            }
            ct[t] /= 4000;
        }
        Chk("chest_3_to_6_stones", sizes);
        Chk("chest_tiers_pay_more", ct[1] > ct[0] && ct[2] > ct[1], $"mean tribute {ct[0]:0.0}/{ct[1]:0.0}/{ct[2]:0.0}");
        Chk("chest_stein_chance_by_tier", steins[0] < steins[1] && steins[1] < steins[2], $"steins per 4000: {steins[0]}/{steins[1]}/{steins[2]}");
        // veins: 1-3 geodes, richer ore = bigger
        bool cnt = true; double[] sz = new double[4]; int[] n = new int[4];
        var rv = new RandomNumberGenerator { Seed = 3 };
        foreach (var ore in new[] { "coal", "silver", "gold", "diamond" })
            for (int i = 0; i < 3000; i++)
            {
                var l = LootRolls.VeinGeodes(rv, ore);
                if (l.Count < 1 || l.Count > 3) cnt = false;
                int k = Array.IndexOf(new[] { "coal", "silver", "gold", "diamond" }, ore); sz[k] += l.Average(); n[k]++;
            }
        Chk("vein_geodes_1_to_3", cnt);
        Chk("vein_richness_bigger_geodes", sz[0] / n[0] < sz[1] / n[1] && sz[1] / n[1] < sz[2] / n[2] && sz[2] / n[2] < sz[3] / n[3], $"mean size {sz[0] / n[0]:0.00}/{sz[1] / n[1]:0.00}/{sz[2] / n[2]:0.00}/{sz[3] / n[3]:0.00}");
        // tiers and noise: dull < bright < gem < stein, stein 45 m
        Chk("noise_by_tier", LootTuning.RevealNoise[0] < LootTuning.RevealNoise[1] && LootTuning.RevealNoise[1] < LootTuning.RevealNoise[2] && LootTuning.RevealNoise[3] == 45f);
        Chk("tier_of_items", LootTuning.TierOfItem("ore:coal") == 0 && LootTuning.TierOfItem("ore:iron") == 0 && LootTuning.TierOfItem("ore:silver") == 1 && LootTuning.TierOfItem("ore:gold") == 1
            && LootTuning.TierOfItem("ore:ruby") == 2 && LootTuning.TierOfItem("ore:diamond") == 2 && LootTuning.TierOfItem("stein:regen") == 3);
        if (bad == 0) Util.Pass("l2check", "all roll-table checks");
        return bad;
    }
}

// ============================================================================ bot
/// <summary>Scripted client for the loot test (--bot=l2_alice / l2_host): does what the director's step says, using only what a real client sees.</summary>
public class Loot2Bot : BotBrain
{
    static int _serial; static string _step = "", _arg = "";
    public static void OnStep(string step, string arg) { if (!Util.Arg("bot").StartsWith("l2_")) return; _step = step; _arg = arg; _serial++; }

    float _yaw, _pitch; int _seen = -1; double _t; bool _flag, _flag2; Vector3 _gp; int _tries; double _pick;

    public Loot2Bot(string routine) : base("idle") { }

    static T Nearest<T>(Game g, Vector3 from, float max, Func<T, bool> ok = null) where T : NetObject
    {
        T best = null; float bd = max;
        foreach (var o in g.World.Objects.Values)
            if (o is T t && (ok == null || ok(t))) { float d = o.GlobalPosition.DistanceTo(from); if (d < bd) { bd = d; best = t; } }
        return best;
    }

    /// <summary>Step argument: "Name" or "Name|objectId" (a specific chest).</summary>
    bool Mine(out int id)
    {
        var a = _arg.Split('|'); id = a.Length > 1 ? int.Parse(a[1]) : -1;
        return Net.I.LocalName == a[0];
    }

    void FaceTo(Miner m, Vector3 pt)
    {
        var d = pt - m.EyePos;
        _yaw = Mathf.Atan2(-d.X, -d.Z);
        _pitch = Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), -1.4f, 1.4f);
    }

    bool Walk(Miner m, ref MinerInput i, Vector3 target, float stop)
    {
        var flat = new Vector3(target.X - m.GlobalPosition.X, 0, target.Z - m.GlobalPosition.Z);
        if (flat.Length() <= stop) return true;
        _yaw = Mathf.Atan2(-flat.X, -flat.Z); _pitch = 0;
        i.Move = new Vector2(0, 1);
        return false;
    }

    public override MinerInput Poll(float dt, Miner m)
    {
        var i = new MinerInput { UtilSlot = -1 };
        var g = Game.I;
        if (g.Phase == GamePhase.Round && g.World != null) Act(dt, m, ref i, g);
        i.Yaw = _yaw; i.Pitch = _pitch;
        return i;
    }

    void Act(float dt, Miner m, ref MinerInput i, Game g)
    {
        if (_seen != _serial) { _seen = _serial; _t = 0; _flag = _flag2 = false; _tries = 0; _gp = Vector3.Zero; }
        _t += dt; _pick -= dt;
        string me = Net.I.LocalName;
        switch (_step)
        {
            case "crack":
                {
                    if (!Mine(out int want)) break;
                    if (_t < 0.3) { int pi = Array.IndexOf(g.OwnInv.Utils, "pickaxe"); if (pi >= 0) g.SendSelectUtil(pi); break; }
                    var ge = Nearest<GeodeObj>(g, m.GlobalPosition, 12f);
                    if (ge != null)
                    {
                        _gp = ge.GlobalPosition;
                        FaceTo(m, ge.GlobalPosition + Vector3.Up * 0.4f);
                        if (Walk(m, ref i, ge.GlobalPosition, 1.7f) && g.OwnInv.Held == "pickaxe") i.Attack = true;
                        if (m.GlobalPosition.DistanceTo(ge.GlobalPosition) < 2.4f && g.OwnInv.Held == "pickaxe") i.Attack = true;
                    }
                    else if (_gp != Vector3.Zero && !_flag)
                    {
                        if (_t > 1.5)
                        {
                            _flag = true;
                            int n = g.World.Objects.Values.Count(o => o is PickupObj && o.GlobalPosition.DistanceTo(_gp) < 3.5f);
                            if (n >= 1) Util.Pass("geode_stones_replicated", $"n={n}"); else Util.Fail("geode_stones_replicated", "no stones near the cracked geode");
                        }
                    }
                    break;
                }
            case "pick":
                {
                    if (!Mine(out int want)) break;
                    var p = Nearest<PickupObj>(g, m.GlobalPosition, 12f, o => o.Data.StartsWith("ore:"));
                    if (p == null) break;
                    FaceTo(m, p.GlobalPosition);
                    if (Walk(m, ref i, p.GlobalPosition, 1.2f) && _pick <= 0) { _pick = 0.5; g.SendInteract(p.Id); }
                    break;
                }
            case "open":
                {
                    if (!Mine(out int want)) break;
                    var c = Nearest<GlowChestObj>(g, m.GlobalPosition, 10f, o => (want < 0 || o.Id == want) && o.State is "closed" or "hold");
                    if (Util.HasArg("botdebug") && ((int)(_t * 2)) % 4 == 0 && _pick <= 0) { _pick = 1.9; Util.Log($"L2BOT open chest={(c == null ? "none" : c.Id.ToString())} pos={m.GlobalPosition} dist={(c == null ? -1 : m.GlobalPosition.DistanceTo(c.GlobalPosition)):0.0}"); }
                    if (c == null) break;
                    FaceTo(m, c.GlobalPosition + Vector3.Up * 0.4f);
                    if (Walk(m, ref i, c.GlobalPosition, 1.8f) && !_flag) { _flag = true; Loot2Client.Start(c.Id); Util.Pass("bot_hold_started", $"id={c.Id}"); }
                    break;
                }
            case "hit":
                {
                    if (!Mine(out int want)) break;
                    if (_t < 0.3) { int pi = Array.IndexOf(g.OwnInv.Utils, "pickaxe"); if (pi >= 0) g.SendSelectUtil(pi); break; }
                    var c = Nearest<GlowChestObj>(g, m.GlobalPosition, 10f, o => want < 0 || o.Id == want);
                    if (Util.HasArg("botdebug") && _pick <= 0) { _pick = 0.5; Util.Log($"L2BOT hit chest={(c == null ? "none" : c.Id.ToString())} state={c?.State} pos={m.GlobalPosition} held={g.OwnInv.Held} root={m.RootLeft:0.0} vel={m.Velocity} dist={(c == null ? -1 : m.GlobalPosition.DistanceTo(c.GlobalPosition)):0.0}"); }
                    if (c == null) break;
                    if (Util.HasArg("botdebug") && !_flag) { _flag = true; var q = Queries.Ray(m.GlobalPosition + Vector3.Up * 0.5f, m.GlobalPosition + Vector3.Up * 0.5f + new Vector3(2f, 0, 0), 0xFF); q.Exclude = new Godot.Collections.Array<Rid> { m.GetRid() }; var h = m.GetWorld3D().DirectSpaceState.IntersectRay(q); Util.Log("L2BOT ray east: " + (h.Count > 0 ? h["collider"].As<Node>().Name + " pos " + h["collider"].As<Node3D>().GlobalPosition + " at " + h["position"].AsVector3() : "nothing")); }
                    FaceTo(m, c.GlobalPosition + Vector3.Up * 0.4f);
                    if (Walk(m, ref i, c.GlobalPosition, 1.7f) && g.OwnInv.Held == "pickaxe") i.Attack = true;
                    break;
                }
            case "checkpos":
                {
                    if (_flag) break;
                    _flag = true;
                    var a = _arg.Split('|');
                    if (g.World.Objects.TryGetValue(int.Parse(a[0]), out var o))
                    {
                        float d = new Vector2(o.GlobalPosition.X - float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), o.GlobalPosition.Z - float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture)).Length();
                        if (d < 0.8f) Util.Pass("mimic_pos_replicated", $"me={me} off={d:0.00}"); else Util.Fail("mimic_pos_replicated", $"me={me} off={d:0.00}");
                    }
                    else Util.Fail("mimic_pos_replicated", "chest missing on client");
                    break;
                }
        }
    }
}

// ============================================================================ director
/// <summary>Host-only scripted scenarios of the loot test (2 miners = 1 round). Server-side checks print PASS/FAIL; the bots do the player-side actions on their own clients.</summary>
public partial class Loot2Director : Node
{
    bool _started;
    readonly List<(Vector3 pos, float r)> _noise = new();
    ServerPlayer P(string n) => Game.I.Players.Values.FirstOrDefault(p => p.Name == n);
    static void Chk(string name, bool ok, string d = "") { if (ok) Util.Pass(name, d); else Util.Fail(name, d); }
    static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        Game.NoiseMade += (pos, r, by) => _noise.Add((pos, r));
    }

    public override void _Process(double delta)
    {
        var g = Game.I;
        if (_started || !Net.I.IsServer || g.Phase != GamePhase.Round || g.World == null) return;
        _started = true;
        _ = RunAsync();
    }

    async Task Wait(float s) => await ToSignal(GetTree().CreateTimer(s), SceneTreeTimer.SignalName.Timeout);
    async Task<bool> Until(Func<bool> f, float timeout)
    {
        double end = Now + timeout;
        while (Now < end) { if (f()) return true; await Wait(0.1f); }
        return f();
    }

    T Spawn<T>(string kind, Vector3 pos, string data) where T : NetObject
    {
        int id = Game.I.ServerSpawnObject(kind, pos, data);
        return (T)Game.I.World.Objects[id];
    }

    int Count<T>(Vector3 near, float r) where T : NetObject => Game.I.World.Objects.Values.Count(o => o is T && o.GlobalPosition.DistanceTo(near) < r);
    int Pickups() => Game.I.World.Objects.Values.Count(o => o is PickupObj);

    /// <summary>Wait for both miners to stand somewhere, then move them.</summary>
    void Put(string name, Vector3 pos) { var p = P(name); Game.I.L2Teleport(p, pos, 0f); foreach (var dl in new[] { 0.15, 0.4, 0.7 }) GetTree().CreateTimer(dl).Timeout += () => Util.Log($"L2POS {name} want={pos.X:0.0},{pos.Y:0.0},{pos.Z:0.0} have={p.Pos.X:0.0},{p.Pos.Y:0.0},{p.Pos.Z:0.0} hp={p.Health:0}"); }

    static bool Sec(string n) => !Util.HasArg("l2only") || ("," + Util.Arg("l2only") + ",").Contains("," + n + ",");

    async Task RunAsync()
    {
        var g = Game.I; var O = new Vector3(32, 0, 0);   // room A of the grey-box cave
        await Wait(5f);
        var alice = P("Alice"); var host = P("Host");
        if (alice == null || host == null) { Util.Fail("l2_players", "Alice / Host missing"); return; }
        host.Stats.NoiseMul = 1f; alice.Stats.NoiseMul = 1f;
        Util.Pass("l2_start");
        if (Util.HasArg("l2probe"))
            foreach (float z in new[] { -9f, -8f, -5f, 0f, 5f, 9f })
            {
                var sb = new System.Text.StringBuilder();
                for (float x = 23; x <= 41; x += 1)
                {
                    var q = Queries.Ray(new Vector3(x, 2, z), new Vector3(x, -3, z), 1);
                    var h = g.World.GetWorld3D().DirectSpaceState.IntersectRay(q);
                    sb.Append(h.Count > 0 ? h["position"].AsVector3().Y.ToString("0.0") : "none").Append(' ');
                }
                Util.Log($"L2PROBE z={z}: {sb}");
            }

        // ---------------------------------------------------------------- 1. a vein breaks into geodes, never into stones
        if (Sec("vein"))
        {
            var vp = O + new Vector3(-7, 0, -7);
            var vein = Spawn<VeinObj>("vein", vp, VeinObj.MakeData("gold", 3, 3));
            int pk0 = Pickups(), ge0 = g.World.Objects.Values.Count(o => o is GeodeObj);
            Put("Alice", vp + new Vector3(2.5f, 0.1f, 0));
            await Wait(0.8f);
            for (int i = 0; i < 3; i++) { if (g.World.Objects.ContainsKey(vein.Id)) vein.ServerHit(alice, 1f, true); await Wait(0.15f); }
            await Wait(0.3f);
            int ge1 = g.World.Objects.Values.Count(o => o is GeodeObj) - ge0;
            Chk("vein_broken_into_geodes", !g.World.Objects.ContainsKey(vein.Id) && ge1 >= 1 && ge1 <= 3, $"geodes={ge1}");
            Chk("vein_drops_no_stones", Pickups() == pk0, $"pickups before={pk0} after={Pickups()}");
            foreach (var o in g.World.Objects.Values.Where(o => o is GeodeObj).ToArray()) g.ServerDespawnObject(o.Id);
        }

        // ---------------------------------------------------------------- 2. a real client cracks a geode in 3 pickaxe hits; the stones replicate
        if (Sec("geode"))
        {
            var gp = O + new Vector3(0, 0, -3);
            var ge = Spawn<GeodeObj>("geode", gp, GeodeObj.MakeData(1, 0));
            ge.Depth = 1; ge.ForcedResult = new GeodeResult { Items = { "ore:coal", "ore:iron" } };
            Put("Alice", gp + new Vector3(3f, 0.1f, 0)); Put("Host", O + new Vector3(8, 0.1f, 8));
            await Wait(0.8f);
            g.L2Step("crack", "Alice");
            int pk0 = Pickups();
            bool seen1 = false, seen2 = false;
            bool gone = await Until(() => { if (g.World.Objects.ContainsKey(ge.Id)) { if (ge.Hits >= 1) seen1 = true; if (ge.Hits >= 2) seen2 = true; } return !g.World.Objects.ContainsKey(ge.Id); }, 20f);
            Chk("geode_three_hits", gone && seen1 && seen2, $"hit1={seen1} hit2={seen2}");
            await Wait(1.2f);
            Chk("geode_stones_spawned", Pickups() - pk0 == 2, $"new pickups={Pickups() - pk0}");
            g.L2Step("pick", "Alice");
            await Wait(5f);
            Chk("geode_stone_taken", alice.Inv.Sack.Count >= 1, $"sack={alice.Inv.Sack.Count}");
            g.L2Step("idle", "");
        }

        // ---------------------------------------------------------------- 3. forced rarities: the reveal noise has the radius of its tier (stein: 45 m)
        if (Sec("noise"))
        {
            var tiers = new (int tier, string item)[] { (0, "ore:coal"), (1, "ore:gold"), (2, "ore:ruby"), (3, "stein:luck") };
            int k = 0;
            foreach (var (tier, item) in tiers)
            {
                var gp = O + new Vector3(-3 + k * 2.2f, 0, 5); k++;
                var ge = Spawn<GeodeObj>("geode", gp, GeodeObj.MakeData(2, 0)); ge.Depth = 2;
                ge.ForcedResult = new GeodeResult { Items = { item } };
                Put("Alice", gp + new Vector3(0, 0.1f, 2f));
                await Wait(0.4f);
                _noise.Clear();
                for (int h = 0; h < 3; h++) { ge.ServerHit(alice, 1f, true); await Wait(0.32f); }
                await Wait(0.3f);
                float want = LootTuning.RevealNoise[tier];
                bool ok = _noise.Any(n => Math.Abs(n.r - want) < 0.01f && n.pos.DistanceTo(gp) < 1.5f);
                Chk($"reveal_noise_tier{tier}", ok, $"radius={want:0} heard={string.Join(",", _noise.Select(n => n.r.ToString("0")))}");
                Chk($"hits_get_louder_tier{tier}", _noise.Count >= 3 || tier > 0, "");
                await Wait(0.6f);
            }
            Chk("stein_reveal_heard_45m", LootStats.Reveals.Any(r => r.tier == 3 && Math.Abs(r.noise - 45f) < 0.01f));
            await Wait(4.5f);
            foreach (var o in g.World.Objects.Values.Where(o => o is GeodeObj or PickupObj).ToArray()) g.ServerDespawnObject(o.Id);
        }

        // ---------------------------------------------------------------- 4. duds: gas (damage, mask immunity) and bats (noise, no damage)
        if (Sec("dud"))
        {
            var gp = O + new Vector3(-8, 0, -8);
            var ge = Spawn<GeodeObj>("geode", gp, GeodeObj.MakeData(0, 0)); ge.ForcedResult = new GeodeResult { Dud = GeodeDud.Gas };
            Put("Alice", gp + new Vector3(1.2f, 0.1f, 0)); await Wait(0.6f);
            float hp0 = alice.Health;
            for (int h = 0; h < 3; h++) { ge.ServerHit(alice, 1f, true); await Wait(0.3f); }
            await Wait(2.5f);
            Chk("gas_dud_hurts", alice.Health < hp0 - 1f, $"hp {hp0:0.#} -> {alice.Health:0.#}");
            await Wait(3f);
            alice.Health = 100f; alice.Dirty = true;
            alice.Stats.GasImmune = true;   // a gas mask: the normal gas rule makes the miner immune
            var ge2 = Spawn<GeodeObj>("geode", gp + new Vector3(0, 0, 3), GeodeObj.MakeData(0, 0)); ge2.ForcedResult = new GeodeResult { Dud = GeodeDud.Gas };
            Put("Alice", gp + new Vector3(1.2f, 0.1f, 3)); await Wait(0.6f);
            hp0 = alice.Health;
            for (int h = 0; h < 3; h++) { ge2.ServerHit(alice, 1f, true); await Wait(0.3f); }
            await Wait(3f);
            Chk("gas_dud_mask_immune", alice.Health >= hp0 - 0.01f, $"hp {hp0:0.#} -> {alice.Health:0.#}");
            alice.Stats.GasImmune = false;
            await Wait(2.5f);
            var ge3 = Spawn<GeodeObj>("geode", gp + new Vector3(4, 0, 0), GeodeObj.MakeData(0, 0)); ge3.ForcedResult = new GeodeResult { Dud = GeodeDud.Bats };
            Put("Alice", gp + new Vector3(5.2f, 0.1f, 0)); await Wait(0.6f);
            _noise.Clear(); hp0 = alice.Health;
            for (int h = 0; h < 3; h++) { ge3.ServerHit(alice, 1f, true); await Wait(0.3f); }
            await Wait(2f);
            Chk("bats_dud_noisy_harmless", _noise.Any(n => Math.Abs(n.r - LootTuning.DudNoise) < 0.01f) && alice.Health >= hp0 - 0.01f, $"noise={string.Join(",", _noise.Select(n => n.r.ToString("0")))} hp {hp0:0.#}->{alice.Health:0.#}");
        }

        // ---------------------------------------------------------------- 5. chest: hold E 2.5 s -> lid, light, 3-6 stones one by one
        if (Sec("chest"))
        {
            var cp = O + new Vector3(6, 0, -5);
            var chest = Spawn<GlowChestObj>("bchest", cp, GlowChestObj.MakeData(0, "closed", false)); chest.Depth = 1;
            Put("Alice", cp + new Vector3(2.3f, 0.1f, 0)); Put("Host", O + new Vector3(8, 0.1f, 8)); await Wait(0.8f);
            int pk0 = Pickups(); double t0 = Now;
            g.L2Step("open", $"Alice|{chest.Id}");
            await Until(() => chest.State == "hold", 6f);
            double tHold = Now;
            bool open = await Until(() => chest.State == "open", 8f);
            double dur = Now - tHold;
            Chk("chest_hold_opens", open, $"hold {dur:0.00}s");
            Chk("chest_hold_takes_2_5s", open && dur > 2.2 && dur < 3.3, $"{dur:0.00}s");
            await Wait(3.2f);
            int stones = Pickups() - pk0;
            Chk("chest_gives_stones", stones >= 3 && stones <= 5, $"pickups={stones}");   // 3-4 stones for a wooden chest, plus a stein at the end sometimes
            g.L2Step("idle", "");
        }

        // ---------------------------------------------------------------- 6. chest: an interrupted hold gives nothing (damage, moving away)
        if (Sec("interrupt"))
        {
            foreach (var how in new[] { "damage", "away" })
            {
                var cp = O + new Vector3(how == "damage" ? 4 : -2, 0, 4);
                var chest = Spawn<GlowChestObj>("bchest", cp, GlowChestObj.MakeData(1, "closed", false)); chest.Depth = 1;
                Put("Alice", cp + new Vector3(2.3f, 0.1f, 0)); await Wait(0.8f);
                int pk0 = Pickups();
                g.L2Step("open", $"Alice|{chest.Id}");
                await Until(() => chest.State == "hold", 6f);
                await Wait(1.0f);
                if (how == "damage") alice.TakeDamage(4f, null, "fall");
                else Put("Alice", cp + new Vector3(0f, 0.1f, -8f));
                bool back = await Until(() => chest.State == "closed", 3f);
                await Wait(3.5f);
                Chk("chest_interrupted_" + how, back && Pickups() == pk0 && chest.State == "closed", $"state={chest.State} new pickups={Pickups() - pk0}");
                g.L2Step("idle", "");
                alice.Health = 100f; alice.Dirty = true;
                await Wait(0.5f);
            }
        }

        // ---------------------------------------------------------------- 7. mimic: snaps when the hold completes, holds 4 s (30 damage, rooted, lantern out), shrieks, spits bait, runs away, settles, replicates
        Vector3 mimicSettled = default;
        GlowChestObj m1 = null;
        if (Sec("mimic"))
        {
            var cp = O + new Vector3(-8, 0, 6);
            m1 = Spawn<GlowChestObj>("bchest", cp, GlowChestObj.MakeData(0, "closed", false)); m1.Depth = 1; m1.ServerMakeMimic(true);
            Put("Alice", cp + new Vector3(2.3f, 0.1f, 0)); Put("Host", O + new Vector3(8, 0.1f, 8)); await Wait(0.8f);
            int pk0 = Pickups(); _noise.Clear(); LootStats.BiteDamage = 0;
            float tell = 0;
            g.L2Step("open", $"Alice|{m1.Id}");
            bool bit = await Until(() => m1.State == "bite", 8f);
            double tb = Now; var apos = alice.Pos; float hp0 = alice.Health;
            await Wait(0.8f);
            Chk("mimic_lantern_out", alice.Items.LanternOutUntil > 0, "");
            Chk("mimic_shriek_noise", _noise.Any(n => Math.Abs(n.r - LootTuning.ShriekNoise) < 0.01f), $"noise={string.Join(",", _noise.Select(n => n.r.ToString("0")))}");
            float maxMove = 0;
            while (m1.State == "bite" && Now - tb < 6) { maxMove = Math.Max(maxMove, alice.Pos.DistanceTo(apos)); await Wait(0.1f); }
            double dur = Now - tb;
            Chk("mimic_bites", bit, "");
            Chk("mimic_holds_4s", dur > 3.6 && dur < 4.6, $"{dur:0.00}s");
            Chk("mimic_victim_cannot_move", maxMove < 0.9f, $"moved {maxMove:0.00} m");
            Chk("mimic_30_damage", Math.Abs(LootStats.BiteDamage - 30f) < 1.5f && hp0 - alice.Health > 20f, $"dealt {LootStats.BiteDamage:0.#} hp {hp0:0.#} -> {alice.Health:0.#} (Regen stein heals meanwhile)");
            await Wait(0.5f);
            Chk("mimic_lantern_back", alice.Items.LanternOutUntil <= 0, "");
            Chk("mimic_spits_bait", Pickups() - pk0 >= 1 && Pickups() - pk0 <= 2, $"bait={Pickups() - pk0}");
            bool runs = await Until(() => m1.State == "run", 3f);
            bool settled = await Until(() => m1.State == "closed", 30f);
            mimicSettled = m1.GlobalPosition;
            float away = new Vector2(mimicSettled.X - cp.X, mimicSettled.Z - cp.Z).Length();
            Chk("mimic_relocates", runs && settled && away > 8f, $"moved {away:0.0} m");
            Chk("mimic_is_chest_again", m1.State == "closed" && m1.Mimic, "");
            await Wait(0.5f);
            g.L2Step("checkpos", $"{m1.Id}|{mimicSettled.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{mimicSettled.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            await Wait(1.0f);
            alice.Health = 100f; alice.Dirty = true;
        }

        // ---------------------------------------------------------------- 8. a hit snaps a mimic (the hitter is bitten); unkillable
        if (Sec("mimic") && m1 != null)
        {
            Put("Host", mimicSettled + new Vector3(2.2f, 0.1f, 0)); Put("Alice", O + new Vector3(8, 0.1f, -8)); await Wait(1.0f);
            g.L2Step("hit", $"Host|{m1.Id}");
            bool bit = await Until(() => m1.State == "bite", 8f);
            Chk("mimic_snaps_when_hit", bit && m1.VictimPeer == host.Peer, $"victim={m1.VictimPeer}");
            g.L2Step("idle", "");
            await Until(() => m1.State == "run", 8f);
            await Until(() => m1.State == "closed", 30f);
            Chk("mimic_unkillable", g.World.Objects.ContainsKey(m1.Id), "");
            host.Health = 100f; host.Dirty = true;
        }

        // ---------------------------------------------------------------- 9. a second miner hitting the mimic during the bite frees the victim early
        if (Sec("free"))
        {
            var cp = O + new Vector3(0, 0, 6);
            var m2 = Spawn<GlowChestObj>("bchest", cp, GlowChestObj.MakeData(1, "closed", false)); m2.Depth = 1; m2.ServerMakeMimic(true); m2.Rooted = true;
            Put("Alice", cp + new Vector3(0, 0.1f, 2.3f)); Put("Host", cp + new Vector3(-8, 0.1f, 0)); await Wait(1.0f);
            float hp0 = alice.Health; LootStats.BiteDamage = 0;
            g.L2Step("open", $"Alice|{m2.Id}");
            await Until(() => m2.State == "bite", 8f);
            double tb = Now;
            Put("Host", cp + new Vector3(-2.2f, 0.1f, 0));
            await Wait(0.8f);
            g.L2Step("hit", $"Host|{m2.Id}");
            await Until(() => m2.State != "bite", 6f);
            double dur = Now - tb;
            Chk("mimic_freed_by_second_miner", dur < 3.4, $"bite lasted {dur:0.00}s");
            Chk("mimic_freed_less_damage", LootStats.BiteDamage < 22f && LootStats.BiteDamage > 3f, $"dealt {LootStats.BiteDamage:0.#} of 30");
            g.L2Step("idle", "");
            await Until(() => m2.State == "closed", 10f);
            Chk("mimic_rooted_stays", m2.GlobalPosition.DistanceTo(cp) < 0.5f, "");
            alice.Health = 100f; alice.Dirty = true; host.Health = 100f; host.Dirty = true;
        }

        // ---------------------------------------------------------------- 10. stun: a bear trap / dynamite stuns a mimic for 3 s; it cannot be opened meanwhile
        if (Sec("stun"))
        {
            var cp = O + new Vector3(-8, 0, -4);
            var m3 = Spawn<GlowChestObj>("bchest", cp, GlowChestObj.MakeData(0, "closed", false)); m3.Depth = 1; m3.ServerMakeMimic(true); m3.Rooted = true;
            await Wait(0.3f);
            ((IRootable)m3).ServerRoot(3f);
            await Wait(0.2f);
            Chk("mimic_stunned", m3.State == "stun", $"state={m3.State}");
            Chk("mimic_stun_blocks_opening", !m3.ServerStartHold(alice), "");
            await Wait(3.4f);
            Chk("mimic_stun_ends", m3.State == "closed", $"state={m3.State}");
            ((IExplodable)m3).OnExplosion(cp, 6f, 40f);
            await Wait(0.2f);
            Chk("mimic_stunned_by_dynamite", m3.State == "stun", $"state={m3.State}");
        }
        await Wait(2f);
        Util.Pass("l2_done");
        g.L2EndRoundIn(3f);
    }
}
