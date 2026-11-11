using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Scripted controller for automated tests (--bot=routine). Runs on the bot's own client and only uses
/// information a real player has (public miner positions, world objects, own inventory).
/// Routines: idle, miner (mine -> pick up -> deliver loop), killer (idle round 1, hunts others from round 2).
/// </summary>
public class BotBrain : IMinerController
{
    readonly string _routine;
    float _yaw, _pitch;
    List<Vector3> _path = new();
    Vector3 _pathGoal = new Vector3(9999, 0, 9999);
    double _actTimer, _stuckTimer, _unstick;
    Vector3 _lastPos;
    int _lastSack;
    bool _statedMine;

    readonly IMinerController _ext; // monsters workstream test bot (--bot=mon)
    public BotBrain(string routine) { _routine = routine; if (routine == "mon") _ext = new MonsterBot(); }

    public virtual MinerInput Poll(float dt, Miner m)
    {
        if (_ext != null) return _ext.Poll(dt, m);
        var inp = new MinerInput { UtilSlot = -1, Yaw = _yaw, Pitch = _pitch };
        var g = Game.I;
        if (g.Phase != GamePhase.Round || g.World == null) return inp;
        _actTimer -= dt;
        int sack = g.OwnInv.Sack.Count;
        if (sack > _lastSack) Util.Pass("ore_picked_up", $"sack={sack}");
        _lastSack = sack;

        switch (_routine)
        {
            case "miner": Miner(m, ref inp, g); break;
            case "killer": Killer(m, ref inp, g); break;
            case "pit": Pit(m, ref inp, g); break;
            default: break; // idle
        }
        _yaw = inp.Yaw; _pitch = inp.Pitch;
        if (_unstick > 0) { _unstick -= dt; inp.Jump = true; inp.Move = new Vector2(1, 0.5f); }
        return inp;
    }

    // ------------------------------------------------------------------ routines
    void Miner(Miner m, ref MinerInput inp, Game g)
    {
        var inv = g.OwnInv;
        var world = g.World;
        var camp = world.Cave.CampZone;
        bool lateRound = g.TimeLeft < 16f;
        NetObject goblin = world.Objects.Values.FirstOrDefault(o => o is GoblinObj);
        if (goblin == null) return;
        Vector3 goblinStand = goblin.GlobalPosition + new Vector3(0, 0, 2.3f);

        // Round 1: deliver every ore at once. Later rounds: keep mining until the last seconds (stays out in the cave, so the killer bot can find it).
        int deliverAt = g.Round >= 2 ? g.OwnSackCap + 1 : 1;
        if (inv.Sack.Count >= deliverAt || lateRound)
        {
            if (inv.Sack.Count == 0) { WalkTo(m, ref inp, new Vector3(0, 0, 3)); return; }
            if (WalkTo(m, ref inp, goblinStand, 1.0f) || m.GlobalPosition.DistanceTo(goblin.GlobalPosition) < 3.2f)
            {
                Face(m, ref inp, goblin.GlobalPosition + Vector3.Up * 1f);
                if (_actTimer <= 0) { _actTimer = 0.5; g.SendInteract(goblin.Id); }
            }
            return;
        }

        // nearest ore pickup or vein
        NetObject best = null; float bd = float.MaxValue;
        foreach (var o in world.Objects.Values)
        {
            bool ok = o is VeinObj || (o is PickupObj && o.Data.StartsWith("ore:") && inv.Sack.Count < g.OwnSackCap);
            if (!ok) continue;
            float d = o.GlobalPosition.DistanceTo(m.GlobalPosition);
            if (d < bd) { bd = d; best = o; }
        }
        if (best == null) { WalkTo(m, ref inp, new Vector3(0, 0, 3)); return; }

        if (best is PickupObj)
        {
            if (WalkTo(m, ref inp, best.GlobalPosition, 1.4f))
            {
                Face(m, ref inp, best.GlobalPosition);
                if (_actTimer <= 0) { _actTimer = 0.4; g.SendInteract(best.Id); }
            }
            return;
        }
        // vein: stand between vein and room centre
        var room = world.Cave.RoomAt(best.GlobalPosition);
        var toCentre = room != null ? (room.Center - best.GlobalPosition) with { Y = 0 } : Vector3.Forward;
        var stand = best.GlobalPosition + toCentre.Normalized() * 1.9f;
        EquipPickaxe(g);
        if (WalkTo(m, ref inp, stand, 0.5f) || m.GlobalPosition.DistanceTo(best.GlobalPosition) < 2.3f)
        {
            Face(m, ref inp, best.GlobalPosition + Vector3.Up * 0.8f);
            inp.Move = Vector2.Zero;
            if (inv.Held == "pickaxe") inp.Attack = true;
        }
    }

    void Killer(Miner m, ref MinerInput inp, Game g)
    {
        var world = g.World;
        if (g.Round < 2)
        {
            // Round 1: stand in a far room (gets buried when the mine collapses).
            { var c = world.Cave; var far = c.Rooms[Math.Min(1, c.Rooms.Count - 1)].Center; int ni = c.NearestNode(far); WalkTo(m, ref inp, ni >= 0 ? c.NavNodes[ni] : far, 1f); }
            return;
        }
        Miner target = null; float bd = float.MaxValue;
        foreach (var o in world.Miners())
        {
            if (o == m || !g.Alive.GetValueOrDefault(o.PeerId)) continue;
            if (world.Cave.InCamp(o.GlobalPosition)) continue; // no PvP in camp
            float d = o.GlobalPosition.DistanceTo(m.GlobalPosition);
            if (d < bd) { bd = d; target = o; }
        }
        if (target == null) { WalkTo(m, ref inp, new Vector3(0, 0, 3)); return; }
        EquipPickaxe(g);
        var tp = target.GlobalPosition;
        if (Util.HasArg("botdebug") && _actTimer <= 0) { _actTimer = 1; Util.Log($"KILLERBOT t={g.TimeLeft:0.0} me={m.GlobalPosition} target={tp} d={bd:0.0} held={g.OwnInv.Held}"); }
        if (bd > 10f) WalkTo(m, ref inp, tp, 1.0f);
        else if (bd > 1.8f) { Face(m, ref inp, tp + Vector3.Up * 1.1f); inp.Pitch = 0; inp.Move = new Vector2(0, 1); inp.Sprint = true; TrackStuck(m, ref inp); }
        if (bd < 2.6f)
        {
            Face(m, ref inp, tp + Vector3.Up * 1.1f);
            if (bd < 1.8f) inp.Move = Vector2.Zero;
            if (g.OwnInv.Held == "pickaxe") inp.Attack = true;
        }
    }

    int _pitStage;
    void Go(Miner m, ref MinerInput inp, Vector3 t) { Face(m, ref inp, new Vector3(t.X, m.EyePos.Y, t.Z)); inp.Pitch = 0; inp.Move = new Vector2(0, 1); }
    /// <summary>Manual-check routine: falls into the pit in room B and climbs the ramp out.</summary>
    void Pit(Miner m, ref MinerInput inp, Game g)
    {
        var p = m.GlobalPosition;
        switch (_pitStage)
        {
            case 0: if (WalkTo(m, ref inp, new Vector3(-32, 0, 0), 1f)) _pitStage = 1; break;
            case 1: Go(m, ref inp, new Vector3(-34, 0, 3.2f)); if (p.Y < -4f) { _pitStage = 2; Util.Pass("pit_fall", $"hp={g.OwnHealth}"); } break;
            case 2: Go(m, ref inp, new Vector3(-37.0f, 0, 6)); if (new Vector2(p.X + 37.0f, p.Z - 6f).Length() < 0.5f) _pitStage = 3; break;
            case 3: Go(m, ref inp, new Vector3(-24, 0, 6)); inp.Sprint = false; if (p.Y > -0.5f && p.X > -29f) { _pitStage = 4; Util.Pass("pit_escape", $"pos={p}"); } break;
        }
    }

    // ------------------------------------------------------------------ helpers
    void EquipPickaxe(Game g)
    {
        if (g.OwnInv.Held == "pickaxe" || _actTimer > 0.2 && _statedMine) return;
        int idx = Array.IndexOf(g.OwnInv.Utils, "pickaxe");
        if (idx >= 0) g.SendSelectUtil(idx);
        _statedMine = true;
    }

    /// <summary>Walks along the nav graph; returns true when within 'arrive' metres of the goal.</summary>
    bool WalkTo(Miner m, ref MinerInput inp, Vector3 goal, float arrive = 1.0f)
    {
        var pos = m.GlobalPosition;
        Vector3 flat(Vector3 v) => new Vector3(v.X, 0, v.Z);
        if ((flat(pos) - flat(goal)).Length() <= arrive) return true;
        var cave = Game.I.World.Cave;
        if (_path.Count == 0 || (flat(_pathGoal) - flat(goal)).Length() > 2f)
        {
            _path = cave.FindPath(pos, goal);
            _pathGoal = goal;
        }
        while (_path.Count > 1 && (flat(_path[0]) - flat(pos)).Length() < 1.5f) _path.RemoveAt(0);
        // skip waypoints that lie behind us when a later one is already closer to the goal
        var wp = _path[0];
        Face(m, ref inp, new Vector3(wp.X, m.EyePos.Y, wp.Z));
        inp.Pitch = 0;
        inp.Move = new Vector2(0, 1);
        inp.Sprint = (flat(pos) - flat(goal)).Length() > 6f;

        TrackStuck(m, ref inp);
        return false;
    }

    void TrackStuck(Miner m, ref MinerInput inp)
    {
        var pos = m.GlobalPosition;
        _stuckTimer += 1.0 / 60.0;
        if (_stuckTimer > 1.5)
        {
            if ((pos - _lastPos).Length() < 0.4f) _unstick = 0.5;
            _lastPos = pos; _stuckTimer = 0;
        }
    }

    void Face(Miner m, ref MinerInput inp, Vector3 point)
    {
        var d = point - m.EyePos;
        inp.Yaw = Mathf.Atan2(-d.X, -d.Z);
        inp.Pitch = Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), -1.4f, 1.4f);
    }
}
