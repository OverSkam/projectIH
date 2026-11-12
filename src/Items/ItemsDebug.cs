using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Test scaffolding (command-line only, honoured by the host / bot clients):
///   --give=[Name=]stein:id|util:id|ore:id,...   replace starting items in round 1 (no name = everybody)
///   --debugspawn=chest,plate,blood,timed,infinity   spawn showcase objects in the Hall
///   --itest=util|infinity                 host runs a scripted scenario; --bot=t_* clients follow its steps
/// </summary>
public static class ItemsDebug
{
    public static int StepSerial;
    public static string Step = "", StepArg = "";
    public static double StepAt;
    public static int StepRound;

    public static void Register()
    {
        if (Util.HasArg("itest") && Util.HasArg("host"))
            Game.I.CallDeferred(Node.MethodName.AddChild, new ItemsDirector { Name = "ItemsDirector" });
    }

    public static BotBrain MakeBot(string routine) => routine.StartsWith("t_") ? new ItemTestBot(routine) : new BotBrain(routine);

    static ServerPlayer ByName(Game g, string n) => g.Players.Values.FirstOrDefault(p => p.Name == n);

    public static void OnRoundPopulated(Game g)
    {
        if (!Net.I.IsServer) return;
        if ((g.Round == 1 || Util.HasArg("giveeach")) && Util.HasArg("give")) ApplyGive(g, Util.Arg("give"));
        if (Util.HasArg("debugspawn")) DebugSpawn(g, Util.Arg("debugspawn"));
    }

    public static void Give(ServerPlayer p, string item, HashSet<ServerPlayer> cleared)
    {
        int c = item.IndexOf(':'); string kind = item.Substring(0, c), id = item.Substring(c + 1);
        if (kind == "stein")
        {
            if (cleared.Add(p)) p.Inv.Steins.Clear();
            p.Inv.Steins.Add(id);
        }
        else if (kind == "util") { p.Inv.Utils[1] = id; p.Inv.Selected = 1; }
        else if (kind == "ore") p.Inv.Sack.Add(id);
        p.RecalcStats();
        p.Dirty = true;
    }

    static void ApplyGive(Game g, string spec)
    {
        var cleared = new HashSet<ServerPlayer>();
        foreach (var tok in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string name = null, item = tok;
            int eq = tok.IndexOf('=');
            if (eq > 0) { name = tok.Substring(0, eq); item = tok.Substring(eq + 1); }
            foreach (var p in g.Players.Values.Where(p => p.Participant && (name == null || p.Name == name))) Give(p, item, cleared);
        }
        Util.Log("DEBUG: --give applied: " + spec);
    }

    static void DebugSpawn(Game g, string spec)
    {
        var rng = new RandomNumberGenerator(); rng.Randomize();
        float x = -6f;
        foreach (var tok in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pos = new Vector3(x, 0, 0); x += 4f;
            switch (tok)
            {
                case "chest": g.ServerSpawnObject("chest", pos, "open=0"); break;
                case "infinity": g.ServerSpawnObject("pickup", pos + new Vector3(0, 0.7f, 0), "stein:infinity"); break;
                case "steins": { float xx = pos.X - 1.5f; foreach (var id in Steins.All.Keys) { g.ServerSpawnObject("pickup", new Vector3(xx, 0.7f, 2.5f), "stein:" + id); xx += 0.6f; } break; }
                case "plate": ItemsModule.SpawnShrineOfType(g, pos + new Vector3(0, 0, -4), "plate", rng, "luck"); break;
                case "blood": ItemsModule.SpawnShrineOfType(g, pos + new Vector3(0, 0, -4), "blood", rng, "blood"); break;
                case "timed": ItemsModule.SpawnShrineOfType(g, pos + new Vector3(0, 0, -4), "timed", rng, "ethereal"); break;
                case "utils": { int i = 0; foreach (var u in new[] { "rope", "gasmask", "map", "dynamite", "beartrap" }) g.ServerSpawnObject("pickup", new Vector3(pos.X - 1 + i++ * 0.5f, 0.4f, 3f), "util:" + u); break; }
            }
        }
    }

    // ------------------------------------------------------------ client side: scripted steps
    public static void OnStep(string step, string arg)
    {
        Step = step; StepArg = arg; StepAt = Time.GetTicksMsec() / 1000.0; StepSerial++;
        string bot = Util.Arg("bot");
        if (!bot.StartsWith("t_")) return;
        var g = Game.I; string me = Net.I.LocalName;
        int PeerOf(string n) => Net.I.Names.FirstOrDefault(kv => kv.Value == n).Key;
        switch (step)
        {
            case "maskcheck":
                if (me != arg && g.World?.GetMiner(PeerOf(arg)) is Miner mm)
                { if (mm.HasMask) Util.Pass("mask_visible_remote", $"viewer={me}"); else Util.Fail("mask_visible_remote", $"viewer={me}"); }
                break;
            case "lanterncheck":
                if (g.World?.GetMiner(PeerOf(arg)) is Miner lm)
                { if (lm.LanternOut) Util.Pass("lantern_out_seen", $"viewer={me}"); else Util.Fail("lantern_out_seen", $"viewer={me}"); }
                break;
            case "meddle": case "meddle2":
                if (g.LocalGhost != null && g.World?.GetMiner(PeerOf(arg)) is Miner vm)
                {
                    g.LocalGhost.GlobalPosition = vm.GlobalPosition + Vector3.Up * 1.5f;
                    g.SendGhostMeddle(g.LocalGhost.GlobalPosition, step == "meddle" ? 0 : 1);
                }
                break;
            case "dropstein":
                if (me == arg) { int i = g.OwnInv.Steins.IndexOf("infinity"); if (i >= 0) g.SendDropStein(i); }
                break;
            case "lanternstate":   // arg "Name:out|low|on": every other client checks the PUBLIC state of that miner's light
            {
                var kv = arg.Split(':');
                if (me != kv[0] && g.World?.GetMiner(PeerOf(kv[0])) is Miner om)
                {
                    string have = om.LanternOut ? "out" : om.LanternLow ? "low" : "on";
                    if (have == kv[1]) Util.Pass("lantern_" + kv[1] + "_seen", $"viewer={me}"); else Util.Fail("lantern_" + kv[1] + "_seen", $"viewer={me} saw={have}");
                }
                break;
            }
            case "quit":
                if (me == arg) { Util.Log("TEST: quitting on request"); Net.I.Disconnect(); g.GetTree().Quit(); }
                break;
        }
    }
}

/// <summary>Bot that follows director steps (see ItemsDirector). Uses only its own client's information.</summary>
public class ItemTestBot : BotBrain
{
    float _yaw, _pitch;
    int _serial = -1;
    double _t;
    bool _flag, _flag2;
    string _snap = "";
    Vector3 _p0;
    float _y0;

    public ItemTestBot(string routine) : base("idle") { }

    static T Nearest<T>(Game g, Vector3 from, Func<T, bool> ok = null) where T : NetObject
    {
        T best = null; float bd = float.MaxValue;
        foreach (var o in g.World.Objects.Values)
            if (o is T t && (ok == null || ok(t))) { float d = o.GlobalPosition.DistanceTo(from); if (d < bd) { bd = d; best = t; } }
        return best;
    }

    void FaceTo(Miner m, ref MinerInput i, Vector3 pt)
    {
        var d = pt - m.EyePos;
        _yaw = Mathf.Atan2(-d.X, -d.Z);
        _pitch = Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), -1.4f, 1.4f);
    }

    /// <summary>Walk straight towards a point; returns true when within stop metres.</summary>
    bool Walk(Miner m, ref MinerInput i, Vector3 target, float stop)
    {
        var flat = new Vector3(target.X - m.GlobalPosition.X, 0, target.Z - m.GlobalPosition.Z);
        _yaw = Mathf.Atan2(-flat.X, -flat.Z); _pitch = 0;
        if (flat.Length() <= stop) return true;
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
        if (_serial != ItemsDebug.StepSerial)
        {
            _serial = ItemsDebug.StepSerial; _t = 0; _flag = _flag2 = false;
            _snap = g.OwnInv.Encode(); _p0 = m.GlobalPosition; _y0 = m.GlobalPosition.Y;
        }
        _t += dt;
        string me = Net.I.LocalName, arg = ItemsDebug.StepArg;
        switch (ItemsDebug.Step)
        {
            case "chest":
            {
                if (me == "Alice")
                {
                    var c = Nearest<ChestObj>(g, m.GlobalPosition);
                    if (c != null && Walk(m, ref i, c.GlobalPosition, 1.9f) && (int)(_t * 2) % 2 == 0) { FaceTo(m, ref i, c.GlobalPosition + Vector3.Up * 0.5f); g.SendInteract(c.Id); }
                    if (!_flag && g.OwnInv.Steins.Contains("swift")) { _flag = true; Util.Pass("chest_loot_received", $"inv={g.OwnInv.Encode()}"); }
                    if (!_flag2 && m.SpeedMul > 1.1f) { _flag2 = true; Util.Pass("own_swift_applied", $"speed={m.SpeedMul}"); }
                }
                else if (_t > 7 && !_flag)
                {
                    _flag = true;
                    if (g.OwnInv.Encode() == _snap && Math.Abs(m.SpeedMul - 1f) < 0.01f) Util.Pass("no_foreign_loot", $"me={me}");
                    else Util.Fail("no_foreign_loot", $"me={me} snap={_snap} now={g.OwnInv.Encode()} speed={m.SpeedMul}");
                }
                break;
            }
            case "dynamite": case "trap": case "rope_place":
            {
                string who = ItemsDebug.Step == "dynamite" ? "Bob" : ItemsDebug.Step == "trap" ? "Host" : "Alice";
                if (me != who) break;
                if (_t < 0.3) g.SendSelectUtil(1);
                _yaw = ItemsDebug.Step == "rope_place" ? Mathf.Pi : 0; _pitch = 0;
                if (_t > 1.0 && !_flag) { _flag = true; i.UseUtility = true; }
                break;
            }
            case "steptrap":
            {
                if (me != "Alice") break;
                var tr = Nearest<BearTrapObj>(g, m.GlobalPosition);
                if (tr == null) break;
                if (m.RootLeft > 0 && !_flag) { _flag = true; _p0 = m.GlobalPosition; _t = 0.001; }
                if (!_flag) Walk(m, ref i, tr.GlobalPosition, 0.1f);
                else
                {
                    Walk(m, ref i, tr.GlobalPosition + (tr.GlobalPosition - _p0).Normalized() * 3f + new Vector3(2, 0, 0), 0.1f); // keep pushing away
                    if (m.RootLeft <= 0 && !_flag2) { _flag2 = true; Util.Log($"rootmoved={_p0.DistanceTo(m.GlobalPosition):0.00}"); if (_p0.DistanceTo(m.GlobalPosition) < 0.5f) Util.Pass("root_held"); else Util.Fail("root_held"); }
                }
                break;
            }
            case "rope_climb":
            {
                if (me != "Bob") break;
                var r = Nearest<RopeObj>(g, m.GlobalPosition);
                if (r == null) break;
                _yaw = 0; _pitch = 0;
                if (m.GlobalPosition.Y - _y0 < 1.4f && !_flag) i.Move = new Vector2(0, 1);
                else if (!_flag) { _flag = true; _t = 0.001; Util.Pass("rope_climbed", $"gain={m.GlobalPosition.Y - _y0:0.0}"); }
                else if (_t < 0.6) { i.Jump = true; i.Move = new Vector2(1, 0); }
                break;
            }
            case "rope_cut":
            {
                if (me != "Alice") break;
                if (_t < 0.3) g.SendSelectUtil(0);
                var r = Nearest<RopeObj>(g, m.GlobalPosition);
                if (r == null) { if (!_flag) { _flag = true; Util.Pass("rope_gone_seen"); } break; }
                var pt = r.GlobalPosition + new Vector3(0, -0.35f, 0);
                FaceTo(m, ref i, pt);
                if (_t > 0.8) i.Attack = true;
                break;
            }
            case "oil_toggle":
                if (me == "Alice" && !_flag) { _flag = true; i.ToggleLantern = true; }
                break;
            case "oil_refill":
            {
                if (me != "Alice") break;
                var d = Nearest<OilDrumObj>(g, m.GlobalPosition);
                if (d != null && Walk(m, ref i, d.GlobalPosition, 1.8f) && (int)(_t * 2) % 2 == 0) { FaceTo(m, ref i, d.GlobalPosition + Vector3.Up * 0.5f); g.SendInteract(d.Id); }
                break;
            }
            case "loot":
            {
                if (me != "Alice") break;
                int hp = Net.I.Names.FirstOrDefault(kv => kv.Value == arg).Key;
                if (g.World.GetMiner(hp) is Miner victim && Walk(m, ref i, victim.GlobalPosition, 1.4f))
                {
                    FaceTo(m, ref i, victim.GlobalPosition + Vector3.Up * 0.2f);
                    if ((int)(_t * 1.5) != (int)((_t - dt) * 1.5)) g.SendLoot(hp);
                }
                break;
            }
            case "pickstein":
            {
                if (me != arg) break;
                var p = Nearest<PickupObj>(g, m.GlobalPosition, o => o.Data.StartsWith("stein:"));
                if (p != null && Walk(m, ref i, p.GlobalPosition, 1.5f) && (int)(_t * 2) % 2 == 0) g.SendInteract(p.Id);
                break;
            }
        }
    }
}

/// <summary>Host-only scripted scenarios. Verifies server state and prints PASS/FAIL lines; bots on the clients do the player-side actions.
/// Scenarios (--itest=): util (5 miners, 3 rounds of checks), infinity (4 miners), tie (3 miners), disconnect (3 miners).
/// Elimination is steered by setting tribute weights (Weights(...)) shortly before the round is ended.</summary>
public partial class ItemsDirector : Node
{
    int _round = -1, _idx;
    double _t;
    List<(double t, Action a)> _script = new();
    bool _noiseDyn, _noisePeb, _verified;
    float _bobMaxY;
    double _oil0, _oil1;
    string Scenario => Util.Arg("itest");

    public override void _Ready()
    {
        Game.NoiseMade += (pos, r, by) =>
        {
            if (r >= 30f) _noiseDyn = true;
            if (by == null && Math.Abs(r - 14f) < 0.1f) _noisePeb = true;
        };
    }

    ServerPlayer P(string n) => Game.I.Players.Values.FirstOrDefault(p => p.Name == n);
    void Chk(string name, bool ok, string d = "") { if (ok) Util.Pass(name, d); else Util.Fail(name, d); }
    void Weights(params (string name, int w)[] ws) { foreach (var (n, w) in ws) if (P(n) is ServerPlayer p) { p.TributeWeight = w; p.Dirty = true; } }

    public override void _Process(double delta)
    {
        var g = Game.I;
        if (!Net.I.IsServer) return;
        if (g.Phase == GamePhase.Weighing && !_verified) { _verified = true; AfterRoundEnd(g); }
        if (g.Phase != GamePhase.Round) return;
        if (g.Round != _round)
        {
            _round = g.Round; _t = 0; _idx = 0; _verified = false; _bobMaxY = 0;
            _script = Scenario switch { "infinity" => InfinityScript(g, _round), "tie" => TieScript(g, _round), "disconnect" => DisconnectScript(g, _round), _ => UtilScript(g, _round) };
        }
        _t += delta;
        if (P("Bob") is ServerPlayer bb) _bobMaxY = Math.Max(_bobMaxY, bb.Pos.Y);
        while (_idx < _script.Count && _t >= _script[_idx].t) _script[_idx++].a();
    }

    /// <summary>Called once at the start of every weighing (the lock-out and the RoundEnd events, e.g. the Blood penalty, have just run).</summary>
    void AfterRoundEnd(Game g)
    {
        if (Scenario == "util" && _round == 3)
        {
            var h = P("Host"); var a = P("Alice");
            Chk("blood_penalty_verified", !h.Inv.Steins.Contains("blood") && h.TributeWeight == 100, $"host stein={string.Join(",", h.Inv.Steins)} tribute={h.TributeWeight}");
            Chk("blood_kept_verified", a.Inv.Steins.Contains("blood") && a.TributeWeight == 200, $"alice stein={string.Join(",", a.Inv.Steins)} tribute={a.TributeWeight}");
        }
    }

    List<(double, Action)> UtilScript(Game g, int round)
    {
        var s = new List<(double, Action)>();
        void At(double t, Action a) => s.Add((t, a));
        if (round == 1)
        {
            At(2, () =>
            {
                var a = P("Alice"); var b = P("Bob");
                int id = g.ServerSpawnObject("chest", a.Pos + new Vector3(0, 0, -2.5f), "open=0");
                ((ChestObj)g.World.Objects[id]).ForcedLoot = new List<string> { "stein:swift" };
                g.ServerSpawnObject("vein", b.Pos + new Vector3(2.5f, 0, -3.2f), VeinObj.MakeData("gold", 4, 4));
                g.DebugStep("chest");
            });
            At(10, () =>
            {
                var a = P("Alice"); var b = P("Bob"); var h = P("Host");
                Chk("chest_loot_to_opener_only", a.Inv.Steins.Contains("swift") && b.Inv.Steins.SequenceEqual(new[] { "regen" }) && h.Inv.Steins.SequenceEqual(new[] { "regen" }),
                    $"alice={string.Join(",", a.Inv.Steins)} bob={string.Join(",", b.Inv.Steins)} host={string.Join(",", h.Inv.Steins)}");
                Chk("stein_stats_owner_only", Math.Abs(a.Stats.SpeedMul - 1.2f) < 0.01f && Math.Abs(b.Stats.SpeedMul - 1f) < 0.01f, $"alice={a.Stats.SpeedMul} bob={b.Stats.SpeedMul}");
                float ha = a.Health, hb = b.Health;
                a.TakeDamage(10, null, "gas"); b.TakeDamage(10, null, "gas");
                Chk("gasmask_immune", a.Stats.GasImmune && a.Health == ha, $"hp {ha}->{a.Health}");
                Chk("gas_hurts_unprotected", b.Health < hb, $"hp {hb}->{b.Health}");
                g.DebugStep("maskcheck", "Alice");
            });
            At(13, () => { _noiseDyn = false; g.DebugStep("dynamite"); });
            At(20, () => Chk("dynamite_noise", _noiseDyn));
            At(21, () => g.DebugStep("trap"));
            At(24, () => g.DebugStep("steptrap"));
            // Midas: Bob kills Dave: a gold statue (one stone, weight 15) instead of a bag
            At(30, () =>
            {
                var b = P("Bob"); b.Inv.Steins.Add("midas"); b.RecalcStats();
                int bags0 = g.World.Objects.Values.Count(o => o is BagObj);
                g.KillMiner(P("Dave"), b, "melee");
                bool statue = g.World.Objects.Values.Any(o => o is PickupObj && o.Data == "ore:statue");
                Chk("midas_statue_verified", statue && g.World.Objects.Values.Count(o => o is BagObj) == bags0 && Ores.WeightOf("statue") == 15, $"statue={statue}");
            });
            At(32, () => Weights(("Host", 10), ("Alice", 12), ("Bob", 9), ("Carol", 1), ("Dave", 5)));   // Carol is the lightest
            At(33, g.DebugEndRound);
        }
        else if (round == 2)
        {
            At(0.5, () =>
            {
                var a = P("Alice"); var b = P("Bob");
                Give(a, "util:rope"); Give(b, "util:lantern");
                g.ServerSpawnObject("dbgplatform", new Vector3(5, 0, -3), "w=4;h=2.6");
            });
            At(1.5, () =>
            {
                g.DebugTeleport(P("Alice"), new Vector3(5, 2.7f, -1.2f), Mathf.Pi);
                g.DebugTeleport(P("Bob"), new Vector3(5, 0.1f, 0.45f), 0);
            });
            At(3, () => g.DebugStep("rope_place"));
            At(6, () => g.DebugStep("rope_climb"));
            At(11, () => { Chk("rope_climbed_server", _bobMaxY > 1.2f, $"maxY={_bobMaxY:0.0}"); g.DebugStep("rope_cut"); });
            At(17, () => g.KillMiner(P("Host"), null, "test"));
            At(18.5, () => g.DebugStep("meddle", "Bob"));
            At(20, () => g.DebugStep("lanterncheck", "Bob"));
            At(25, () => { _noisePeb = false; g.DebugStep("meddle2", "Bob"); });
            At(26, () => Chk("ghost_pebble_noise", _noisePeb));
            At(27, () => g.KillMiner(P("Alice"), null, "test"));
            At(27.5, () => Weights(("Host", 30), ("Alice", 40), ("Bob", 25), ("Dave", 3)));   // dead miners' fed tribute still counts; Dave is the lightest
            At(28, g.DebugEndRound);
        }
        else if (round == 3)
        {
            At(0.5, () =>
            {
                foreach (var n in new[] { "Alice", "Host" })
                {
                    var p = P(n);
                    Chk("respawn_kit_only", p.Inv.Utils[0] == "pickaxe" && p.Inv.Utils[1] == "lantern" && p.Inv.Steins.Count == 0, $"{n} utils={string.Join(",", p.Inv.Utils)} steins={string.Join(",", p.Inv.Steins)}");
                    p.Inv.Steins.Clear(); p.Inv.Steins.Add("blood"); p.TributeWeight = 200; p.RecalcStats();
                }
                P("Bob").TributeWeight = 50;
            });
            At(2, () => g.KillMiner(P("Bob"), P("Alice"), "melee"));
            At(5, g.DebugEndRound);
        }
        else if (round == 4)
        {
            // final two (Host, Alice): lantern oil, then downed -> looted -> bleeds out, then downed -> hit again -> dies
            At(0.5, () =>
            {
                Weights(("Host", 50), ("Alice", 80));
                var a = P("Alice");
                Chk("oil_starts_full", Math.Abs(a.Oil - g.Config.LanternOilSeconds) < 2f && a.LanternOn && a.Inv.Held == "lantern", $"oil={a.Oil:0} held={a.Inv.Held}");
                _oil0 = a.Oil;
            });
            At(4, () => { var a = P("Alice"); Chk("oil_drains_while_lit", _oil0 - a.Oil > 2.5 && _oil0 - a.Oil < 4.5, $"drained={_oil0 - a.Oil:0.0}"); g.DebugStep("oil_toggle"); });
            At(5, () => { var a = P("Alice"); Chk("lantern_toggled_off", !a.LanternOn, $"on={a.LanternOn}"); _oil1 = a.Oil; g.DebugStep("lanternstate", "Alice:out"); });
            At(8, () => { var a = P("Alice"); Chk("oil_not_burning_when_off", Math.Abs(_oil1 - a.Oil) < 0.2, $"oil {_oil1:0.0}->{a.Oil:0.0}"); g.DebugStep("oil_toggle"); });
            At(9, () => { var a = P("Alice"); Chk("lantern_toggled_on", a.LanternOn, $"on={a.LanternOn}"); a.Oil = 30f; a.Dirty = true; });
            At(10.5, () => g.DebugStep("lanternstate", "Alice:low"));
            At(11, () =>
            {
                var a = P("Alice");
                int id = g.ServerSpawnObject("oil", a.Pos + new Vector3(0, 0, -2.5f), OilDrumObj.MakeData(2));
                g.DebugStep("oil_refill");
            });
            At(16, () => { var a = P("Alice"); Chk("oil_refilled_to_full", a.Oil > g.Config.LanternOilSeconds - 3f, $"oil={a.Oil:0}"); a.Oil = 1.5f; });
            At(18, () => { Chk("lantern_out_when_oil_gone", P("Alice").Oil <= 0f); g.DebugStep("lanternstate", "Alice:out"); P("Alice").Oil = g.Config.LanternOilSeconds; });
            At(19, () =>
            {
                // downed -> looted -> bleeds out
                var h = P("Host"); var a = P("Alice");
                h.Inv.Sack.Add("gold"); h.Inv.Sack.Add("coal"); h.Inv.Steins.Add("swift"); h.RecalcStats();
                h.TakeDamage(500, a, "test");
                Chk("downed_state_set", h.Alive && h.Downed && h.Health <= 0f, $"alive={h.Alive} downed={h.Downed}");
                Chk("downed_cannot_act", !g.CanAct(h));
                g.DebugStep("loot", "Host");
            });
            At(25, () =>
            {
                var h = P("Host"); var a = P("Alice");
                Chk("downed_looted_by_other", a.Inv.Sack.Contains("gold") && a.Inv.Sack.Contains("coal") && a.Inv.Steins.Contains("swift") && h.Inv.Sack.Count == 0 && h.Inv.Steins.Count == 0,
                    $"alice sack={string.Join(",", a.Inv.Sack)} steins={string.Join(",", a.Inv.Steins)} host sack={h.Inv.Sack.Count}");
            });
            At(31, () => { var h = P("Host"); Chk("downed_bleeds_out", !h.Alive && !h.Downed, $"alive={h.Alive}"); });
            At(32, () =>
            {
                var a = P("Alice");
                a.TakeDamage(500, null, "test");
                Chk("downed_second_state", a.Alive && a.Downed);
                a.TakeDamage(3, null, "test");
                Chk("downed_further_damage_kills", !a.Alive && !a.Downed, $"alive={a.Alive}");   // both miners dead: the Goblin ends the round and weighs (final two, Alice heavier)
            });
        }
        return s;
    }

    void Give(ServerPlayer p, string item) => ItemsDebug.Give(p, item, new HashSet<ServerPlayer> { p });

    List<(double, Action)> InfinityScript(Game g, int round)
    {
        var s = new List<(double, Action)>();
        void At(double t, Action a) => s.Add((t, a));
        if (round == 1)
        {
            At(1, () => g.DebugStep("dropstein", "Alice"));
            At(3, () => { Chk("infinity_reset_on_drop", InfinityTracker.Holder == -1 && InfinityTracker.Count == 0 && !P("Alice").Inv.Steins.Contains("infinity")); g.DebugStep("pickstein", "Bob"); });
            At(8, () => { Chk("infinity_transferred", P("Bob").Inv.Steins.Contains("infinity")); Weights(("Host", 10), ("Alice", 8), ("Bob", 12), ("Carol", 1)); g.DebugEndRound(); });   // Carol is thrown
        }
        else if (round == 2) At(4, () => { Weights(("Host", 10), ("Alice", 2), ("Bob", 12)); g.DebugEndRound(); });   // Alice is thrown
        else At(4, g.DebugEndRound);   // Bob holds the stein at the end of round 3: he wins at once (before any weighing)
        return s;
    }

    /// <summary>Two miners tied for lightest are both thrown; the remaining miner wins.</summary>
    List<(double, Action)> TieScript(Game g, int round)
    {
        var s = new List<(double, Action)>();
        s.Add((3, () => { Weights(("Host", 5), ("Alice", 5), ("Bob", 20)); g.DebugEndRound(); }));
        return s;
    }

    /// <summary>A miner who disconnects is eliminated; when only one miner remains the match ends with them as winner.</summary>
    List<(double, Action)> DisconnectScript(Game g, int round)
    {
        var s = new List<(double, Action)>();
        s.Add((3, () => g.DebugStep("quit", "Bob")));
        s.Add((9, () => { Chk("one_left_after_first_leaves", g.Contenders().Count == 2, $"contenders={g.Contenders().Count}"); g.DebugStep("quit", "Alice"); }));
        return s;
    }
}
