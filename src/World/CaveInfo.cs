using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Spawn marker inside a room. Kind: "vein", "ore" (loose), "monster", "dragon", "trap", "lever", "loot", "chest", "shrine".
/// "vein"/"ore" are spawned by Game.PopulateWorld; every other kind is handled by Game.MarkerSpawners[kind] (see Seams.cs).</summary>
public class SpawnMarker
{
    public string Kind;
    public Vector3 Pos;
    public SpawnMarker(string kind, Vector3 pos) { Kind = kind; Pos = pos; }
}

public class RoomInfo
{
    public int Id;
    /// <summary>Module name ("camp", "pit", "lair"...), for debugging and themed spawns.</summary>
    public string Name = "";
    public Aabb Bounds;
    public Vector3 Center;
    /// <summary>Rooms away from the camp (camp neighbours = 1). Richer ore and tougher monsters deeper.</summary>
    public int Depth;
    public List<SpawnMarker> Markers = new();
}

/// <summary>Everything the rest of the game needs to know about a built cave.</summary>
public class CaveInfo
{
    public Aabb CampZone;
    public Vector3 GoblinPos;
    public List<Vector3> MinerSpawns = new();
    public List<RoomInfo> Rooms = new();
    /// <summary>Walkable waypoint graph (used by bots, and later by monster AI).</summary>
    public List<Vector3> NavNodes = new();
    public List<(int a, int b)> NavEdges = new();
    /// <summary>Water pools (Ember Elementals die inside; puts out fire).</summary>
    public List<Aabb> WaterZones = new();
    public bool InWater(Vector3 p) { foreach (var w in WaterZones) if (w.HasPoint(p)) return true; return false; }

    public bool InCamp(Vector3 p) => CampZone.HasPoint(p);

    public RoomInfo RoomAt(Vector3 p)
    {
        foreach (var r in Rooms) if (r.Bounds.HasPoint(p)) return r;
        return null;
    }

    int Nearest(Vector3 p)
    {
        int best = 0; float bd = float.MaxValue;
        for (int i = 0; i < NavNodes.Count; i++)
        {
            float d = NavNodes[i].DistanceSquaredTo(p);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    List<int>[] _adj;
    public List<int>[] Adjacency()
    {
        if (_adj != null && _adj.Length == NavNodes.Count) return _adj;
        _adj = new List<int>[NavNodes.Count];
        for (int i = 0; i < _adj.Length; i++) _adj[i] = new List<int>(4);
        foreach (var (a, b) in NavEdges) { _adj[a].Add(b); _adj[b].Add(a); }
        return _adj;
    }

    public int NearestNode(Vector3 p) => NavNodes.Count == 0 ? -1 : Nearest(p);

    /// <summary>Waypoints from 'from' to 'to' (excluding 'from', including 'to'). A* over the nav graph; if 'to' is unreachable the result is just [to].</summary>
    public List<Vector3> FindPath(Vector3 from, Vector3 to)
    {
        var res = new List<Vector3>();
        if (NavNodes.Count == 0) { res.Add(to); return res; }
        int s = Nearest(from), t = Nearest(to);
        var adj = Adjacency();
        var dist = new float[NavNodes.Count]; var prev = new int[NavNodes.Count];
        for (int i = 0; i < dist.Length; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
        var pq = new PriorityQueue<int, float>();
        dist[s] = 0; pq.Enqueue(s, 0);
        bool found = false;
        while (pq.Count > 0)
        {
            int u = pq.Dequeue();
            if (u == t) { found = true; break; }
            foreach (int v in adj[u])
            {
                float nd = dist[u] + NavNodes[u].DistanceTo(NavNodes[v]);
                if (nd < dist[v]) { dist[v] = nd; prev[v] = u; pq.Enqueue(v, nd + NavNodes[v].DistanceTo(NavNodes[t])); }
            }
        }
        if (!found) { res.Add(to); return res; }
        var chain = new List<int>();
        for (int c = t; c >= 0; c = prev[c]) chain.Add(c);
        chain.Reverse();
        foreach (var n in chain) res.Add(NavNodes[n]);
        res.Add(to);
        return res;
    }
}

/// <summary>Swap point for the future procedural generator. Must be deterministic for a given seed
/// (every peer builds the cave locally; the server then spawns networked objects from the markers).</summary>
public interface ICaveBuilder
{
    CaveInfo Build(Node3D parent, int seed, int round);
}
