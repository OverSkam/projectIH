using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Test-bot routines for the trap test (hooked from BotBrain's default branch). Bots only use what a player sees:
/// world objects (kind, position, replicated Data) and their own health.
///   trapstand      walk to the nearest floor/ceiling trap (spikes, rockfall, gas, trapdoor) and stand in it
///   trapwalk       cross the dart tripwire (walks along the dart direction)
///   trapleverpull  walk to the nearest lever and pull it (repeats until it reports pulled)
///   trapgate       camp-side of the nearest gate; push through once it is down (must be blocked), again once it is up (must pass)
/// </summary>
public static class TrapBots
{
    static List<Vector3> _path = new();
    static Vector3 _goal = new(9999, 0, 9999);
    static int _stage;
    static double _t, _clock;
    static float _lastHp = 100f;
    static readonly HashSet<string> Said = new();

    static void Once(string key, string detail = "") { if (Said.Add(key)) Util.Pass(key, detail); }

    static NetObject Nearest(Game g, Miner m, Func<NetObject, bool> f)
    {
        NetObject best = null; float bd = float.MaxValue;
        foreach (var o in g.World.Objects.Values)
        {
            if (!f(o)) continue;
            float d = o.GlobalPosition.DistanceTo(m.GlobalPosition);
            if (d < bd) { bd = d; best = o; }
        }
        return best;
    }

    static Dictionary<string, string> Kv(NetObject o) => NetObject.Kv(o.Data);
    static float Fv(Dictionary<string, string> d, string k, float def = 0) => d.TryGetValue(k, out var s) ? TrapUtil.ToF(s, def) : def;

    static void Steer(Miner m, ref MinerInput inp, Vector3 p, bool sprint = false)
    {
        var d = p - m.EyePos;
        inp.Yaw = Mathf.Atan2(-d.X, -d.Z); inp.Pitch = 0; inp.Move = new Vector2(0, 1); inp.Sprint = sprint;
    }

    static bool Walk(Miner m, ref MinerInput inp, Vector3 goal, float arrive)
    {
        var pos = m.GlobalPosition;
        float flat = new Vector2(pos.X - goal.X, pos.Z - goal.Z).Length();
        if (flat <= arrive) return true;
        if (flat < 4f) { Steer(m, ref inp, goal); return false; }
        if (_path.Count == 0 || new Vector2(_goal.X - goal.X, _goal.Z - goal.Z).Length() > 1.5f)
        {
            _path = Game.I.World.Cave.FindPath(pos, goal); _goal = goal;
        }
        while (_path.Count > 1 && new Vector2(_path[0].X - pos.X, _path[0].Z - pos.Z).Length() < 1.5f) _path.RemoveAt(0);
        Steer(m, ref inp, _path[0], flat > 8f);
        return false;
    }

    public static void Run(string routine, Miner m, ref MinerInput inp, Game g)
    {
        _clock += 1.0 / 60.0;
        if (g.OwnHealth < _lastHp - 0.5f) Util.Pass("trap_hurt", $"hp={g.OwnHealth:0}");
        _lastHp = g.OwnHealth;
        inp.UtilSlot = -1;
        switch (routine)
        {
            case "trapstand": Stand(m, ref inp, g); break;
            case "trapwalk": WalkWire(m, ref inp, g); break;
            case "trapleverpull": Pull(m, ref inp, g); break;
            case "trapgate": Gate(m, ref inp, g); break;
        }
    }

    static void Stand(Miner m, ref MinerInput inp, Game g)
    {
        var o = Nearest(g, m, x => x.Kind is "trap_spikes" or "trap_rockfall" or "trap_gas" or "trap_trapdoor");
        if (o == null) return;
        var d = Kv(o);
        var target = o.GlobalPosition;
        if (o.Kind == "trap_trapdoor") target += TrapUtil.Dir(Fv(d, "a")) * (-Fv(d, "hl", 7f) * 0.4f);
        if (m.GlobalPosition.Y < o.GlobalPosition.Y - 2.5f) Once("trapdoor_fell", $"y={m.GlobalPosition.Y:0.0}");
        if (Walk(m, ref inp, target, 0.3f)) { inp.Move = Vector2.Zero; inp.Sprint = false; Once("trap_standing", o.Kind); }
    }

    static void WalkWire(Miner m, ref MinerInput inp, Game g)
    {
        var o = Nearest(g, m, x => x.Kind == "trap_darts");
        if (o == null) return;
        var dir = TrapUtil.Dir(Fv(Kv(o), "a"));
        var start = o.GlobalPosition - dir * 3f;
        if (_stage == 0) { if (Walk(m, ref inp, start, 0.5f)) _stage = 1; return; }
        if (_stage == 1)
        {
            Steer(m, ref inp, o.GlobalPosition + dir * 6f);
            if ((m.GlobalPosition - o.GlobalPosition).Dot(dir) > 5f) _stage = 2;
            return;
        }
        inp.Move = Vector2.Zero;
    }

    static void Pull(Miner m, ref MinerInput inp, Game g)
    {
        var o = Nearest(g, m, x => x.Kind == "trap_lever");
        if (o == null) return;
        string st = Kv(o).GetValueOrDefault("s", "");
        if (st != "ready") { inp.Move = Vector2.Zero; Once("lever_seen_pulled"); return; }
        if (Kv(o).GetValueOrDefault("mk", "") == "collapse")
        {
            var mp = new Vector3(Fv(Kv(o), "fx"), 0, Fv(Kv(o), "fz"));
            if (!g.World.Miners().Any(x => x != m && new Vector2(x.GlobalPosition.X - mp.X, x.GlobalPosition.Z - mp.Z).Length() < 1.2f)) { inp.Move = Vector2.Zero; return; }
        }
        if (!Walk(m, ref inp, o.GlobalPosition, 1.7f)) return;
        inp.Move = Vector2.Zero;
        var d = o.GlobalPosition + Vector3.Up * 1f - m.EyePos;
        inp.Yaw = Mathf.Atan2(-d.X, -d.Z); inp.Pitch = Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), -1.2f, 1.2f);
        if (_clock - _t > 1.0) { _t = _clock; g.SendInteract(o.Id); }
    }

    static void Gate(Miner m, ref MinerInput inp, Game g)
    {
        var o = Nearest(g, m, x => x.Kind == "trap_gate");
        if (o == null) return;
        var d = Kv(o);
        string st = d.GetValueOrDefault("s", "");
        var dir = TrapUtil.Dir(Fv(d, "a"));
        var P = o.GlobalPosition;
        // stand on the camp side of the gate and push through towards the far side
        var campSide = (P - dir * 3.5f).DistanceTo(Vector3.Zero) <= (P + dir * 3.5f).DistanceTo(Vector3.Zero) ? -1f : 1f;
        var home = P + dir * (3.5f * campSide);
        var far = P - dir * (4.5f * campSide);
        float along = (m.GlobalPosition - P).Dot(dir) * campSide;   // negative = camp side... positive = on the home side
        switch (_stage)
        {
            case 0: if (Walk(m, ref inp, home, 0.8f)) _stage = 1; break;
            case 1:
                inp.Move = Vector2.Zero;
                if (st == "down") { _stage = 2; _t = _clock; Util.Log($"GATEBOT start pos={m.GlobalPosition} P={P} dir={dir} home={home} far={far}"); }
                break;
            case 2:   // push into the closed gate: must stay on the home side
                Steer(m, ref inp, far);
                if (along < 0.2f) { Util.Fail("gate_blocked", "crossed a closed gate"); _stage = 9; }
                else if (_clock - _t > 2.5)
                {
                    Util.Pass("gate_blocked", $"along={along:0.0} state={st} pos={m.GlobalPosition}");
                    _stage = 3;
                }
                break;
            case 3:   // keep pushing until it rises and we pass
                Steer(m, ref inp, far);
                if (along < -1.0f) { Util.Pass("gate_reopened", $"state={st}"); _stage = 4; }
                break;
            default: inp.Move = Vector2.Zero; break;
        }
    }
}
