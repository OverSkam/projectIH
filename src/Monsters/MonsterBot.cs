using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Test bot (--bot=mon). Walks to --monpos=x:z along the nav graph, then faces --monface=x:z (default: nearest monster).
/// --monhit: once in place, equips the pickaxe and swings at the nearest monster (real RequestAttack ray path).
/// Also reports PASS: miner_hurt_seen when its own health drops (client-side proof that damage arrives).
/// </summary>
public class MonsterBot : IMinerController
{
    float _yaw, _pitch;
    List<Vector3> _path = new();
    Vector3 _pathGoal = new Vector3(9999, 0, 9999);
    double _stuckT, _unstick;
    Vector3 _lastPos;
    bool _hurtSeen, _arrived;
    double _equipT;
    bool _lanternDone; float _dbg, _strafe = 1f;
    Vector3? _goal;

    /// <summary>--monpos=x:z, or deep:H[:variant] = a room spot about H path metres from the Hall (the same spot the server scenarios compute).</summary>
    static string DescCell(Vector3 p) { var mc = ModularCave.Last; if (mc == null) return "-"; var c = mc.At((int)System.Math.Round(p.X / 2f), (int)System.Math.Round(p.Z / 2f)); return $"open={c.Open} thin={c.Thin} crawl={c.Crawl} ceil={c.Ceil} owner={c.Owner} ramp={c.Ramp}"; }
    static Vector3? ParseXZ(string s)
    {
        var p = s.Split(':');
        if (p.Length >= 2 && p[0] == "deep" && float.TryParse(p[1], out var hd) && Game.I?.World?.Cave != null)
        {
            var v = MonsterNav.For(Game.I.World.Cave).DeepNode(hd, p.Length > 2 && int.TryParse(p[2], out var vi) ? vi : 0);
            return v == Vector3.Zero ? null : v;
        }
        if (p.Length == 2 && float.TryParse(p[0], out var x) && float.TryParse(p[1], out var z)) return new Vector3(x, 0, z);
        return null;
    }

    public MinerInput Poll(float dt, Miner m)
    {
        var inp = new MinerInput { UtilSlot = -1, Yaw = _yaw, Pitch = _pitch };
        var g = Game.I;
        if (g.Phase != GamePhase.Round || g.World == null) return inp;
        if (!_hurtSeen && g.OwnHealth < 99f && g.OwnHealth > 0f) { _hurtSeen = true; Util.Pass("miner_hurt_seen", $"hp={g.OwnHealth}"); }

        _goal ??= ParseXZ(Util.Arg("monpos", ""));
        var goal = _goal;
        var pos = m.GlobalPosition;
        if (Util.HasArg("botdebug") && (_dbg -= dt) <= 0) { _dbg = 3; Util.Log($"MONBOT pos={pos} goal={goal} arrived={_arrived} path={_path.Count} wp={(_path.Count > 0 ? _path[0] : Vector3.Zero)} cell={DescCell(_path.Count > 0 ? _path[0] : pos)} here={DescCell(pos)} hall={g.World.Cave.HallZone}"); }
        if (goal.HasValue && !_arrived)
        {
            if (new Vector3(pos.X - goal.Value.X, 0, pos.Z - goal.Value.Z).Length() < 1.0f) { _arrived = true; Util.Pass("bot_in_position", $"pos={pos}"); }
            else Walk(m, ref inp, goal.Value, g);
        }
        if (goal.HasValue && !_arrived) { if (_unstick > 0) { _unstick -= dt; inp.Jump = true; inp.Move = new Vector2(_strafe, 0.3f); } _yaw = inp.Yaw; _pitch = inp.Pitch; return inp; }

        if (_arrived && Util.Arg("monlantern", "") == "off" && !_lanternDone && g.OwnInv.Held == "lantern") { inp.ToggleLantern = true; _lanternDone = true; }
        // in position: look at the interesting thing
        MonsterObj mon = null; float bd = float.MaxValue;
        foreach (var o in g.World.Objects.Values)
            if (o is MonsterObj mo && mo.Kind != "dragon" || o is DragonObj)
            {
                float d = o.GlobalPosition.DistanceTo(pos);
                if (d < bd) { bd = d; mon = (MonsterObj)o; }
            }
        var face = ParseXZ(Util.Arg("monface", ""));
        if (Util.HasArg("monhit") && mon != null)
        {
            Face(m, ref inp, mon.GlobalPosition + new Vector3(0, 1.8f, 0));
            if (g.OwnInv.Held != "pickaxe")
            {
                _equipT -= dt;
                if (_equipT <= 0) { _equipT = 0.5; int idx = Array.IndexOf(g.OwnInv.Utils, "pickaxe"); if (idx >= 0) g.SendSelectUtil(idx); }
            }
            else if (bd < 3.4f) inp.Attack = true;
        }
        else if (face.HasValue) Face(m, ref inp, face.Value + new Vector3(0, 1.5f, 0));
        else if (mon != null && bd < 30f) Face(m, ref inp, mon.GlobalPosition + new Vector3(0, 1.2f, 0));
        _yaw = inp.Yaw; _pitch = inp.Pitch;
        return inp;
    }

    void Walk(Miner m, ref MinerInput inp, Vector3 goal, Game g)
    {
        var pos = m.GlobalPosition;
        static Vector3 flat(Vector3 v) => new Vector3(v.X, 0, v.Z);
        if (_path.Count == 0 || (flat(_pathGoal) - flat(goal)).Length() > 2f)
        {
            _path = MonsterNav.For(g.World.Cave).FindPath(NavProfile.Person, pos, goal) ?? g.World.Cave.FindPath(pos, goal); _pathGoal = goal;
        }
        while (_path.Count > 1 && (flat(_path[0]) - flat(pos)).Length() < 0.7f) _path.RemoveAt(0);
        var wp = _path[0];
        Face(m, ref inp, new Vector3(wp.X, m.EyePos.Y, wp.Z));
        inp.Pitch = 0;
        inp.Move = new Vector2(0, 1);
        inp.Sprint = (flat(pos) - flat(goal)).Length() > 6f;
        _stuckT += 1.0 / 60.0;
        if (_stuckT > 1.5) { if ((pos - _lastPos).Length() < 0.4f) { _unstick = 1.0; _strafe = -_strafe; if (_path.Count > 2) _path.RemoveAt(0); } _lastPos = pos; _stuckT = 0; }
    }

    static void Face(Miner m, ref MinerInput inp, Vector3 point)
    {
        var d = point - m.EyePos;
        inp.Yaw = Mathf.Atan2(-d.X, -d.Z);
        inp.Pitch = Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), -1.4f, 1.4f);
    }
}
