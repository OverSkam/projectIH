using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Soak-test bot (--bot=explorer | explorer_brave). Roams the generated cave along the nav graph: mines veins, picks up loot, opens chests,
/// tries shrines, pulls levers, (brave) walks into traps, keeps its lantern oil topped up, and returns to the Hall to feed the Goblin before the timer ends.
/// Uses only what a real client sees (replicated object kinds/positions/data, own inventory and health).
/// </summary>
public class ExplorerBot : IMinerController
{
    readonly bool _brave;
    readonly string _name;
    float _yaw, _pitch;
    List<Vector3> _path = new();
    Vector3 _pathGoal = new(9999, 0, 9999);
    double _clock, _actT, _stuckT, _unstick, _unstickDir = 1, _targetSince, _flee, _dbgT, _holdUntil;
    Vector3 _lastPos;
    int _stuckCount, _round = -1;
    World _world;
    NetObject _target;
    int _targetTries;
    readonly HashSet<int> _skip = new();
    bool[] _comp;
    string _mode = "explore";
    double _pathLenT; float _pathLen = 30f;
    bool _trapPassed, _deliberateTrap; Vector3 _trapBeyond;
    int _leverRound = -1, _monRound = -1, _trapDuds; float _hpAtTrap;
    static int _trapRuns;   // deliberate trap visits completed in this process (= this match)
    bool _crossing; double _etherealUntil;

    public ExplorerBot(string routine) { _brave = routine.Contains("brave"); _name = routine; }

    static Vector3 Flat(Vector3 v) => new(v.X, 0, v.Z);

    public MinerInput Poll(float dt, Miner m)
    {
        var inp = new MinerInput { UtilSlot = -1, Yaw = _yaw, Pitch = _pitch };
        var g = Game.I;
        if (g.Phase != GamePhase.Round || g.World == null || g.World.Cave == null) return inp;
        _clock += dt; _actT -= dt;
        if (g.World != _world) NewRound(g);
        Tick(m, ref inp, g);
        _yaw = inp.Yaw; _pitch = inp.Pitch;
        if (_unstick > 0) { _unstick -= dt; inp.Jump = true; inp.Move = new Vector2((float)_unstickDir, 0.6f); }
        return inp;
    }

    void NewRound(Game g)
    {
        _world = g.World; _round = g.Round; _target = null; _skip.Clear(); _path.Clear(); _mode = "explore"; _flee = 0; _stuckCount = 0; _crossing = false; _etherealUntil = 0;
        var cave = g.World.Cave; var adj = cave.Adjacency();
        _comp = new bool[cave.NavNodes.Count];
        int s = cave.NearestNode(cave.GoblinPos + new Vector3(0, 0, 2.3f));
        var q = new Queue<int>(); if (s >= 0) { q.Enqueue(s); _comp[s] = true; }
        while (q.Count > 0) { int u = q.Dequeue(); foreach (int v in adj[u]) if (!_comp[v]) { _comp[v] = true; q.Enqueue(v); } }
    }

    // ------------------------------------------------------------------ decisions
    void Tick(Miner m, ref MinerInput inp, Game g)
    {
        var inv = g.OwnInv; var world = g.World; var cave = world.Cave;
        var goblin = world.Objects.Values.FirstOrDefault(o => o is GoblinObj);
        if (goblin == null) return;
        Vector3 stand = goblin.GlobalPosition + new Vector3(0, 0, 2.3f);
        var pos = m.GlobalPosition;
        if (Util.HasArg("botdebug") && (_dbgT -= 1.0 / 60) <= 0) { _dbgT = 4; Util.Log($"EXPLORER {_name} mode={_mode} hp={g.OwnHealth} sack={inv.Sack.Count} t={g.TimeLeft:0} pos={pos} target={_target?.Kind}#{_target?.Id}"); }

        // sealed in (e.g. inside a thin-wall nook after opening its chest): use the Ethereal stein to get back out
        bool sealedTarget = _target != null && GodotObject.IsInstanceValid(_target) && _target is not MonsterObj && !Reachable(cave, _target.GlobalPosition, 3.2f) && !InCrawl(_target.GlobalPosition);
        if (!Reachable(cave, pos, 3.8f) && !sealedTarget)
        {
            var home = NearestCompNode(cave, pos);
            Cross(m, ref inp, g, home);
            return;
        }

        // time to head back? (path length / speed + margin)
        if (_clock - _pathLenT > 3) { _pathLenT = _clock; _pathLen = PathLen(cave.FindPath(pos, stand)); }
        bool late = g.TimeLeft < _pathLen / 3.6f + 14f;
        bool full = inv.Sack.Count >= g.OwnSackCap;
        bool hurt = g.OwnHealth < (_brave ? 28 : 50);
        if (hurt && !cave.InHall(pos) && _mode != "flee") { _mode = "flee"; _flee = _clock + 12; }
        LanternEconomy(m, ref inp, g, cave, inv);
        if (_mode == "flee")
        {
            if (cave.InHall(pos) && _clock > _flee) _mode = "explore";
            else { GoFeed(m, ref inp, g, goblin, stand, inv); return; }
        }
        if (late || (full && inv.Sack.Count > 0))
        {
            if (inv.Sack.Count == 0 && late) { Walk(m, ref inp, new Vector3(0, 0, 4), 2.5f); return; }  // wait in camp
            GoFeed(m, ref inp, g, goblin, stand, inv); return;
        }
        if (_mode == "feed" && inv.Sack.Count == 0) _mode = "explore";   // "feed" = walking to the Goblin to feed him

        // cautious bot backs off from awake monsters
        if (!_brave)
        {
            foreach (var o in world.Objects.Values)
                if (o is MonsterObj mo && mo.State is MonsterState.Chase or MonsterState.Windup or MonsterState.Attack && mo.GlobalPosition.DistanceTo(pos) < 7f && !cave.InHall(pos))
                { _mode = "flee"; _flee = _clock + 8; return; }
        }

        if (_target is OilDrumObj && (g.OwnOil > g.OwnOilMax * 0.8f || Kv(_target, "uses") == "0")) Drop();
        if (_target != null && (!GodotObject.IsInstanceValid(_target) || Gone(_target, world))) { _target = null; _holdUntil = 0; }
        else if (_target is ChestObj && Kv(_target, "open") == "1" || _target is LeverObj && Kv(_target, "s") != "ready" || _target is ShrineObj && Kv(_target, "state") == "done") Drop();
        if (_target != null && _clock - _targetSince > 45) { Util.Log($"EXPLORER {_name} giving up on {_target.Kind}#{_target.Id} at {_target.GlobalPosition}"); _skip.Add(_target.Id); _target = null; }
        if (_target == null) PickTarget(m, g);
        if (_target == null) { Walk(m, ref inp, RoamGoal(cave, pos), 1.5f); return; }
        Work(m, ref inp, g);
    }

    double _lanternT;
    /// <summary>Basic oil management: lantern off while standing in the Hall (it is lit there anyway), on again in the cave.</summary>
    void LanternEconomy(Miner m, ref MinerInput inp, Game g, CaveInfo cave, Inventory inv)
    {
        if (_clock - _lanternT < 2.0 || g.OwnOil <= 0f || Array.IndexOf(inv.Utils, "lantern") < 0) return;
        bool inHall = cave.InHall(m.GlobalPosition);
        if (inHall == m.LanternOut) return;   // already as wanted (off in the Hall, on elsewhere)
        _lanternT = _clock; inp.ToggleLantern = true;
        Util.Pass("explorer_lantern_toggle", $"inHall={inHall}");
    }

    static bool Gone(NetObject o, World w) => !w.Objects.ContainsKey(o.Id);

    float PathLen(List<Vector3> p) { float l = 0; for (int i = 1; i < p.Count; i++) l += p[i - 1].DistanceTo(p[i]); return l; }

    Vector3 _roam; double _roamT;
    Vector3 RoamGoal(CaveInfo cave, Vector3 pos)
    {
        if (_clock > _roamT || Flat(pos).DistanceTo(Flat(_roam)) < 2f)
        {
            _roamT = _clock + 20;
            var r = cave.Rooms[(int)(GD.Randi() % (uint)cave.Rooms.Count)];
            int ni = cave.NearestNode(r.Center); _roam = ni >= 0 ? cave.NavNodes[ni] : r.Center;
        }
        return _roam;
    }

    void GoFeed(Miner m, ref MinerInput inp, Game g, NetObject goblin, Vector3 stand, Inventory inv)
    {
        _mode = _mode == "flee" ? "flee" : "feed";
        if (Walk(m, ref inp, stand, 1.0f) || m.GlobalPosition.DistanceTo(goblin.GlobalPosition) < 3.2f)
        {
            Face(m, ref inp, goblin.GlobalPosition + Vector3.Up);
            inp.Move = Vector2.Zero;
            if (inv.Sack.Count > 0 && _actT <= 0) { _actT = 0.5; g.SendInteract(goblin.Id); Util.Pass("explorer_feed_try"); }
        }
    }

    void PickTarget(Miner m, Game g)
    {
        var inv = g.OwnInv; var cave = g.World.Cave; var pos = m.GlobalPosition;
        bool sackRoom = inv.Sack.Count < g.OwnSackCap;
        NetObject best = null; float bs = float.MaxValue;
        // brave bots deliberately walk over one known damaging trap per match, so the soak test's "a trap hurt a miner" check never depends on luck
        if (_brave && _trapRuns < 1 && g.TimeLeft > 35f)
        {
            NetObject tb = null; float tbs = float.MaxValue;
            foreach (var o in g.World.Objects.Values)
            {
                if (o.Kind is not ("trap_spikes" or "trap_darts" or "trap_gas" or "trap_rockfall") || _skip.Contains(o.Id) || cave.InHall(o.GlobalPosition)) continue;
                if (!Reachable(cave, o.GlobalPosition, 3.2f)) continue;
                float d = Flat(o.GlobalPosition).DistanceTo(Flat(pos));
                if (d < tbs) { tbs = d; tb = o; }
            }
            if (tb != null) { _target = tb; _targetSince = _clock; _targetTries = 0; _trapPassed = false; _stuckCount = 0; _crossing = false; _deliberateTrap = true; return; }
        }
        if (_brave && g.TimeLeft > 40f && _leverRound != g.Round)
        {
            foreach (var o in g.World.Objects.Values)
                if (o is LeverObj lv && Kv(lv, "s") == "ready" && !_skip.Contains(o.Id) && !cave.InHall(o.GlobalPosition) && Reachable(cave, o.GlobalPosition, 3.2f))
                { _leverRound = g.Round; _target = lv; _targetSince = _clock; _targetTries = 0; _trapPassed = false; _stuckCount = 0; _crossing = false; return; }
        }
        // ... and walk up to a golem once per round (they hit hard; golems cannot be killed), so "a monster hurt a miner" is not left to chance either
        if (_brave && (_trapRuns >= 1 || g.TimeLeft < 85f) && g.TimeLeft > 40f && _monRound != g.Round)
        {
            MonsterObj gb = null; float gbs = float.MaxValue;
            foreach (var o in g.World.Objects.Values)
            {
                if (o is not MonsterObj mo || mo.Kind != "golem" || mo.State == MonsterState.Stunned || _skip.Contains(o.Id) || cave.InHall(o.GlobalPosition)) continue;
                if (!Reachable(cave, o.GlobalPosition, 4.5f)) continue;
                float d = Flat(o.GlobalPosition).DistanceTo(Flat(pos));
                if (d < gbs) { gbs = d; gb = mo; }
            }
            if (gb != null) { Util.Log("EXPLORER golem run id=" + gb.Id + " dist=" + gbs); _monRound = g.Round; _target = gb; _targetSince = _clock; _targetTries = 0; _trapPassed = false; _stuckCount = 0; _crossing = false; return; }
        }
        bool chestFirst = !_brave && g.TimeLeft > 55f;   // the thin-wall chest is far away: go for it early
        foreach (var o in g.World.Objects.Values)
        {
            if (_skip.Contains(o.Id)) continue;
            if (chestFirst && o is ChestObj ch0 && Kv(ch0, "open") != "1") { best = o; break; }
            float w;
            switch (o)
            {
                case VeinObj: w = sackRoom ? 1.0f : 99f; break;
                case PickupObj p: w = p.Data.StartsWith("ore:") ? (sackRoom ? 0.7f : 99f) : 0.8f; break;
                case ChestObj c: if (Kv(c, "open") == "1") continue; w = 0.3f; break;
                case ShrineObj s:
                    { var t = Kv(s, "type"); if (Kv(s, "state") == "done" || t == "plate") continue; w = 1.3f; break; }
                case LeverObj l: if (Kv(l, "s") != "ready") continue; w = _brave ? 0.7f : 1.2f; break;
                case OilDrumObj od: if (g.OwnOil > g.OwnOilMax * 0.3f || Kv(od, "uses") == "0") continue; w = 0.15f; break;
                case MonsterObj mo: if (!_brave || mo.Kind != "golem" || mo.State == MonsterState.Stunned) continue; w = 0.9f; break;   // brave bots provoke golems (they cannot be killed; a swing at the back stuns them)
                default:
                    if (_brave && o.Kind is "trap_spikes" or "trap_darts" or "trap_trapdoor" or "trap_gas" or "trap_rockfall") { w = 0.6f; break; }
                    continue;
            }
            if (cave.InHall(o.GlobalPosition) && o is not PickupObj) continue;
            if (o is PickupObj && !Reachable(cave, o.GlobalPosition, 3.2f) && !InCrawl(o.GlobalPosition)) continue;   // sealed-in loot: only chests are worth the Ethereal trip
            float d = Flat(o.GlobalPosition).DistanceTo(Flat(pos));
            float jitter = 0.85f + 0.3f * ((o.Id * 2654435761u + (uint)_name.GetHashCode()) % 1000) / 1000f;
            float score = d * w * jitter;
            if (score < bs) { bs = score; best = o; }
        }
        if (best == null) return;
        _target = best; _targetSince = _clock; _targetTries = 0; _trapPassed = false; _stuckCount = 0; _crossing = false;
    }

    static string Kv(NetObject o, string k) => NetObject.Kv(o.Data).GetValueOrDefault(k, "");

    // ------------------------------------------------------------------ doing the work at the target
    void Work(Miner m, ref MinerInput inp, Game g)
    {
        var o = _target; var inv = g.OwnInv; var pos = m.GlobalPosition; var world = g.World;
        var op = o.GlobalPosition;
        // behind a thin wall (nook chest / loot): walk to the nearest reachable spot, then phase through with the Ethereal stein (Q)
        if (o is not MonsterObj && o is not VeinObj && !Reachable(world.Cave, op, 3.2f) && !InCrawl(op))
        {
            if (g.OwnAbility != "ethereal") { if (_clock - _targetSince > 6) Drop(); return; }
            if (!_crossing && !Reachable(world.Cave, pos, 3.8f)) _crossing = true;   // already on the far side
            if (!_crossing) { if (Walk(m, ref inp, NearestCompNode(world.Cave, op), 0.9f)) _crossing = true; return; }
            if (Flat(pos).DistanceTo(Flat(op)) > 1.0f) { Cross(m, ref inp, g, op); return; }
        }
        // crawl spaces: duck
        if (InCrawl(pos) || (InCrawl(op) && Flat(pos).DistanceTo(Flat(op)) < 6f)) inp.Crouch = true;
        switch (o)
        {
            case VeinObj:
                {
                    var room = world.Cave.RoomAt(op);
                    var toC = room != null ? Flat(room.Center - op) : Vector3.Forward;
                    var stand = op + (toC.LengthSquared() < 0.01f ? Vector3.Forward : toC.Normalized()) * 1.9f;
                    EquipPickaxe(g);
                    if (Walk(m, ref inp, stand, 0.5f) || pos.DistanceTo(op) < 2.3f)
                    {
                        Face(m, ref inp, op + Vector3.Up * 0.8f); inp.Move = Vector2.Zero;
                        if (inv.Held == "pickaxe") inp.Attack = true;
                    }
                    break;
                }
            case PickupObj:
                if (Walk(m, ref inp, op, 1.2f)) { Face(m, ref inp, op); Interact(g, o, 0.4); if (++_targetTries > 12) Drop(); }
                break;
            case ChestObj:
                if (Walk(m, ref inp, op, 1.7f)) { Face(m, ref inp, op + Vector3.Up * 0.3f); inp.Move = Vector2.Zero; Interact(g, o, 0.6); if (++_targetTries > 40) Drop(); }
                break;
            case LeverObj:
                if (Walk(m, ref inp, op, 1.7f)) { Face(m, ref inp, op + Vector3.Up); inp.Move = Vector2.Zero; Interact(g, o, 0.8); if (++_targetTries > 6) Drop(); }
                break;
            case ShrineObj s:
                {
                    // timed trial: hit the start switch first
                    if (Kv(s, "type") == "timed" && Kv(s, "state") != "running")
                    {
                        var sw = world.Objects.Values.OfType<ShrinePlateObj>().Where(x => Kv(x, "mode") == "start").OrderBy(x => x.GlobalPosition.DistanceTo(op)).FirstOrDefault();
                        if (sw != null)
                        {
                            if (Walk(m, ref inp, sw.GlobalPosition, 1.4f)) { Face(m, ref inp, sw.GlobalPosition + Vector3.Up); inp.Move = Vector2.Zero; Interact(g, sw, 0.8); if (++_targetTries > 4) Drop(); }
                            break;
                        }
                    }
                    if (Walk(m, ref inp, op, 1.7f)) { Face(m, ref inp, op + Vector3.Up); inp.Move = Vector2.Zero; Interact(g, o, 0.8); if (++_targetTries > 10) Drop(); }
                    break;
                }
            case MonsterObj mo:
                {
                    EquipPickaxe(g);
                    float d = Flat(mo.GlobalPosition).DistanceTo(Flat(pos));
                    if (d > 2.6f) Walk(m, ref inp, op, 2.4f);
                    else { Face(m, ref inp, op + Vector3.Up * 1.5f); inp.Move = Vector2.Zero; if (inv.Held == "pickaxe") inp.Attack = true; }
                    if (++_targetTries > 3000) Drop();
                    break;
                }
            case OilDrumObj:
                if (Walk(m, ref inp, op, 1.6f)) { Face(m, ref inp, op + Vector3.Up * 0.5f); inp.Move = Vector2.Zero; Interact(g, o, 0.6); if (++_targetTries > 10) Drop(); }
                break;
            default: // trap: walk onto it, then through
                {
                    if (!_trapPassed)
                    {
                        if (Walk(m, ref inp, op, 0.35f))
                        {
                            if (_holdUntil == 0) { _holdUntil = _clock + (_deliberateTrap ? 6.0 : 2.5); _hpAtTrap = g.OwnHealth; _trapBeyond = op + Flat(op - pos).Normalized() * 5f; }
                            inp.Move = Vector2.Zero;
                            if (_clock > _holdUntil) { _trapPassed = true; _holdUntil = _clock + 4; }
                        }
                    }
                    else
                    {
                        Walk(m, ref inp, _trapBeyond, 1.2f);
                        if (_clock > _holdUntil) { _holdUntil = 0; if (_deliberateTrap) { if (g.OwnHealth < _hpAtTrap - 0.5f || ++_trapDuds >= 3) { _trapRuns++; Util.Log("EXPLORER deliberate trap run done hp " + _hpAtTrap + " -> " + g.OwnHealth); } _deliberateTrap = false; } Drop(); }
                    }
                    break;
                }
        }
    }

    static bool InCrawl(Vector3 p) { var mc = ModularCave.Last; return mc != null && mc.At((int)Math.Round(p.X / 2f), (int)Math.Round(p.Z / 2f)).Crawl; }

    bool Reachable(CaveInfo cave, Vector3 p, float maxDist)
    {
        int ni = cave.NearestNode(p);
        if (ni < 0 || !_comp[ni]) return false;
        return Flat(cave.NavNodes[ni]).DistanceTo(Flat(p)) <= maxDist;
    }

    Vector3 NearestCompNode(CaveInfo cave, Vector3 p)
    {
        float bd = float.MaxValue; Vector3 best = p;
        for (int i = 0; i < cave.NavNodes.Count; i++)
            if (_comp[i]) { float d = Flat(cave.NavNodes[i]).DistanceTo(Flat(p)); if (d < bd) { bd = d; best = cave.NavNodes[i]; } }
        return best;
    }

    /// <summary>Phase straight towards dest through thin walls: needs the Ethereal stein; fires Q when ready and walks while it is active.</summary>
    void Cross(Miner m, ref MinerInput inp, Game g, Vector3 dest)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        if (_clock > _etherealUntil)
        {
            // open line (same chamber): just walk
            var from = m.EyePos - new Vector3(0, 0.6f, 0); var to = new Vector3(dest.X, from.Y, dest.Z);
            var q = PhysicsRayQueryParameters3D.Create(from, to, Layers.ThinWall); q.Exclude = new Godot.Collections.Array<Rid> { m.GetRid() };
            if (m.GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0) { Face(m, ref inp, new Vector3(dest.X, m.EyePos.Y, dest.Z)); inp.Pitch = 0; inp.Move = new Vector2(0, 1); return; }
            inp.Move = Vector2.Zero;
            if (g.OwnAbility == "ethereal" && g.OwnAbilityReadyAt <= now + 0.05 && _actT <= 0) { g.SendAbility(); _etherealUntil = _clock + 4.6; _actT = 0.4; Util.Pass("explorer_ethereal_cross"); }
            return;
        }
        Face(m, ref inp, new Vector3(dest.X, m.EyePos.Y, dest.Z)); inp.Pitch = 0; inp.Move = new Vector2(0, 1);
    }

    void Drop() { if (_target != null) _skip.Add(_target.Id); _target = null; _holdUntil = 0; }

    void Interact(Game g, NetObject o, double every)
    {
        if (_actT > 0) return;
        _actT = every;
        g.SendInteract(o.Id);
        if (o is LeverObj) Util.Pass("explorer_lever_try");
        else if (o is ChestObj) Util.Pass("explorer_chest_try");
        else if (o is ShrineObj) Util.Pass("explorer_shrine_try");
    }

    void EquipPickaxe(Game g)
    {
        if (g.OwnInv.Held == "pickaxe") return;
        int idx = Array.IndexOf(g.OwnInv.Utils, "pickaxe");
        if (idx >= 0 && _actT <= 0) { g.SendSelectUtil(idx); _actT = 0.3; }
    }

    // ------------------------------------------------------------------ movement
    bool Walk(Miner m, ref MinerInput inp, Vector3 goal, float arrive)
    {
        var pos = m.GlobalPosition; var cave = Game.I.World.Cave;
        float flat = Flat(pos).DistanceTo(Flat(goal));
        if (flat <= arrive) return true;
        if (_path.Count == 0 || Flat(_pathGoal).DistanceTo(Flat(goal)) > 2f)
        {
            _path = cave.FindPath(pos, goal); _pathGoal = goal;
        }
        while (_path.Count > 1 && Flat(_path[0]).DistanceTo(Flat(pos)) < 1.3f) _path.RemoveAt(0);
        var wp = _path[0];
        if (_path.Count == 1) wp = goal;
        Face(m, ref inp, new Vector3(wp.X, m.EyePos.Y, wp.Z));
        inp.Pitch = 0; inp.Move = new Vector2(0, 1);
        inp.Sprint = flat > 8f && Game.I.OwnHealth > 20;
        _stuckT += 1.0 / 60.0;
        if (_stuckT > 1.2)
        {
            if ((pos - _lastPos).Length() < 0.35f)
            {
                _unstick = 0.6; _unstickDir = -_unstickDir; _stuckCount++; _path.Clear();
                if (_stuckCount > 5 && _target != null) { Util.Log($"EXPLORER {_name} stuck near {pos} heading to {_target.Kind}#{_target.Id}"); Drop(); _stuckCount = 0; }
            }
            _lastPos = pos; _stuckT = 0;
        }
        return false;
    }

    static void Face(Miner m, ref MinerInput inp, Vector3 point)
    {
        var d = point - m.EyePos;
        inp.Yaw = Mathf.Atan2(-d.X, -d.Z);
        inp.Pitch = Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()), -1.4f, 1.4f);
    }
}
