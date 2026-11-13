using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Grid-based grey-box cave: lit camp hub, 4 dark rooms, tunnels, one pit with a ramp.</summary>
public class GreyBoxCave : ICaveBuilder
{
    const float Cell = 4f, H = 5f, PitDepth = 5.5f;
    readonly HashSet<Vector2I> _open = new();
    readonly HashSet<Vector2I> _pit = new();
    StaticBody3D _body;
    Node3D _parent;

    void Rect(int x0, int z0, int x1, int z1, HashSet<Vector2I> set = null)
    {
        for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++) { _open.Add(new Vector2I(x, z)); set?.Add(new Vector2I(x, z)); }
    }

    static Vector3 CellCenter(Vector2I c) => new Vector3(c.X * Cell, 0, c.Y * Cell);

    void Solid(Vector3 center, Vector3 size, Color col, Basis? basis = null)
    {
        var t = basis ?? Basis.Identity;
        var mi = Util.BoxMesh(size, col);
        mi.Transform = new Transform3D(t, center);
        _parent.AddChild(mi);
        var cs = new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = new Transform3D(t, center) };
        _body.AddChild(cs);
    }

    public CaveInfo Build(Node3D parent, int seed, int round, int miners = 2)
    {
        _parent = parent;
        _body = new StaticBody3D { Name = "CaveBody", CollisionLayer = 1, CollisionMask = 0 };
        parent.AddChild(_body);

        var info = new CaveInfo();
        var camp = new HashSet<Vector2I>();
        Rect(-2, -2, 2, 2, camp);
        // tunnels
        Rect(3, 0, 5, 0); Rect(-5, 0, -3, 0); Rect(0, -5, 0, -3); Rect(8, 3, 8, 5);
        // rooms: A east, B west (has pit), C north, D south of A
        var roomCells = new (int id, int cx, int cz, int depth)[] { (0, 8, 0, 1), (1, -8, 0, 1), (2, 0, -8, 1), (3, 8, 8, 2) };
        foreach (var r in roomCells) Rect(r.cx - 2, r.cz - 2, r.cx + 2, r.cz + 2);
        // pit in room B: cells (-9..-8, 1..2)
        Rect(-9, 1, -8, 2, _pit);

        var floorCol = new Color(0.22f, 0.2f, 0.19f);
        var wallCol = new Color(0.3f, 0.28f, 0.27f);
        var campFloor = new Color(0.35f, 0.3f, 0.22f);

        // floors (merged runs per row), skipping pit cells
        var zs = new SortedSet<int>(); foreach (var c in _open) zs.Add(c.Y);
        foreach (int z in zs)
        {
            var xs = new List<int>();
            foreach (var c in _open) if (c.Y == z && !_pit.Contains(c)) xs.Add(c.X);
            xs.Sort();
            foreach (var (a, b) in Runs(xs))
                Solid(new Vector3((a + b) * 0.5f * Cell, -0.25f, z * Cell), new Vector3((b - a + 1) * Cell, 0.5f, Cell), floorCol);
            xs.Clear();
            foreach (var c in _open) if (c.Y == z) xs.Add(c.X);
            xs.Sort();
            foreach (var (a, b) in Runs(xs))
            {
                // ceiling
                var mi = Util.BoxMesh(new Vector3((b - a + 1) * Cell, 0.5f, Cell), wallCol);
                mi.Position = new Vector3((a + b) * 0.5f * Cell, H + 0.25f, z * Cell);
                _parent.AddChild(mi);
                _body.AddChild(Util.BoxShape(new Vector3((b - a + 1) * Cell, 0.5f, Cell), mi.Position));
            }
        }
        // camp floor tint
        var cf = Util.BoxMesh(new Vector3(5 * Cell, 0.05f, 5 * Cell), campFloor);
        cf.Position = new Vector3(0, 0.01f, 0);
        _parent.AddChild(cf);

        // walls: N(-z) S(+z) edges grouped along x; W(-x) E(+x) along z
        var dirs = new[] { new Vector2I(0, -1), new Vector2I(0, 1), new Vector2I(-1, 0), new Vector2I(1, 0) };
        foreach (var d in dirs)
        {
            bool alongX = d.X == 0;
            var groups = new SortedDictionary<int, List<int>>();
            foreach (var c in _open)
            {
                if (_open.Contains(c + d)) continue;
                int key = alongX ? c.Y : c.X; int val = alongX ? c.X : c.Y;
                if (!groups.TryGetValue(key, out var l)) groups[key] = l = new List<int>();
                l.Add(val);
            }
            foreach (var kv in groups)
            {
                kv.Value.Sort();
                foreach (var (a, b) in Runs(kv.Value))
                {
                    float mid = (a + b) * 0.5f * Cell, len = (b - a + 1) * Cell + 0.5f;
                    float fixedPos = kv.Key * Cell + (alongX ? d.Y : d.X) * (Cell * 0.5f + 0.25f);
                    Vector3 size = alongX ? new Vector3(len, H + 0.5f, 0.5f) : new Vector3(0.5f, H + 0.5f, len);
                    Vector3 pos = alongX ? new Vector3(mid, H * 0.5f, fixedPos) : new Vector3(fixedPos, H * 0.5f, mid);
                    Solid(pos, size, wallCol);
                }
            }
        }

        // pit geometry
        float px0 = -9 * Cell - Cell * 0.5f, px1 = -8 * Cell + Cell * 0.5f, pz0 = 1 * Cell - Cell * 0.5f, pz1 = 2 * Cell + Cell * 0.5f;
        Solid(new Vector3((px0 + px1) / 2, -PitDepth - 0.25f, (pz0 + pz1) / 2), new Vector3(px1 - px0, 0.5f, pz1 - pz0), floorCol);
        float wallH = PitDepth + 0.5f, wallY = -(PitDepth + 0.5f) / 2f + 0.0f;
        float cx = (px0 + px1) / 2, cz = (pz0 + pz1) / 2;
        // north side (z = pz0) and south (pz1) walls, west (px0) wall; east side open: ramp meets floor
        Solid(new Vector3(cx, wallY, pz0 + 0.25f), new Vector3(px1 - px0, wallH, 0.5f), wallCol);
        Solid(new Vector3(cx, wallY, pz1 - 0.25f), new Vector3(px1 - px0, wallH, 0.5f), wallCol);
        Solid(new Vector3(px0 + 0.25f, wallY, cz), new Vector3(0.5f, wallH, pz1 - pz0), wallCol);
        // east wall pieces beside the ramp (ramp is 3m wide, centred)
        float rampHalf = 1.5f;
        float sideLen = (pz1 - pz0) / 2f - rampHalf;
        Solid(new Vector3(px1 - 0.25f, wallY, pz0 + sideLen / 2f), new Vector3(0.5f, wallH, sideLen), wallCol);
        Solid(new Vector3(px1 - 0.25f, wallY, pz1 - sideLen / 2f), new Vector3(0.5f, wallH, sideLen), wallCol);
        // closes the gap under the ramp so nobody walks out beneath the floor
        Solid(new Vector3(px1 - 0.25f, -0.75f - 2.625f, cz), new Vector3(0.5f, 5.25f, rampHalf * 2f), wallCol);
        // ramp rising towards +x
        // surface emerges from the pit floor 1m in front of the west wall and meets the floor edge at px1
        float xe = px0 + 1.5f;
        float dx = px1 - xe; float ang = Mathf.Atan2(PitDepth, dx);
        var dirv = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0);
        var normal = new Vector3(-Mathf.Sin(ang), Mathf.Cos(ang), 0);
        var pStart = new Vector3(xe, -PitDepth, cz) - dirv * 1.0f;   // buried 1m in the pit floor
        var pEnd = new Vector3(px1, 0, cz);
        float len2 = pStart.DistanceTo(pEnd);
        Solid((pStart + pEnd) * 0.5f - normal * 0.2f, new Vector3(len2, 0.4f, 3f), new Color(0.4f, 0.32f, 0.2f), new Basis(Vector3.Back, ang));

        // metadata
        info.HallZone = new Aabb(new Vector3(-10, -1, -10), new Vector3(20, H + 3, 20));
        // legacy test cave: same seams as the Hall of the Scale, squeezed into the 20 m hall (the giant pokes through its walls; rules and positions still work)
        info.GoblinSeat = new Vector3(0, 0, -7.5f);
        info.GoblinPos = info.GoblinSeat + new Vector3(-4.2f, 0, 4.4f);
        info.PalmPos = info.GoblinPos + new Vector3(0, 1.4f, 0);
        info.ScalePos = new Vector3(0, 0, -2.5f);
        info.ShaftPos = new Vector3(7.5f, 0, -2f);
        info.CagePos = new Vector3(-8f, 0, 7.5f);
        info.MakeWeighSpots(8, 6.5f, 1.0f);
        for (int i = 0; i < 8; i++) info.MinerSpawns.Add(new Vector3(-7 + 2 * i, 0.3f, 6.5f));
        var markerOffs = new (string k, float x, float z)[]
        {
            ("vein", -6, -6), ("vein", 7, -3), ("vein", 6, 6),
            ("ore", -3, -7), ("ore", 7, 2),
            ("monster", 4, 3), ("trap", 0, -3), ("loot", -8, -8), ("oil", -3, 4),
        };
        foreach (var r in roomCells)
        {
            var c = new Vector3(r.cx * Cell, 0, r.cz * Cell);
            var ri = new RoomInfo { Id = r.id, Center = c, Depth = r.depth, Bounds = new Aabb(c + new Vector3(-10, -1, -10), new Vector3(20, H + 2, 20)) };
            foreach (var m in markerOffs) ri.Markers.Add(new SpawnMarker(m.k, c + new Vector3(m.x, 0, m.z)));
            info.Rooms.Add(ri);
        }
        // nav graph (axis-aligned corridors from camp centre)
        int N(Vector3 p) { info.NavNodes.Add(p); return info.NavNodes.Count - 1; }
        void E(int a, int b) => info.NavEdges.Add((a, b));
        int camp0 = N(Vector3.Zero);
        int e1 = N(new Vector3(16, 0, 0)), ra = N(new Vector3(32, 0, 0)), dt = N(new Vector3(32, 0, 16)), rd = N(new Vector3(32, 0, 32));
        int w1 = N(new Vector3(-16, 0, 0)), rb = N(new Vector3(-32, 0, 0));
        int n1 = N(new Vector3(0, 0, -16)), rc = N(new Vector3(0, 0, -32));
        E(camp0, e1); E(e1, ra); E(ra, dt); E(dt, rd); E(camp0, w1); E(w1, rb); E(camp0, n1); E(n1, rc);

        BuildCampDecor(info);
        return info;
    }

    void BuildCampDecor(CaveInfo info)
    {
        var warm = new Color(1f, 0.7f, 0.35f);
        foreach (var p in new[] { new Vector3(0, 4.3f, 0), new Vector3(-7, 4f, -7), new Vector3(7, 4f, -7), new Vector3(-7, 4f, 7), new Vector3(7, 4f, 7) })
        {
            _parent.AddChild(new OmniLight3D { Position = p, LightColor = warm, LightEnergy = 1.6f, OmniRange = 16f });
            var lamp = Util.BoxMesh(new Vector3(0.3f, 0.3f, 0.3f), warm, 3f);
            lamp.Position = p + new Vector3(0, 0.4f, 0);
            _parent.AddChild(lamp);
        }
        // camp banner above spawn row
        var sign = Util.BoxMesh(new Vector3(6, 1, 0.2f), new Color(0.5f, 0.1f, 0.1f));
        sign.Position = new Vector3(0, 3.5f, -9.6f);
        _parent.AddChild(sign);
    }

    static IEnumerable<(int a, int b)> Runs(List<int> sorted)
    {
        int i = 0;
        while (i < sorted.Count)
        {
            int j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
            yield return (sorted[i], sorted[j]);
            i = j + 1;
        }
    }
}
