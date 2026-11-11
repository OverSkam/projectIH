using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Test scaffolding (command-line only, honoured by the host / bot clients):
///   --give=[Name=]stein:id|util:id,...   replace starting items in round 1 (no name = everybody)
///   --debugspawn=chest,plate,blood,timed,infinity   spawn showcase objects in the camp
///   --itest=util|infinity                 host runs a scripted scenario; --bot=t_* clients follow its steps
///   --rounds=N                            round-count override
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
        if (g.Round == 1 && Util.HasArg("give")) ApplyGive(g, Util.Arg("give"));
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

/// <summary>Host-only scripted scenarios. Verifies server state and prints PASS/FAIL lines; bots on the clients do the player-side actions.</summary>
public partial class ItemsDirector : Node
{
    int _round = -1, _idx;
    double _t;
    List<(double t, Action a)> _script = new();
    bool _noiseDyn, _noisePeb, _verified;
    float _bobMaxY;
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

    public override void _Process(double delta)
    {
        var g = Game.I;
        if (!Net.I.IsServer) return;
        if (g.Phase == GamePhase.Summary && !_verified) { _verified = true; AfterRoundEnd(g); }
        if (g.Phase != GamePhase.Round) return;
        if (g.Round != _round)
        {
            _round = g.Round; _t = 0; _idx = 0; _verified = false; _bobMaxY = 0;
            _script = Scenario == "infinity" ? InfinityScript(g, _round) : UtilScript(g, _round);
        }
        _t += delta;
        if (P("Bob") is ServerPlayer bb) _bobMaxY = Math.Max(_bobMaxY, bb.Pos.Y);
        while (_idx < _script.Count && _t >= _script[_idx].t) _script[_idx++].a();
    }

    void AfterRoundEnd(Game g)
    {
        if (Scenario == "util" && _round == 3)
        {
            var h = P("Host"); var a = P("Alice");
            Chk("blood_penalty_verified", !h.Inv.Steins.Contains("blood") && h.Money == 80, $"host stein={string.Join(",", h.Inv.Steins)} money={h.Money}");
            Chk("blood_kept_verified", a.Inv.Steins.Contains("blood") && a.Money == 160, $"alice stein={string.Join(",", a.Inv.Steins)} money={a.Money}");
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
            At(27, () => g.KillMiner(P("Alice"), null, "collapse"));
            At(28, g.DebugEndRound);
        }
        else if (round == 3)
        {
            At(0.5, () =>
            {
                foreach (var n in new[] { "Alice", "Host" })
                {
                    var p = P(n);
                    Chk("respawn_kit_and_stein", p.Inv.Utils[0] == "pickaxe" && p.Inv.Utils[1] == "lantern" && p.Inv.Steins.Count == 1 && Steins.Get(p.Inv.Steins[0]).Rarity == Rarity.Common, $"{n} utils={string.Join(",", p.Inv.Utils)} steins={string.Join(",", p.Inv.Steins)}");
                    p.Inv.Steins.Clear(); p.Inv.Steins.Add("blood"); p.Money = 200; p.RecalcStats();
                }
            });
            At(2, () => g.KillMiner(P("Bob"), P("Alice"), "melee"));
            At(5, g.DebugEndRound);
        }
        return s;
    }

    void Give(ServerPlayer p, string item) => ItemsDebug.Give(p, item, new HashSet<ServerPlayer> { p });

    List<(double, Action)> InfinityScript(Game g, int round)
    {
        var s = new List<(double, Action)>();
        void At(double t, Action a) => s.Add((t, a));
        if (round == 2)
        {
            At(1, () => g.DebugStep("dropstein", "Alice"));
            At(3, () => { Chk("infinity_reset_on_drop", InfinityTracker.Holder == -1 && InfinityTracker.Count == 0 && !P("Alice").Inv.Steins.Contains("infinity")); g.DebugStep("pickstein", "Bob"); });
            At(8, () => { Chk("infinity_transferred", P("Bob").Inv.Steins.Contains("infinity")); g.DebugEndRound(); });
        }
        else At(4, g.DebugEndRound);
        return s;
    }
}
