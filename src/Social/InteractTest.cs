using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Client side of the interaction test (--itest=interact, bots --bot=i_host / i_alice / i_bob): follows the director's steps with real requests and reports what it sees.</summary>
public static class InteractDebug
{
    public static int Serial;
    public static string Step = "", Arg = "";
    public static float Yaw; public static bool Crouch; public static bool PoseSet;
    static readonly HashSet<string> _seen = new();
    static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    public static void OnStep(string step, string arg)
    {
        Step = step; Arg = arg; Serial++;
        var g = Game.I; string me = Net.I.LocalName;
        int PeerOf(string n) => Net.I.Names.FirstOrDefault(kv => kv.Value == n).Key;
        switch (step)
        {
            case "pose":   // Name|x|y|z|yawDeg|crouch
            {
                var a = arg.Split('|');
                if (a[0] != me || g.LocalMiner == null) break;
                g.LocalMiner.Teleport(new Vector3(F(a[1]), F(a[2]), F(a[3])), 0);
                Yaw = Mathf.DegToRad(F(a[4])); Crouch = a[5] == "1"; PoseSet = true;
                break;
            }
            case "turn":   // Name|yawDeg
            {
                var a = arg.Split('|');
                if (a[0] == me) Yaw = Mathf.DegToRad(F(a[1]));
                break;
            }
            case "shove_now": if (arg == me) g.SendShove(); break;
            case "sackcheck":   // Name:step;Name:step : every client checks what the OTHER miners' models show, and that no exact value came with it
                foreach (var tok in arg.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = tok.Split(':');
                    if (kv[0] == me || g.World?.GetMiner(PeerOf(kv[0])) is not Miner m) continue;
                    if (m.SackStep == int.Parse(kv[1])) Util.Pass("sack_step_seen", $"viewer={me} miner={kv[0]} step={m.SackStep}");
                    else Util.Fail("sack_step_seen", $"viewer={me} miner={kv[0]} saw={m.SackStep} want={kv[1]}");
                    if (m.Burden == 0f) Util.Pass("sack_exact_not_public", $"viewer={me} miner={kv[0]}");
                    else Util.Fail("sack_exact_not_public", $"viewer={me} miner={kv[0]} burden={m.Burden}");
                }
                break;
            case "report":   // each client states what it knows about its own sack
                Util.Log($"INTERACT report me={me} sack={g.OwnInv.Sack.Count} inv={g.OwnInv.Encode()} downed={g.OwnDowned}");
                break;
        }
    }

    public static void Note(string key, string pass)
    {
        if (_seen.Add(key)) Util.Pass(pass);
    }
}

public class InteractBot : BotBrain
{
    int _serial = -1; double _t;
    bool _f1, _f2, _f3; int _lastRob = -1;
    public InteractBot() : base("idle") { }

    static T Nearest<T>(Game g, Vector3 from, Func<T, bool> ok = null) where T : NetObject
    {
        T best = null; float bd = float.MaxValue;
        foreach (var o in g.World.Objects.Values)
            if (o is T t && (ok == null || ok(t))) { float d = o.GlobalPosition.DistanceTo(from); if (d < bd) { bd = d; best = t; } }
        return best;
    }

    public override MinerInput Poll(float dt, Miner m)
    {
        var i = new MinerInput { UtilSlot = -1, Yaw = InteractDebug.Yaw };
        var g = Game.I;
        if (g.Phase == GamePhase.Round && g.World != null && m != null)
        {
            i.Crouch = InteractDebug.Crouch && !m.IsDowned;
            Act(dt, m, ref i, g);
        }
        return i;
    }

    void Act(float dt, Miner m, ref MinerInput i, Game g)
    {
        if (_serial != InteractDebug.Serial) { _serial = InteractDebug.Serial; _t = 0; _f1 = _f2 = _f3 = false; _lastRob = -1; }
        _t += dt;
        string me = Net.I.LocalName, arg = InteractDebug.Arg;
        int PeerOf(string n) => Net.I.Names.FirstOrDefault(kv => kv.Value == n).Key;
        // what a downed miner sees (the actors in the overlay) and what a dragged miner feels
        foreach (var kv in g.DownedActors) InteractDebug.Note("actor" + kv.Value.mode, "downed_actor_seen mode=" + kv.Value.mode + " by=" + Net.I.NameOf(kv.Key));
        if (m.IsDowned && m.DraggedBy >= 0) InteractDebug.Note("dragged", "dragged_client by=" + Net.I.NameOf(m.DraggedBy));
        var a = arg.Split('|');
        switch (InteractDebug.Step)
        {
            case "shove": if (arg == me && !_f1) { _f1 = true; i.Shove = true; } break;
            case "shove_spam":
                if (arg != me) break;
                if (!_f1) { _f1 = true; g.SendShove(); }
                if (!_f2 && _t >= 0.3) { _f2 = true; g.SendShove(); }
                if (!_f3 && _t >= 1.5) { _f3 = true; g.SendShove(); }
                break;
            case "runshove":   // Shover|Victim: sprint at the victim and shove when in reach
                if (a[0] != me) break;
                if (g.World.GetMiner(PeerOf(a[1])) is Miner vic)
                {
                    var to = vic.GlobalPosition - m.GlobalPosition; to.Y = 0;
                    InteractDebug.Yaw = Mathf.Atan2(-to.X, -to.Z); i.Yaw = InteractDebug.Yaw;
                    if (to.Length() > 1.35f) { i.Move = new Vector2(0, 1); i.Sprint = true; }
                    else if (!_f1) { _f1 = true; i.Shove = true; }
                }
                break;
            case "pick": case "revive":   // Who|Target|secs : hold E on the target (pickpocket / revive), as the human client does
            {
                if (a[0] != me) break;
                int kind = InteractDebug.Step == "pick" ? Game.HoldPick : Game.HoldRevive;
                if (!_f1) { _f1 = true; g.SendHold(kind, PeerOf(a[1]), true); }
                if (!_f2 && _t >= F(a[2])) { _f2 = true; g.SendHold(kind, PeerOf(a[1]), false); }
                break;
            }
            case "rob":   // Who|Target|n
                if (a[0] != me) break;
            {
                int k = (int)(_t / 0.7);
                if (k < int.Parse(a[2]) && k != _lastRob) { _lastRob = k; g.SendLoot(PeerOf(a[1])); }
                break;
            }
            case "drag":   // Who|Target|secs|yawDeg
                if (a[0] != me) break;
                if (!_f1) { _f1 = true; g.SendHold(Game.HoldDrag, PeerOf(a[1]), true); m.SetBotHold(Game.HoldDrag); }
                if (_t < F(a[2])) { i.Move = new Vector2(0, 1); InteractDebug.Yaw = Mathf.DegToRad(F(a[3])); i.Yaw = InteractDebug.Yaw; }
                else if (!_f2) { _f2 = true; g.SendHold(Game.HoldDrag, PeerOf(a[1]), false); m.SetBotHold(0); }
                break;
            case "pickup":
            {
                if (arg != me) break;
                var p = Nearest<PickupObj>(g, m.GlobalPosition, o => o.Data.StartsWith("ore:"));
                if (p == null) break;
                var flat = new Vector3(p.GlobalPosition.X - m.GlobalPosition.X, 0, p.GlobalPosition.Z - m.GlobalPosition.Z);
                InteractDebug.Yaw = Mathf.Atan2(-flat.X, -flat.Z); i.Yaw = InteractDebug.Yaw;
                if (flat.Length() > 1.3f) i.Move = new Vector2(0, 1);
                else if ((int)(_t * 2) != (int)((_t - dt) * 2)) g.SendInteract(p.Id);
                break;
            }
        }
        float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }
}

/// <summary>Host-only scripted scenario (--itest=interact, 3 miners Host / Alice / Bob): shove, spill, cooldown, hazards, pickpocket, sack steps, downed options. Prints PASS/FAIL lines.</summary>
public partial class InteractDirector : Node
{
    int _round = -1, _idx; double _t;
    List<(double t, Action a)> _script = new();
    bool _noiseLoud, _weighStarted;
    Vector3 _o, _da, _db; float _y;
    int _hits0, _blocked0, _miss0, _starts0;
    Vector3 _bobPos0, _hostPos0;
    List<string> _bobSack0 = new();
    int _plateId, _platId, _spillId; HashSet<int> _pk0 = new();

    ServerPlayer P(string n) => Game.I.Players.Values.FirstOrDefault(p => p.Name == n);
    void Chk(string name, bool ok, string d = "") { if (ok) Util.Pass(name, d); else Util.Fail(name, d); }
    static string I(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    static float YawOf(Vector3 d) => Mathf.Atan2(-d.X, -d.Z);

    public override void _Ready()
    {
        Game.NoiseMade += (pos, r, by) => { if (by != null && by.Name == "Alice" && r >= 20f) _noiseLoud = true; };
    }

    void Pose(string name, Vector3 at, Vector3 faceDir, bool crouch = false)
    {
        float yaw = YawOf(faceDir);
        var p = P(name); p.Pos = at; p.Yaw = yaw;
        Game.I.DebugStep("pose", $"{name}|{I(at.X)}|{I(at.Y)}|{I(at.Z)}|{I(Mathf.RadToDeg(yaw))}|{(crouch ? 1 : 0)}");
    }
    void Sack(string name, params string[] ores) { var p = P(name); p.Inv.Sack.Clear(); p.Inv.Sack.AddRange(ores); p.TributeAdj = 0; p.Dirty = true; }
    string SackOf(string n) => string.Join(",", P(n).Inv.Sack);

    /// <summary>Open floor of the Hall for the arena: the miner spawn with two long free directions at least 80 degrees apart.</summary>
    bool FindArena(Game g)
    {
        var cave = g.World.Cave; var space = g.World.GetWorld3D().DirectSpaceState;
        foreach (var spawn in cave.MinerSpawns.Concat(new[] { cave.ScalePos }))
        {
            var fq = Queries.Ray(spawn + Vector3.Up * 2f, spawn + Vector3.Down * 4f, 1);
            var fh = space.IntersectRay(fq); if (fh.Count == 0) continue;
            var o = fh["position"].AsVector3();
            var free = new float[16];
            for (int k = 0; k < 16; k++)
            {
                var d = new Vector3(Mathf.Cos(k * Mathf.Pi / 8f), 0, Mathf.Sin(k * Mathf.Pi / 8f));
                var q = Queries.Ray(o + Vector3.Up * 1.0f, o + Vector3.Up * 1.0f + d * 14f, 1 | 4);
                var h = space.IntersectRay(q);
                free[k] = h.Count == 0 ? 14f : (h["position"].AsVector3() - (o + Vector3.Up)).Length();
            }
            int a = Array.IndexOf(free, free.Max());
            if (free[a] < 11f) continue;
            int b = -1; float bf = 0;
            for (int k = 0; k < 16; k++)
            {
                int dk = Math.Min((k - a + 16) % 16, (a - k + 16) % 16);
                if (dk >= 4 && free[k] >= 10.5f && free[k] > bf) { bf = free[k]; b = k; }
            }
            if (b < 0) continue;
            _o = o; _y = o.Y;
            _da = new Vector3(Mathf.Cos(a * Mathf.Pi / 8f), 0, Mathf.Sin(a * Mathf.Pi / 8f));
            _db = new Vector3(Mathf.Cos(b * Mathf.Pi / 8f), 0, Mathf.Sin(b * Mathf.Pi / 8f));
            Util.Log($"INTERACT arena o={_o} da={_da} free={free[a]:0.0} db={_db} free={bf:0.0}");
            return true;
        }
        return false;
    }

    public override void _Process(double delta)
    {
        var g = Game.I;
        if (!Net.I.IsServer) return;
        if (g.Phase == GamePhase.Weighing && !_weighStarted && _round == 1)
        {
            _weighStarted = true;
            int denied0 = g.ShoveDenied;
            GetTree().CreateTimer(1.0).Timeout += () => g.DebugStep("shove_now", "Alice");
            GetTree().CreateTimer(2.5).Timeout += () => Chk("shove_disabled_in_weighing", g.ShoveDenied > denied0, $"denied={g.ShoveDenied - denied0}");
        }
        if (g.Phase != GamePhase.Round) return;
        if (g.Round != _round)
        {
            _round = g.Round; _t = 0; _idx = 0;
            _script = _round == 1 ? Script1(g) : new List<(double, Action)> { (1.0, g.DebugEndRound) };
        }
        _t += delta;
        while (_idx < _script.Count && _t >= _script[_idx].t) _script[_idx++].a();
    }

    List<(double, Action)> Script1(Game g)
    {
        var s = new List<(double, Action)>();
        void At(double t, Action a) => s.Add((t, a));
        Vector3 Hold(float along) => _o + _da * along + Vector3.Up * 0.12f;

        At(2, () =>
        {
            if (!FindArena(g)) { Util.Fail("arena", "no open floor"); g.DebugEndRound(); return; }
        });

        // ---- A: a front shove moves the victim and spills nothing
        At(3, () =>
        {
            Pose("Host", Hold(9.5f), _da);
            Pose("Alice", Hold(0f), _da); Pose("Bob", Hold(1.2f), -_da);
            Sack("Bob", "ruby", "coal"); Sack("Alice"); _bobSack0 = P("Bob").Inv.Sack.ToList();
            _pk0 = g.World.Objects.Values.OfType<PickupObj>().Select(o => o.Id).ToHashSet();
        });
        At(3.7, () => { _bobPos0 = P("Bob").Pos; _hits0 = g.ShoveHits; g.DebugStep("shove", "Alice"); });
        At(5.5, () =>
        {
            float moved = (P("Bob").Pos - _bobPos0).Length(); float along = (P("Bob").Pos - _bobPos0).Dot(_da);
            Chk("front_shove_moved", moved > 2.2f && along > 2.0f && g.ShoveHits == _hits0 + 1, $"moved={moved:0.00} along={along:0.00} hits={g.ShoveHits - _hits0}");
            Chk("front_shove_no_spill", SackOf("Bob") == "ruby,coal" && g.World.Objects.Values.OfType<PickupObj>().All(o => _pk0.Contains(o.Id)), $"sack={SackOf("Bob")}");
        });

        // ---- B: a back shove spills exactly the top stone as a pickup a third miner can take
        At(6, () => { Pose("Alice", Hold(0f), _da); Pose("Bob", Hold(1.2f), _da); Sack("Bob", "coal", "silver", "ruby"); });
        At(6.8, () => { _bobPos0 = P("Bob").Pos; g.DebugStep("shove", "Alice"); });
        At(8.6, () =>
        {
            var fresh = g.World.Objects.Values.OfType<PickupObj>().Where(o => !_pk0.Contains(o.Id)).ToList();
            var pk = fresh.FirstOrDefault(o => o.Data == "ore:ruby");
            Chk("back_shove_spills_top_stone", SackOf("Bob") == "coal,silver" && pk != null && fresh.Count == 1,
                $"sack={SackOf("Bob")} new_pickups={fresh.Count} top_pickup={(pk != null)} dist={(pk != null ? pk.GlobalPosition.DistanceTo(P("Bob").Pos) : -1):0.0}");
            _spillId = pk?.Id ?? -1;
            Chk("back_shove_moved", (P("Bob").Pos - _bobPos0).Length() > 2.2f);
            if (pk != null) { Pose("Host", pk.GlobalPosition + new Vector3(-_da.X, 0, -_da.Z) * 2.5f, _da); Sack("Host"); g.DebugStep("pickup", "Host"); }
        });
        At(11, () =>
        {
            Chk("spilled_stone_taken_by_third_miner", SackOf("Host") == "ruby" && !g.World.Objects.ContainsKey(_spillId), $"host_sack={SackOf("Host")}");
        });

        // ---- C: the cooldown: the second request 0.3 s later is blocked, the third one 1.5 s after the first is allowed (but nobody is in front any more)
        At(11.5, () => { Pose("Host", Hold(9.5f), _da); Pose("Alice", Hold(0f), _da); Pose("Bob", Hold(1.2f), -_da); Sack("Bob"); });
        At(12.3, () => { _hits0 = g.ShoveHits; _blocked0 = g.ShoveBlocked; _miss0 = g.ShoveMisses; g.DebugStep("shove_spam", "Alice"); });
        At(14.3, () => Chk("shove_cooldown", g.ShoveHits == _hits0 + 1 && g.ShoveBlocked == _blocked0 + 1 && g.ShoveMisses == _miss0 + 1, $"hits={g.ShoveHits - _hits0} blocked={g.ShoveBlocked - _blocked0} misses={g.ShoveMisses - _miss0}"));

        // ---- D: shoved into a spike plate: the hazard hurts
        At(14.8, () => { Pose("Alice", Hold(0f), _da); Pose("Bob", Hold(1.2f), _da); Sack("Bob"); P("Bob").Health = 100; P("Bob").Dirty = true; });
        At(15.5, () =>
        {
            // the plate goes down only after Bob has been moved off its spot (he lay on it after test C)
            var rng = new RandomNumberGenerator(); rng.Randomize();
            var plate = TrapSpawner.Spawn(g, "spikes", _o + _da * 4.8f, rng, true, true);
            _plateId = plate?.Id ?? -1;
            Chk("spike_plate_spawned", plate != null);
        });
        At(16.2, () => g.DebugStep("shove", "Alice"));
        At(19.5, () =>
        {
            Chk("shove_into_trap_hurts", P("Bob").Health <= 75f, $"hp={P("Bob").Health:0} pos_along={(P("Bob").Pos - _o).Dot(_da):0.0}");
            if (_plateId > 0) g.ServerDespawnObject(_plateId);
        });

        // ---- E: shoved off a ledge: the fall hurts
        At(20, () =>
        {
            _platId = g.ServerSpawnObject("dbgplatform", _o + _db * 4f, "w=4;h=4.5");
            P("Bob").Health = 100; P("Bob").Dirty = true;
        });
        At(21, () =>
        {
            float top = _y + 4.5f + 0.15f;
            Pose("Alice", new Vector3(_o.X + _db.X * 3.6f, top, _o.Z + _db.Z * 3.6f), _db);
            Pose("Bob", new Vector3(_o.X + _db.X * 4.8f, top, _o.Z + _db.Z * 4.8f), _db);
        });
        At(22.2, () => g.DebugStep("shove", "Alice"));
        At(26, () =>
        {
            Chk("shove_off_ledge_hurts", P("Bob").Health < 100f && P("Bob").Pos.Y < _y + 1f, $"hp={P("Bob").Health:0} y={P("Bob").Pos.Y - _y:0.0}");
            if (_platId > 0) g.ServerDespawnObject(_platId);
        });

        // ---- F: pickpocket succeeds: exactly one stone moves, the victim hears 1.5 s later
        At(26.5, () =>
        {
            Pose("Alice", Hold(0.6f), _da, true); Pose("Bob", Hold(1.5f), _da); Sack("Alice"); Sack("Bob", "silver", "gold", "coal"); P("Alice").PickCooldownUntil = 0;
            P("Bob").Health = 100; P("Bob").Dirty = true;
        });
        At(27.8, () => { _starts0 = g.PickStarts; g.DebugStep("pick", "Alice|Bob|2.4"); });
        At(31, () =>
        {
            var orig = new[] { "silver", "gold", "coal" };
            var a = P("Alice").Inv.Sack; var b = P("Bob").Inv.Sack;
            bool one = a.Count == 1 && b.Count == 2 && orig.Contains(a[0]) && b.Concat(a).OrderBy(x => x).SequenceEqual(orig.OrderBy(x => x));
            Chk("pickpocket_moved_one_stone", one && g.PickStarts == _starts0 + 1, $"alice={SackOf("Alice")} bob={SackOf("Bob")}");
            g.DebugStep("report", "");
        });

        // ---- G: the victim turns round: the attempt fails after more than half the hold, loud, and a cooldown follows
        At(31.5, () => { Pose("Alice", Hold(0.6f), _da, true); Pose("Bob", Hold(1.5f), _da); Sack("Alice"); Sack("Bob", "ruby", "coal"); _noiseLoud = false; });
        At(32.8, () => { _starts0 = g.PickStarts; g.DebugStep("pick", "Alice|Bob|3.0"); });
        At(33.8, () => g.DebugStep("turn", "Bob|" + I(Mathf.RadToDeg(YawOf(-_da)))));
        At(35.5, () =>
        {
            Chk("pickpocket_failed_loud", _noiseLoud && SackOf("Bob") == "ruby,coal" && SackOf("Alice") == "" && P("Alice").PickCooldownUntil > Time.GetTicksMsec() / 1000.0 + 2.0,
                $"noise={_noiseLoud} bob={SackOf("Bob")} alice={SackOf("Alice")}");
            Pose("Bob", Hold(1.5f), _da);   // face away again; Alice tries during her cooldown
        });
        At(36.4, () => { _starts0 = g.PickStarts; g.DebugStep("pick", "Alice|Bob|1.0"); });
        At(38, () => Chk("pickpocket_cooldown_blocks", g.PickStarts == _starts0 && SackOf("Bob") == "ruby,coal", $"starts={g.PickStarts - _starts0}"));

        // ---- H: the coarse sack step is public, the exact numbers are not
        At(38.5, () =>
        {
            Pose("Alice", Hold(0.6f), _da); Pose("Bob", Hold(1.5f), _da); Pose("Host", Hold(2.5f), _da);
            Sack("Host", Enumerable.Repeat("coal", 12).ToArray()); Sack("Bob", Enumerable.Repeat("coal", 4).ToArray()); Sack("Alice");
        });
        At(40.5, () =>
        {
            Chk("sack_steps_server", P("Host").SackStep == 4 && P("Bob").SackStep == 2 && P("Alice").SackStep == 0, $"host={P("Host").SackStep} bob={P("Bob").SackStep} alice={P("Alice").SackStep}");
            g.DebugStep("sackcheck", "Host:4;Bob:2;Alice:0");
        });

        // ---- I: downed miner: a pickpocket is refused, hold E revives, C robs, hold RMB drags
        At(41.5, () =>
        {
            Sack("Host", "gold", "coal"); Sack("Bob"); Sack("Alice"); P("Alice").PickCooldownUntil = 0;
            Pose("Host", Hold(1.5f), _da); Pose("Alice", Hold(0.6f), _da, true); Pose("Bob", Hold(1.5f) + new Vector3(_da.Z, 0, -_da.X) * 1.0f, -_da);
        });
        At(42.3, () => { g.DownMiner(P("Host"), null, "test"); });
        At(43, () => { _starts0 = g.PickStarts; g.DebugStep("pick", "Alice|Host|1.0"); });
        At(44.5, () =>
        {
            Chk("downed_cannot_be_pickpocketed", g.PickStarts == _starts0 && P("Host").Downed, $"starts={g.PickStarts - _starts0}");
            Pose("Alice", Hold(0.4f), _da, false);
        });
        At(45.3, () => g.DebugStep("revive", "Alice|Host|3.5"));
        At(49.3, () => Chk("revive_works", !P("Host").Downed && P("Host").Alive && P("Host").Health >= 30f, $"downed={P("Host").Downed} hp={P("Host").Health:0}"));
        At(49.8, () => { g.DownMiner(P("Host"), null, "test"); _hostPos0 = P("Host").Pos; });
        At(50.4, () => g.DebugStep("rob", "Bob|Host|2"));
        At(52.2, () =>
        {
            Chk("rob_takes_most_valuable_first", SackOf("Bob") == "gold,coal" && SackOf("Host") == "", $"bob={SackOf("Bob")} host={SackOf("Host")}");
            Pose("Alice", _o + _da * (_hostPos0 - _o).Dot(_da) + _da * 1.4f + Vector3.Up * 0.12f, _da);
        });
        At(53.2, () => { _hostPos0 = P("Host").Pos; g.DebugStep("drag", "Alice|Host|2.4|" + I(Mathf.RadToDeg(YawOf(_da)))); });
        At(56.4, () =>
        {
            float along = (P("Host").Pos - _hostPos0).Dot(_da);
            Chk("drag_moves_downed_miner", along > 2.0f && P("Host").Downed && P("Host").DraggedBy < 0, $"moved_along={along:0.0} downed={P("Host").Downed} still_dragged={P("Host").DraggedBy >= 0}");
            g.ServerRevive(P("Host"), 50f);
        });

        // ---- J: a shove just before the fuse: logged, deterministic (the Hall check at the fuse uses the server's last reported position)
        At(57.5, () => { Pose("Alice", Hold(0f), _da); Pose("Bob", Hold(1.2f), _da); Pose("Host", Hold(9.5f), _da); Sack("Bob", "coal"); Sack("Host", "gold"); Sack("Alice", "iron"); });
        At(58.3, () => g.DebugStep("shove", "Alice"));
        At(59.5, () =>
        {
            P("Host").TributeWeight = 5; P("Bob").TributeWeight = 9; P("Alice").TributeWeight = 30;
            g.DebugEndRound();
        });
        return s;
    }
}
