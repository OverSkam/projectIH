using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Soak-test instrumentation (host only, --soak): per round it audits what the markers produced (counts, placement sanity,
/// lever mechanisms, shrine parts, reachability from the camp) and tracks monster movement. Output: SOAKROUND / SOAKBAD / SOAKMON lines.</summary>
[ContentModule]
public static class SoakModule
{
    public static void Register()
    {
        if (!Util.HasArg("soak")) return;
        Game.RoundPopulated += (g, rng) => { if (g.World != null && Net.I.IsServer) g.World.AddChild(new SoakDirector { Name = "SoakDirector" }); };
    }
}

public partial class SoakDirector : Node
{
    double _t;
    bool _audited;
    double _sample;
    class MonTrack { public string Kind; public Vector3 Last; public float Dist; public HashSet<int> Rooms = new(); public int StuckEvents; public Vector3 StuckRef; public double StuckSince; }
    readonly Dictionary<int, MonTrack> _mon = new();

    static string Fmt(Vector3 v) => $"({v.X:0.#},{v.Y:0.#},{v.Z:0.#})";

    public override void _PhysicsProcess(double delta)
    {
        var g = Game.I;
        if (g?.World == null || g.Phase != GamePhase.Round) return;
        _t += delta;
        if (!_audited && _t > 3.0) { _audited = true; Audit(g); }
        _sample -= delta;
        if (_sample <= 0) { _sample = 0.5; SampleMonsters(g); }
    }

    void SampleMonsters(Game g)
    {
        foreach (var o in g.World.Objects.Values.ToArray())
        {
            if (o is not MonsterObj m || m.State == MonsterState.Dead) continue;
            if (!_mon.TryGetValue(m.Id, out var t)) _mon[m.Id] = t = new MonTrack { Kind = m.Kind, Last = m.GlobalPosition, StuckRef = m.GlobalPosition, StuckSince = _t };
            var p = m.GlobalPosition;
            t.Dist += new Vector3(p.X - t.Last.X, 0, p.Z - t.Last.Z).Length();
            t.Last = p;
            var room = g.World.Cave.RoomAt(p);
            if (room != null) t.Rooms.Add(room.Id);
            bool chasing = m.State == MonsterState.Chase || m.State == MonsterState.Investigate || m.State == MonsterState.Wander || m.State == MonsterState.Return;
            if (!chasing || new Vector3(p.X - t.StuckRef.X, 0, p.Z - t.StuckRef.Z).Length() > 0.5f) { t.StuckRef = p; t.StuckSince = _t; }
            else if (_t - t.StuckSince > 8.0) { t.StuckEvents++; t.StuckSince = _t; Util.Log($"SOAKSTUCK kind={m.Kind} id={m.Id} state={m.State} at={Fmt(p)}"); }
        }
    }

    public override void _ExitTree()
    {
        foreach (var kv in _mon)
            Util.Log($"SOAKMON kind={kv.Value.Kind} id={kv.Key} dist={kv.Value.Dist:0} rooms={kv.Value.Rooms.Count} stuck={kv.Value.StuckEvents}");
    }

    void Bad(string what) => Util.Fail("soak_bad", what);

    void Audit(Game g)
    {
        var w = g.World; var cave = w.Cave; var mc = ModularCave.Last;
        var byKind = w.Objects.Values.GroupBy(o => o.Kind).ToDictionary(k => k.Key, k => k.Count());
        var markers = cave.Rooms.SelectMany(r => r.Markers).GroupBy(m => m.Kind).ToDictionary(k => k.Key, k => k.Count());
        int C(Dictionary<string, int> d, string k) => d.GetValueOrDefault(k);
        int traps = byKind.Where(kv => kv.Key.StartsWith("trap_") && kv.Key != "trap_lever" && kv.Key != "trap_gate").Sum(kv => kv.Value);
        int monsters = C(byKind, "golem") + C(byKind, "ember") + C(byKind, "wisp");
        Util.Log($"SOAKROUND r={g.Round} rooms={cave.Rooms.Count} markers[{string.Join(",", markers.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))}] " +
                 $"objects[{string.Join(",", byKind.OrderBy(k => k.Key).Select(k => k.Key + "=" + k.Value))}] monsters={monsters} traps={traps} levers={C(byKind, "trap_lever")}");
        int campMonsters = 0;

        // counts that must match exactly
        if (C(byKind, "vein") != C(markers, "vein")) Bad($"r={g.Round} veins {C(byKind, "vein")} != markers {C(markers, "vein")}");
        if (C(byKind, "chest") != C(markers, "chest")) Bad($"r={g.Round} chests {C(byKind, "chest")} != markers {C(markers, "chest")}");
        int shrineCap = Math.Min(C(markers, "shrine"), Math.Max(1, cave.Rooms.Count / 4));
        if (C(byKind, "shrine") != shrineCap) Bad($"r={g.Round} shrines {C(byKind, "shrine")} != expected {shrineCap} (markers {C(markers, "shrine")}, 1 per 4 rooms)");
        if (monsters > MonsterTuning.MaxTotal(g.Round)) Bad($"r={g.Round} {monsters} monsters exceed the cap {MonsterTuning.MaxTotal(g.Round)}");
        if (C(byKind, "dragon") != C(markers, "dragon")) Bad($"r={g.Round} dragon {C(byKind, "dragon")} != markers {C(markers, "dragon")}");
        if (g.Round >= 3 && C(byKind, "dragon") != 1) Bad($"r={g.Round} no dragon in round >= 3");

        // nav component reachable from the camp
        var adj = cave.Adjacency();
        var comp = new bool[cave.NavNodes.Count];
        int start = cave.NearestNode(cave.GoblinPos + new Vector3(0, 0, 2.3f));
        var q = new Queue<int>(); if (start >= 0) { q.Enqueue(start); comp[start] = true; }
        while (q.Count > 0) { int u = q.Dequeue(); foreach (int v in adj[u]) if (!comp[v]) { comp[v] = true; q.Enqueue(v); } }

        var space = w.GetWorld3D().DirectSpaceState;
        var exclude = new Godot.Collections.Array<Rid>();
        foreach (var o in w.Objects.Values) exclude.Add(o.GetRid());
        foreach (var o in w.Objects.Values.ToArray())
        {
            if (o is GoblinObj) continue;
            var p = o.GlobalPosition;
            string tag = $"r={g.Round} {o.Kind}#{o.Id} at={Fmt(p)}";
            var room = cave.RoomAt(p);
            // inside rock / outside the cave grid
            if (mc != null)
            {
                int i = (int)Math.Round(p.X / 2f), j = (int)Math.Round(p.Z / 2f);
                if (!mc.OpenAt(i, j) && Math.Abs(p.X - i * 2f) < 0.65f && Math.Abs(p.Z - j * 2f) < 0.65f)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int dj = -6; dj <= 6; dj++) { sb.Append(' '); for (int di = -6; di <= 6; di++) sb.Append(di == 0 && dj == 0 ? 'X' : mc.OpenAt(i + di, j + dj) ? '.' : '#'); }
                    Bad($"{tag} in rock (cell closed) room={room?.Name} grid(x right, z down):{sb}"); continue;
                }
                if (!mc.OpenAt(i, j)) continue;   // within a wall's edge band (monster body against a wall): fine
            }
            bool flyer = o.Kind == "wisp";
            if (!flyer)
            {
                var from = p + new Vector3(0, 0.7f, 0);
                var down = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 3.5f, 1); down.Exclude = exclude;
                var hit = space.IntersectRay(down);
                if (hit.Count == 0) Bad($"{tag} mid-air (no floor within 3.5 m)");
                else
                {
                    float gap = p.Y - hit["position"].AsVector3().Y;
                    if (gap > 1.3f) Bad($"{tag} mid-air ({gap:0.0} m above floor)");
                    if (gap < -0.45f) Bad($"{tag} sunk into floor ({gap:0.0})");
                }
                var up = PhysicsRayQueryParameters3D.Create(p + new Vector3(0, 0.3f, 0), p + new Vector3(0, 3.5f, 0), 1); up.Exclude = exclude;
                var uh = space.IntersectRay(up);
                float need = o is MonsterObj ? 2.0f : 0.6f;
                if (uh.Count > 0 && uh["position"].AsVector3().Y - p.Y < need && !(mc != null && IsCrawl(mc, p)) && !o.Kind.StartsWith("trap_")) Bad($"{tag} no headroom ({uh["position"].AsVector3().Y - p.Y:0.0} m)");
            }
            if (o is MonsterObj && cave.InCamp(p)) campMonsters++;
            // reachability for things a miner must get to
            bool needsReach = o is ChestObj || o is ShrineObj || o is VeinObj || o is MonsterObj || o.Kind == "trap_lever" || (o is PickupObj && !flyer);
            if (needsReach)
            {
                bool thin = room?.Name == "nook" || (mc != null && IsThinBehind(mc, p));
                int ni = cave.NearestNode(p);
                float dn = ni >= 0 ? cave.NavNodes[ni].DistanceTo(new Vector3(p.X, cave.NavNodes[ni].Y, p.Z)) : 99f;
                if (!thin && (ni < 0 || !comp[ni] || dn > 3.2f) && !(mc != null && IsCrawl(mc, p))) Bad($"{tag} not reachable from camp (node dist {dn:0.0}, connected={(ni >= 0 && comp[ni])})");
            }
            // links
            if (o.Kind == "trap_lever")
            {
                var kv = NetObject.Kv(o.Data);
                if (!int.TryParse(kv.GetValueOrDefault("mid", "-1"), out var mid) || !w.Objects.ContainsKey(mid)) Bad($"{tag} lever has no mechanism");
            }
            if (o is ShrineObj sh)
            {
                string type = NetObject.Kv(o.Data).GetValueOrDefault("type", "");
                if (type == "plate" && sh.Plate == null) Bad($"{tag} plate shrine without plate");
                if (type == "timed" && (sh.Barrier == null || sh.Start == null)) Bad($"{tag} timed shrine without barrier/start");
                if (sh.Plate != null && !IsFloorOk(space, exclude, sh.Plate.GlobalPosition)) Bad($"{tag} plate has no floor");
                if (sh.Start != null && !IsFloorOk(space, exclude, sh.Start.GlobalPosition)) Bad($"{tag} start switch has no floor");
            }
        }
        if (campMonsters > 0) Bad($"r={g.Round} {campMonsters} monsters spawned inside the camp");
        // rooms next to the camp must be free of monsters and lethal traps (pacing rule)
        foreach (var o in w.Objects.Values)
        {
            var room = cave.RoomAt(o.GlobalPosition);
            if (room == null || room.Depth != 1) continue;
            if (o is MonsterObj || o.Kind is "trap_spikes" or "trap_rockfall" or "trap_trapdoor" or "trap_gas") Bad($"r={g.Round} {o.Kind}#{o.Id} in camp-adjacent room {room.Name}");
        }
        Util.Pass("soak_audit", $"round={g.Round}");
    }

    static bool IsCrawl(ModularCave mc, Vector3 p) { int i = (int)Math.Round(p.X / 2f), j = (int)Math.Round(p.Z / 2f); return mc.At(i, j).Crawl; }
    static bool IsThinBehind(ModularCave mc, Vector3 p) { int i = (int)Math.Round(p.X / 2f), j = (int)Math.Round(p.Z / 2f); return mc.At(i, j).Thin; }

    static bool IsFloorOk(PhysicsDirectSpaceState3D space, Godot.Collections.Array<Rid> ex, Vector3 p)
    {
        var from = p + new Vector3(0, 0.7f, 0);
        var q = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 2.5f, 1); q.Exclude = ex;
        return space.IntersectRay(q).Count > 0;
    }
}
