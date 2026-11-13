using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>What a monster can walk through: ceiling height, crawls, 0.85 m squeezes, tunnels. Set by each monster class so its collision fits where it may go.</summary>
public sealed class NavProfile
{
    public float MinCeil = 2.05f;
    public bool Crawl;          // may enter crawl cells (ceiling 1.1 m): only the Wicks
    public bool Squeeze = true; // may enter the 0.85 m wide squeezes
    public bool Water;
    public static readonly NavProfile Tall = new() { MinCeil = 2.05f, Squeeze = false };         // golem, plug: tunnel-sized, no squeezes
    public static readonly NavProfile Person = new() { MinCeil = 2.05f, Squeeze = true };         // lamplighter, assessor
    public static readonly NavProfile Swarm = new() { MinCeil = 0f, Crawl = true, Squeeze = true };// wicks
}

/// <summary>
/// Monster navigation over the cave: the cave's own nav graph (<see cref="CaveInfo.NavNodes"/>/<see cref="CaveInfo.NavEdges"/>) plus the crawl cells (only profiles with Crawl use them),
/// with per-node ceiling / tunnel / squeeze / Hall flags. Built once per cave on the server. Path distances are in metres.
/// </summary>
public sealed class MonsterNav
{
    public readonly CaveInfo Cave;
    public readonly List<Vector3> Pos = new();
    public readonly List<float> Ceil = new();
    public readonly List<bool> Crawl = new(), Tunnel = new(), Squeeze = new(), Water = new(), Hall = new();
    public readonly List<List<int>> Adj = new();
    public float[] HallDist;      // path metres to the nearest Hall-adjacent node (0 there), infinity when unreachable

    static MonsterNav _cached;
    public static MonsterNav For(CaveInfo cave)
    {
        if (_cached == null || _cached.Cave != cave) _cached = new MonsterNav(cave);
        return _cached;
    }

    MonsterNav(CaveInfo cave)
    {
        Cave = cave;
        var mc = ModularCave.Last;
        var zone = cave.HallZone.Grow(MonsterTuning.HallBuffer);
        int n = cave.NavNodes.Count;
        var cellNode = new Dictionary<long, int>();
        static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;
        for (int k = 0; k < n; k++)
        {
            var p = cave.NavNodes[k];
            Add(p, mc, zone, cave, cellNode, false);
        }
        foreach (var (a, b) in cave.NavEdges) { Adj[a].Add(b); Adj[b].Add(a); }
        if (mc != null)
        {
            // crawl cells: extra nodes joined to their walkable neighbours (the Wicks creep through them, nothing else does)
            var crawls = new List<(int i, int j)>();
            for (int j = mc.OY; j < mc.OY + mc.H; j++)
                for (int i = mc.OX; i < mc.OX + mc.W; i++)
                {
                    ref var c = ref mc.At(i, j);
                    if (c.Open && c.Crawl && !c.Thin) crawls.Add((i, j));
                }
            foreach (var (i, j) in crawls)
            {
                ref var c = ref mc.At(i, j);
                int idx = Add(ModularCave.CellPos(i, j, c.Mean), mc, zone, cave, cellNode, true);
                cellNode[Key(i, j)] = idx;
            }
            foreach (var (i, j) in crawls)
            {
                int me = cellNode[Key(i, j)];
                foreach (var (dx, dy) in ModularCave.Dirs)
                {
                    if (!mc.Linked(i, j, dx, dy)) continue;
                    if (!cellNode.TryGetValue(Key(i + dx, j + dy), out int other)) continue;
                    if (!Adj[me].Contains(other)) { Adj[me].Add(other); Adj[other].Add(me); }
                }
            }
        }
        ComputeHallDist();
    }

    int Add(Vector3 p, ModularCave mc, Aabb zone, CaveInfo cave, Dictionary<long, int> cellNode, bool crawl)
    {
        int idx = Pos.Count;
        Pos.Add(p);
        float ceil = 99f; bool tunnel = false, water = false;
        if (mc != null)
        {
            int i = (int)Math.Round(p.X / 2f), j = (int)Math.Round(p.Z / 2f);
            ref var c = ref mc.At(i, j);
            if (c.Open) { ceil = c.Ceil; tunnel = c.Owner >= 1000; water = c.Water; }
            if (!crawl) cellNode[((long)i << 32) ^ (uint)j] = idx;
        }
        Ceil.Add(ceil); Crawl.Add(crawl); Tunnel.Add(tunnel); Water.Add(water);
        Squeeze.Add(cave.InSqueeze(p + new Vector3(0, 0.8f, 0)));
        Hall.Add(zone.HasPoint(new Vector3(p.X, cave.HallZone.Position.Y + 1f, p.Z)));
        Adj.Add(new List<int>(4));
        return idx;
    }

    public int Count => Pos.Count;

    public bool Ok(NavProfile pr, int k)
    {
        if (Hall[k]) return false;
        if (Crawl[k]) return pr.Crawl;
        if (Ceil[k] < pr.MinCeil) return false;
        if (!pr.Squeeze && Squeeze[k]) return false;
        if (Water[k] && !pr.Water) return false;
        return true;
    }

    void ComputeHallDist()
    {
        int n = Count;
        HallDist = new float[n];
        var pq = new PriorityQueue<int, float>();
        for (int k = 0; k < n; k++)
        {
            HallDist[k] = float.PositiveInfinity;
            if (Hall[k]) continue;
            foreach (int o in Adj[k]) if (Hall[o]) { HallDist[k] = 0f; break; }
            // nodes whose neighbours were cut out of the graph (Hall furniture) still touch the Hall zone from outside: treat a node within 3 m of the zone as adjacent
            if (float.IsPositiveInfinity(HallDist[k]) && Cave.HallZone.Grow(3f).HasPoint(new Vector3(Pos[k].X, Cave.HallZone.Position.Y + 1f, Pos[k].Z))) HallDist[k] = 0f;
            if (HallDist[k] == 0f) pq.Enqueue(k, 0f);
        }
        while (pq.Count > 0)
        {
            int u = pq.Dequeue();
            foreach (int v in Adj[u])
            {
                if (Hall[v]) continue;
                float nd = HallDist[u] + Pos[u].DistanceTo(Pos[v]);
                if (nd < HallDist[v]) { HallDist[v] = nd; pq.Enqueue(v, nd); }
            }
        }
    }

    // ---------------------------------------------------------------- lookup
    /// <summary>Nearest node the profile may stand on (within ~maxDist metres), or -1.</summary>
    public int Nearest(NavProfile pr, Vector3 p, float maxDist = 40f)
    {
        int best = -1; float bd = maxDist * maxDist;
        for (int k = 0; k < Pos.Count; k++)
        {
            float d = Pos[k].DistanceSquaredTo(p);
            if (d < bd && Ok(pr, k)) { bd = d; best = k; }
        }
        return best;
    }

    // ---------------------------------------------------------------- search
    /// <summary>A* from 'from' to 'to'. Result excludes the start node, ends at the goal node. Null when there is no route for this profile.</summary>
    public List<Vector3> FindPath(NavProfile pr, Vector3 from, Vector3 to)
    {
        int s = Nearest(pr, from), t = Nearest(pr, to);
        if (s < 0 || t < 0) return null;
        int n = Count;
        var dist = new float[n]; var prev = new int[n];
        for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
        var pq = new PriorityQueue<int, float>();
        dist[s] = 0; pq.Enqueue(s, 0);
        bool found = false;
        while (pq.Count > 0)
        {
            int u = pq.Dequeue();
            if (u == t) { found = true; break; }
            foreach (int v in Adj[u])
            {
                if (!Ok(pr, v)) continue;
                float nd = dist[u] + Pos[u].DistanceTo(Pos[v]);
                if (nd < dist[v]) { dist[v] = nd; prev[v] = u; pq.Enqueue(v, nd + Pos[v].DistanceTo(Pos[t])); }
            }
        }
        if (!found) return null;
        var chain = new List<int>();
        for (int c = t; c >= 0; c = prev[c]) chain.Add(c);
        chain.Reverse();
        var res = new List<Vector3>(chain.Count);
        for (int i = 1; i < chain.Count; i++) res.Add(Pos[chain[i]]);
        if (res.Count == 0) res.Add(Pos[t]);
        return res;
    }

    /// <summary>Path metres from node 'start' to every node the profile can reach (infinity elsewhere).</summary>
    public float[] Distances(NavProfile pr, int start)
    {
        int n = Count;
        var dist = new float[n];
        for (int i = 0; i < n; i++) dist[i] = float.PositiveInfinity;
        if (start < 0) return dist;
        var pq = new PriorityQueue<int, float>();
        dist[start] = 0; pq.Enqueue(start, 0);
        while (pq.Count > 0)
        {
            int u = pq.Dequeue();
            foreach (int v in Adj[u])
            {
                if (!Ok(pr, v)) continue;
                float nd = dist[u] + Pos[u].DistanceTo(Pos[v]);
                if (nd < dist[v]) { dist[v] = nd; pq.Enqueue(v, nd); }
            }
        }
        return dist;
    }

    /// <summary>A random reachable node at least minPath metres away (falls back to anything reachable). Optionally only tunnel cells. Returns its position, or null.</summary>
    public Vector3? RandomGoal(NavProfile pr, Vector3 from, float minPath, float maxPath = 200f, bool tunnelsOnly = false, float minHallDist = 0f)
    {
        int s = Nearest(pr, from);
        if (s < 0) return null;
        var d = Distances(pr, s);
        var good = new List<int>(); var any = new List<int>();
        for (int k = 0; k < d.Length; k++)
        {
            if (float.IsPositiveInfinity(d[k]) || k == s || HallDist[k] < minHallDist) continue;
            if (tunnelsOnly && !Tunnel[k]) continue;
            any.Add(k);
            if (d[k] >= minPath && d[k] <= maxPath) good.Add(k);
        }
        var pool = good.Count > 0 ? good : any;
        if (pool.Count == 0) return null;
        return Pos[pool[(int)(GD.Randi() % (uint)pool.Count)]];
    }

    /// <summary>Spawn cells: reachable from the Hall region, >= minHall path metres from the Hall, spread out. Shuffled with 'rng'; relaxes the spacing when the cave is small.</summary>
    public List<Vector3> SpawnCells(NavProfile pr, int count, float minHall, float spacing, RandomNumberGenerator rng, List<Vector3> taken, bool tunnelsOnly = false)
    {
        var cand = new List<int>();
        for (int k = 0; k < Count; k++)
        {
            if (!Ok(pr, k) || float.IsPositiveInfinity(HallDist[k]) || HallDist[k] < minHall) continue;
            if (tunnelsOnly && !Tunnel[k]) continue;
            cand.Add(k);
        }
        for (int i = cand.Count - 1; i > 0; i--) { int j = (int)(rng.Randi() % (uint)(i + 1)); (cand[i], cand[j]) = (cand[j], cand[i]); }
        var res = new List<Vector3>();
        foreach (float sp in new[] { spacing, spacing * 0.6f, spacing * 0.3f, 0f })
        {
            if (res.Count >= count) break;
            foreach (int k in cand)
            {
                if (res.Count >= count) break;
                var p = Pos[k];
                bool far = true;
                foreach (var t in taken) if (t.DistanceTo(p) < sp) { far = false; break; }
                if (far) foreach (var t in res) if (t.DistanceTo(p) < sp) { far = false; break; }
                if (far) res.Add(p);
            }
        }
        return res;
    }
}
