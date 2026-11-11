using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

public struct Cell
{
    public bool Open, Ramp, Thin, Water, Crawl;
    public int Owner;
    public float Ceil;
    /// <summary>Floor height at the corners: A(x-,z-) B(x+,z-) C(x-,z+) D(x+,z+).</summary>
    public float A, B, C, D;
    public float Min => Math.Min(Math.Min(A, B), Math.Min(C, D));
    public float Mean => (A + B + C + D) * 0.25f;
}

public class Slot
{
    public int Id, Gx, Gy, Rot, Depth;
    public CaveModule Mod;
    public string[] Map;
    public bool[] R = new bool[4];           // connected sides N E S W
    public List<int> Adj = new();
    public int OX => Gx * ModularCave.Pitch - 7;
    public int OY => Gy * ModularCave.Pitch - 7;
}

public class RampDef { public float X0, Z0, X1, Z1, H0, H1; }

public class TunnelDef { public int Owner; public bool AlongX; public float X, Z; public int Depth; public List<(int i, int j)> Cells = new(); public Slot A, B; }

/// <summary>Modular cave: rooms assembled from hand-built modules on a slot grid, joined by tunnels. Deterministic per (seed, round).</summary>
public class ModularCave : ICaveBuilder
{
    public const int Pitch = 18;
    public const float CellSize = 2f;
    public static readonly (int dx, int dy)[] Dirs = { (0, -1), (1, 0), (0, 1), (-1, 0) };

    public CaveInfo Info = new();
    public Cell[] G; public int W, H, OX, OY;
    public List<Slot> Slots = new();
    public List<RampDef> Ramps = new();
    public List<TunnelDef> Tunnels = new();
    public List<string> Errors = new();
    public int Seed, Round, LoopCount, Salt;
    public int[] NodeAt;            // per cell: nav node index or -1
    public Dictionary<int, int> OwnerDepth = new();

    // ------------------------------------------------------------------ ICaveBuilder
    public CaveInfo Build(Node3D parent, int seed, int round)
    {
        if (Util.HasArg("caveseed") && int.TryParse(Util.Arg("caveseed"), out var s)) seed = s;
        if (Util.HasArg("caveround") && int.TryParse(Util.Arg("caveround"), out var r)) round = r;
        var g = Generate(seed, round);
        foreach (var e in g.Errors) Util.Fail("cave_layout", e);
        CaveBuilder.Emit(g, parent);
        if (Util.HasArg("cavelist"))
            foreach (var sl in g.Slots) Util.Log($"CAVE room={sl.Id} {sl.Mod.Name} rot={sl.Rot} depth={sl.Depth} center={g.Info.Rooms[sl.Id].Center}");
        return g.Info;
    }

    public ref Cell At(int i, int j)
    {
        int x = i - OX, y = j - OY;
        if (x < 0 || y < 0 || x >= W || y >= H) return ref _void;
        return ref G[y * W + x];
    }
    static Cell _void;
    public bool OpenAt(int i, int j) { int x = i - OX, y = j - OY; return x >= 0 && y >= 0 && x < W && y < H && G[y * W + x].Open; }
    public RoomInfo RoomAt(Vector3 p) { int i = (int)Math.Round(p.X / 2f), j = (int)Math.Round(p.Z / 2f); var o = At(i, j).Owner; return At(i, j).Open && o < Info.Rooms.Count ? Info.Rooms[o] : null; }
    public int Idx(int i, int j) => (j - OY) * W + (i - OX);
    public static Vector3 CellPos(int i, int j, float y = 0f) => new Vector3(i * CellSize, y, j * CellSize);

    // ------------------------------------------------------------------ generation
    public static ModularCave Generate(int seed, int round)
    {
        ModularCave g = null;
        for (int salt = 0; salt < 20; salt++)
        {
            g = new ModularCave { Seed = seed, Round = round, Salt = salt };
            g.Topology();
            if (g.LoopCount > 0 || g.Slots.Count < 6) break;
        }
        g.Assign();
        g.Stamp();
        g.Finish();
        return g;
    }

    void Topology()
    {
        var rnd = new Rnd(Seed * 7919L + Round * 104729L + Salt * 1299709L + 17);
        int target = (int)(8 + (Round - 1) * 2.5f + 0.5f);
        var at = new Dictionary<(int, int), Slot>();
        Slot Add(int gx, int gy) { var s = new Slot { Id = Slots.Count, Gx = gx, Gy = gy }; Slots.Add(s); at[(gx, gy)] = s; return s; }
        void Connect(Slot a, Slot b, int d) { a.R[d] = true; b.R[(d + 2) % 4] = true; a.Adj.Add(b.Id); b.Adj.Add(a.Id); }
        var camp = Add(0, 0);
        int guard = 0;
        while (Slots.Count < target && guard++ < 5000)
        {
            Slot from = Slots.Count < 3 ? camp : Slots[rnd.I(Slots.Count)];
            int d = rnd.I(4);
            var (dx, dy) = Dirs[d];
            if (at.ContainsKey((from.Gx + dx, from.Gy + dy))) continue;
            if (from == camp && camp.Adj.Count >= 3) continue;
            var n = Add(from.Gx + dx, from.Gy + dy);
            Connect(from, n, d);
        }
        // loops: connect adjacent but unconnected slots
        var cands = new List<(Slot, Slot, int)>();
        foreach (var s in Slots)
            for (int d = 0; d < 2; d++) // E and S only, each pair once
            {
                var (dx, dy) = Dirs[1 + d];
                if (at.TryGetValue((s.Gx + dx, s.Gy + dy), out var o) && !s.Adj.Contains(o.Id)) cands.Add((s, o, 1 + d));
            }
        rnd.Shuffle(cands);
        foreach (var (a, b, d) in cands)
            if (LoopCount == 0 || rnd.Chance(0.4f)) { Connect(a, b, d); LoopCount++; }
        // depth = graph distance from camp
        foreach (var s in Slots) s.Depth = -1;
        camp.Depth = 0;
        var q = new Queue<Slot>(); q.Enqueue(camp);
        while (q.Count > 0) { var u = q.Dequeue(); foreach (var v in u.Adj) if (Slots[v].Depth < 0) { Slots[v].Depth = u.Depth + 1; q.Enqueue(Slots[v]); } }
        _rnd = rnd;
    }
    Rnd _rnd;

    bool Fits(CaveModule m, Slot s, out List<int> rots)
    {
        rots = new List<int>();
        for (int r = 0; r < 4; r++)
        {
            var d = CaveModule.DoorsOf(m.Rotated(r));
            bool ok = true;
            for (int k = 0; k < 4; k++) if (s.R[k] && !d[k]) ok = false;
            if (ok) rots.Add(r);
        }
        return rots.Count > 0;
    }

    void Set(Slot s, CaveModule m)
    {
        Fits(m, s, out var rots);
        s.Mod = m; s.Rot = rots[_rnd.I(rots.Count)]; s.Map = m.Rotated(s.Rot);
    }

    void Assign()
    {
        var rnd = _rnd;
        var camp = Slots[0];
        camp.Mod = CaveModule.Get("camp"); camp.Rot = 0; camp.Map = camp.Mod.Map;
        Slot lair = null;
        if (Round >= 3)
        {
            int md = Slots.Max(s => s.Depth);
            var deepest = Slots.Where(s => s.Depth == md && s != camp).ToList();
            lair = deepest[rnd.I(deepest.Count)];
            Set(lair, CaveModule.Get("lair"));
        }
        var leaves = Slots.Where(s => s != camp && s != lair && s.Adj.Count == 1).ToList();
        rnd.Shuffle(leaves);
        if (leaves.Count > 0) Set(leaves[0], CaveModule.Get("shrine"));
        if (leaves.Count > 1) Set(leaves[1], CaveModule.Get("nook"));
        if (leaves.Count > 2 && Round >= 3 && rnd.Chance(0.5f)) Set(leaves[2], CaveModule.Get("shrine"));
        var used = new Dictionary<string, int>();
        foreach (var s in Slots) if (s.Mod != null) used[s.Mod.Name] = used.GetValueOrDefault(s.Mod.Name) + 1;
        var order = Slots.Where(s => s.Mod == null).ToList();
        rnd.Shuffle(order);
        foreach (var s in order)
        {
            for (int pass = 0; pass < 3; pass++)
            {
                var pool = CaveModule.All.Where(m => m.Role == "" && m.Weight > 0 && m.MinRound <= Round && Fits(m, s, out _)).ToList();
                if (pass < 2) pool = pool.Where(m => used.GetValueOrDefault(m.Name) < m.Max).ToList();
                if (pass < 1) pool = pool.Where(m => !s.Adj.Any(a => Slots[a].Mod?.Name == m.Name)).ToList();
                if (pool.Count == 0) continue;
                float tot = pool.Sum(m => m.Weight - 0.4f * used.GetValueOrDefault(m.Name)).Clamp(0.01f);
                float x = rnd.F() * tot; CaveModule pick = pool[^1];
                foreach (var m in pool) { x -= Math.Max(0.05f, m.Weight - 0.4f * used.GetValueOrDefault(m.Name)); if (x <= 0) { pick = m; break; } }
                Set(s, pick); used[pick.Name] = used.GetValueOrDefault(pick.Name) + 1;
                break;
            }
            if (s.Mod == null) { Set(s, CaveModule.Get("cavern")); }
        }
    }

    // ------------------------------------------------------------------ stamping into the global grid
    static float FlatH(char c) => c switch { 'w' => -1.2f, ':' or 'K' => -2f, ';' or 'Q' or 'O' => -5f, _ => 0f };
    static string MarkerOf(char c) => c switch
    {
        'v' => "vein", 'o' or 'O' => "ore", 'm' => "monster", 'D' => "dragon", 't' => "trap", 'l' => "lever",
        'L' or 'Q' or 'K' => "loot", 'C' => "chest", 'S' => "shrine", _ => null,
    };
    static bool IsRamp(char c) => c == '8' || c == '6' || c == '2' || c == '4';

    void Stamp()
    {
        int gx0 = Slots.Min(s => s.Gx), gx1 = Slots.Max(s => s.Gx), gy0 = Slots.Min(s => s.Gy), gy1 = Slots.Max(s => s.Gy);
        OX = gx0 * Pitch - 7 - 2; OY = gy0 * Pitch - 7 - 2;
        W = (gx1 - gx0) * Pitch + 14 + 4; H = (gy1 - gy0) * Pitch + 14 + 4;
        G = new Cell[W * H];
        foreach (var s in Slots) { StampSlot(s); OwnerDepth[s.Id] = s.Depth; }
        // tunnels
        int tid = 1000;
        foreach (var a in Slots)
            for (int d = 1; d <= 2; d++)
            {
                if (!a.R[d]) continue;
                var (dx, dy) = Dirs[d];
                var b = Slots.First(s => s.Gx == a.Gx + dx && s.Gy == a.Gy + dy);
                var t = new TunnelDef { Owner = tid++, AlongX = d == 1, A = a, B = b, Depth = Math.Min(a.Depth, b.Depth) };
                OwnerDepth[t.Owner] = t.Depth;
                for (int k = 0; k < Pitch - 14; k++)
                    for (int w = 6; w <= 7; w++)
                    {
                        int i = d == 1 ? a.OX + 14 + k : a.OX + w, j = d == 1 ? a.OY + w : a.OY + 14 + k;
                        ref var c = ref At(i, j);
                        c = new Cell { Open = true, Owner = t.Owner, Ceil = 4.5f };
                        t.Cells.Add((i, j));
                    }
                t.X = (float)t.Cells.Average(c => c.i) * CellSize; t.Z = (float)t.Cells.Average(c => c.j) * CellSize;
                Tunnels.Add(t);
            }
    }

    void StampSlot(Slot s)
    {
        var map = s.Map;
        char Ch(int r, int c) => r < 0 || c < 0 || r >= 14 || c >= 14 ? '#' : map[r][c];
        for (int r = 0; r < 14; r++)
            for (int c = 0; c < 14; c++)
            {
                char ch = map[r][c];
                if (ch == '#') continue;
                if ((r == 0 && (c == 6 || c == 7) && !s.R[0]) || (r == 13 && (c == 6 || c == 7) && !s.R[2]) ||
                    (c == 13 && (r == 6 || r == 7) && !s.R[1]) || (c == 0 && (r == 6 || r == 7) && !s.R[3])) continue;
                int i = s.OX + c, j = s.OY + r;
                var cell = new Cell { Open = true, Owner = s.Id, Ceil = s.Mod.Ceil };
                float h = FlatH(ch);
                cell.A = cell.B = cell.C = cell.D = h;
                if (ch == 'w') cell.Water = true;
                if (ch == 'W') cell.Thin = true;
                bool nearCrawl = Ch(r - 1, c) == 'x' || Ch(r + 1, c) == 'x' || Ch(r, c - 1) == 'x' || Ch(r, c + 1) == 'x';
                if (ch == 'x' || (MarkerOf(ch) != null && nearCrawl)) { cell.Ceil = 1.7f; cell.Crawl = true; }
                if (IsRamp(ch))
                {
                    var (dr, dc) = ch switch { '8' => (-1, 0), '2' => (1, 0), '6' => (0, 1), _ => (0, -1) };
                    int k = 0, rr = r, cc = c;
                    while (Ch(rr - dr, cc - dc) == ch) { rr -= dr; cc -= dc; k++; }
                    int n = k + 1; rr = r; cc = c;
                    while (Ch(rr + dr, cc + dc) == ch) { rr += dr; cc += dc; n++; }
                    int lr = r - dr * (k + 1), lc = c - dc * (k + 1), hr = rr + dr, hc = cc + dc; // flat neighbours at the ends
                    char lo = Ch(lr, lc), hi = Ch(hr, hc);
                    if (lo == '#' || hi == '#' || IsRamp(lo) || IsRamp(hi)) Errors.Add($"{s.Mod.Name}: ramp at r{r} c{c} has no flat ends");
                    float hl = FlatH(lo), hh = FlatH(hi);
                    float e0 = hl + (hh - hl) * k / n, e1 = hl + (hh - hl) * (k + 1) / n;
                    if (Math.Abs(hh - hl) / (n * 2f) > 0.7f) Errors.Add($"{s.Mod.Name}: ramp at r{r} c{c} too steep");
                    cell.Ramp = true;
                    switch (ch)
                    {
                        case '8': cell.A = cell.B = e1; cell.C = cell.D = e0; break;
                        case '2': cell.C = cell.D = e1; cell.A = cell.B = e0; break;
                        case '6': cell.B = cell.D = e1; cell.A = cell.C = e0; break;
                        default: cell.A = cell.C = e1; cell.B = cell.D = e0; break;
                    }
                    if (k == 0)
                    {
                        // plan positions of the low and high edge (world metres)
                        var ramp = new RampDef { H0 = hl, H1 = hh };
                        float cx = i * CellSize, cz = j * CellSize;
                        ramp.X0 = cx - dc; ramp.Z0 = cz - dr;
                        ramp.X1 = cx + dc * (2 * n - 1); ramp.Z1 = cz + dr * (2 * n - 1);
                        Ramps.Add(ramp);
                    }
                }
                At(i, j) = cell;
            }
    }

    // ------------------------------------------------------------------ info, markers, nav graph
    static bool Same(float a, float b) => Math.Abs(a - b) < 0.05f;

    /// <summary>Walkable link between two adjacent cells (shared edge at the same height on both sides).</summary>
    public bool Linked(int i, int j, int di, int dj)
    {
        var a = At(i, j); var b = At(i + di, j + dj);
        if (!a.Open || !b.Open) return false;
        if (di == 1) return Same(a.B, b.A) && Same(a.D, b.C);
        if (di == -1) return Same(a.A, b.B) && Same(a.C, b.D);
        if (dj == 1) return Same(a.C, b.A) && Same(a.D, b.B);
        return Same(a.A, b.C) && Same(a.B, b.D);
    }

    void Finish()
    {
        var info = Info;
        foreach (var s in Slots)
        {
            var ri = new RoomInfo
            {
                Id = s.Id, Name = s.Mod.Name, Depth = s.Depth,
                Bounds = new Aabb(new Vector3(s.OX * CellSize - 1, -12, s.OY * CellSize - 1), new Vector3(28, 26, 28)),
            };
            ri.Center = ri.Bounds.GetCenter() with { Y = 0 };
            for (int r = 0; r < 14; r++)
                for (int c = 0; c < 14; c++)
                {
                    var k = MarkerOf(s.Map[r][c]);
                    if (k == null || !OpenAt(s.OX + c, s.OY + r)) continue;
                    ri.Markers.Add(new SpawnMarker(k, CellPos(s.OX + c, s.OY + r, FlatH(s.Map[r][c]))));
                }
            info.Rooms.Add(ri);
        }
        var camp = Slots[0];
        info.CampZone = new Aabb(new Vector3(camp.OX * CellSize - 1, -2, camp.OY * CellSize - 1), new Vector3(28, 14, 28));
        info.GoblinPos = CellPos(camp.OX + 4, camp.OY + 2);
        for (int c = 3; c <= 10; c++) info.MinerSpawns.Add(CellPos(camp.OX + c, camp.OY + 9, 0.3f));
        for (int c = 4; c <= 9; c++) info.MinerSpawns.Add(CellPos(camp.OX + c, camp.OY + 10, 0.3f));
        // water zones
        foreach (var s in Slots)
        {
            int i0 = int.MaxValue, i1 = int.MinValue, j0 = int.MaxValue, j1 = int.MinValue;
            for (int r = 0; r < 14; r++) for (int c = 0; c < 14; c++)
                if (s.Map[r][c] == 'w') { i0 = Math.Min(i0, s.OX + c); i1 = Math.Max(i1, s.OX + c); j0 = Math.Min(j0, s.OY + r); j1 = Math.Max(j1, s.OY + r); }
            if (i0 != int.MaxValue)
                info.WaterZones.Add(new Aabb(new Vector3(i0 * CellSize - 1, -1.4f, j0 * CellSize - 1), new Vector3((i1 - i0 + 1) * CellSize, 1.4f, (j1 - j0 + 1) * CellSize)));
        }
        // nav graph: one node per walkable (non-thin, non-crawl) cell, links where the shared edge heights match
        NodeAt = new int[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                int k = y * W + x; NodeAt[k] = -1;
                var c = G[k];
                if (!c.Open || c.Thin || c.Crawl) continue;
                NodeAt[k] = info.NavNodes.Count;
                info.NavNodes.Add(CellPos(x + OX, y + OY, c.Mean));
            }
        bool L(int i, int j, int di, int dj) => NodeAt[Idx(i, j)] >= 0 && OpenAt(i + di, j + dj) && NodeAt[Idx(i + di, j + dj)] >= 0 && Linked(i, j, di, dj);
        for (int j = OY; j < OY + H; j++)
            for (int i = OX; i < OX + W; i++)
            {
                if (NodeAt[Idx(i, j)] < 0) continue;
                int me = NodeAt[Idx(i, j)];
                if (L(i, j, 1, 0)) info.NavEdges.Add((me, NodeAt[Idx(i + 1, j)]));
                if (L(i, j, 0, 1)) info.NavEdges.Add((me, NodeAt[Idx(i, j + 1)]));
                if (L(i, j, 1, 0) && L(i, j, 0, 1) && L(i + 1, j, 0, 1) && L(i, j + 1, 1, 0)) info.NavEdges.Add((me, NodeAt[Idx(i + 1, j + 1)]));
                if (L(i, j, -1, 0) && L(i, j, 0, 1) && L(i - 1, j, 0, 1) && L(i, j + 1, -1, 0)) info.NavEdges.Add((me, NodeAt[Idx(i - 1, j + 1)]));
            }
    }
}

static class FloatExt { public static float Clamp(this float v, float min) => Math.Max(v, min); }
