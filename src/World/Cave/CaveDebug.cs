using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Cave tooling: `-- --cavecheck` headless self-check, and the --camroom/--cam debug camera used for screenshots.</summary>
[ContentModule]
public static class CaveTools
{
    public static void Register()
    {
        if (!Util.HasArg("cavecheck")) return;
        int fails = CaveCheck.Run();
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree != null) tree.ProcessFrame += () => tree.Quit(fails == 0 ? 0 : 1);
    }
}

public static class CaveCheck
{
    public static int Run()
    {
        int fails = 0, caves = 0, oilless = 0;
        var usage = new Dictionary<string, int>();
        void Bad(string m) { fails++; Util.Fail("cavecheck", m); }
        for (int seed = 1; seed <= 20; seed++)
            for (int round = 1; round <= 5; round++)
            {
                var g = ModularCave.Generate(seed, round);
                string tag = $"seed={seed} round={round}";
                caves++;
                foreach (var e in g.Errors) Bad($"{tag} {e}");
                foreach (var s in g.Slots) usage[s.Mod.Name] = usage.GetValueOrDefault(s.Mod.Name) + 1;
                var info = g.Info;
                int target = (int)(8 + (round - 1) * 2.5f + 0.5f);
                if (info.Rooms.Count != target) Bad($"{tag} rooms={info.Rooms.Count} want {target}");
                if (info.Rooms[0].Name != "camp") Bad($"{tag} camp missing");
                // Hall of the Scale seams
                if (info.WeighSpots.Count < 8) Bad($"{tag} weighspots={info.WeighSpots.Count}");
                foreach (var ws in info.WeighSpots) if (!info.HallZone.HasPoint(ws + Vector3.Up)) Bad($"{tag} weigh spot {ws} outside the Hall");
                foreach (var (nm, pt) in new[] { ("scale", info.ScalePos), ("palm", info.PalmPos), ("shaft", info.ShaftPos), ("goblin", info.GoblinPos) })
                    if (!info.HallZone.HasPoint(pt + Vector3.Up)) Bad($"{tag} {nm} {pt} outside the Hall");
                if (info.WeighSpots.Select(w => new Vector2(w.X, w.Z)).Distinct().Count() != info.WeighSpots.Count) Bad($"{tag} duplicate weigh spots");
                if (info.Rooms.Sum(r => r.Markers.Count(m => m.Kind == "oil")) == 0 && round >= 2) oilless++;
                if (info.MinerSpawns.Count < 8) Bad($"{tag} spawns={info.MinerSpawns.Count}");
                if (info.Rooms.Count >= 6 && g.LoopCount < 1) Bad($"{tag} no loop");
                if (round >= 3 && !info.Rooms.Any(r => r.Markers.Any(m => m.Kind == "dragon"))) Bad($"{tag} no dragon lair");
                if (round >= 3 && info.Rooms.First(r => r.Name == "lair").Depth != g.Slots.Max(s => s.Depth)) Bad($"{tag} lair not deepest");

                // grid-level connectivity (crawl + thin passable)
                var camp = g.Slots[0];
                int ci = camp.OX + 6, cj = camp.OY + 9;
                var seen = new bool[g.W * g.H];
                var q = new Queue<(int, int)>(); q.Enqueue((ci, cj)); seen[g.Idx(ci, cj)] = true;
                while (q.Count > 0)
                {
                    var (i, j) = q.Dequeue();
                    foreach (var (dx, dy) in ModularCave.Dirs)
                        if (g.Linked(i, j, dx, dy) && !seen[g.Idx(i + dx, j + dy)]) { seen[g.Idx(i + dx, j + dy)] = true; q.Enqueue((i + dx, j + dy)); }
                }
                int unreachable = 0;
                for (int j = g.OY; j < g.OY + g.H; j++) for (int i = g.OX; i < g.OX + g.W; i++) if (g.OpenAt(i, j) && !seen[g.Idx(i, j)]) unreachable++;
                if (unreachable > 0)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int j = g.OY; j < g.OY + g.H; j++) for (int i = g.OX; i < g.OX + g.W; i++) if (g.OpenAt(i, j) && !seen[g.Idx(i, j)] && sb.Length < 200) { var o = g.Slots.FirstOrDefault(sl => i >= sl.OX && i < sl.OX + 14 && j >= sl.OY && j < sl.OY + 14); sb.Append(o == null ? $"tunnel({i},{j}) " : $"{o.Mod.Name}/rot{o.Rot} r{j - o.OY}c{i - o.OX} "); }
                    Bad($"{tag} {unreachable} open cells unreachable from camp: {sb}");
                }

                // nav graph components
                var adj = info.Adjacency();
                int start = g.NodeAt[g.Idx(ci, cj)];
                var comp = new bool[info.NavNodes.Count];
                if (start < 0) { Bad($"{tag} camp has no nav node"); continue; }
                var nq = new Queue<int>(); nq.Enqueue(start); comp[start] = true;
                while (nq.Count > 0) { int u = nq.Dequeue(); foreach (int v in adj[u]) if (!comp[v]) { comp[v] = true; nq.Enqueue(v); } }
                int off = 0;
                for (int k = 0; k < comp.Length; k++) if (!comp[k] && g.RoomAt(info.NavNodes[k])?.Name != "nook") off++;
                if (off > 0) Bad($"{tag} {off} nav nodes not connected to camp");
                bool InMain(Vector3 p)
                {
                    int i = (int)MathF.Round(p.X / 2f), j = (int)MathF.Round(p.Z / 2f);
                    if (!g.OpenAt(i, j)) return false;
                    int n = g.NodeAt[g.Idx(i, j)];
                    return n >= 0 && comp[n];
                }
                foreach (var s in g.Slots)
                {
                    bool any = false;
                    for (int r = 0; r < 14 && !any; r++) for (int c = 0; c < 14; c++)
                    {
                        int n = g.OpenAt(s.OX + c, s.OY + r) ? g.NodeAt[g.Idx(s.OX + c, s.OY + r)] : -1;
                        if (n >= 0 && comp[n]) { any = true; break; }
                    }
                    if (!any) Bad($"{tag} room {s.Id} ({s.Mod.Name}) not in nav graph");
                }
                foreach (var room in info.Rooms)
                    foreach (var m in room.Markers)
                    {
                        int i = (int)MathF.Round(m.Pos.X / 2f), j = (int)MathF.Round(m.Pos.Z / 2f);
                        if (!g.OpenAt(i, j) || !seen[g.Idx(i, j)]) { Bad($"{tag} marker {m.Kind} in {room.Name} unreachable"); continue; }
                        var cell = g.At(i, j);
                        if (!cell.Crawl && !cell.Thin && room.Name != "nook" && !InMain(m.Pos)) Bad($"{tag} marker {m.Kind} in {room.Name} not in nav graph");
                        if (m.Kind == "vein")
                        {
                            bool access = false;
                            foreach (var (dx, dy) in ModularCave.Dirs) { int n2 = g.OpenAt(i + dx, j + dy) ? g.NodeAt[g.Idx(i + dx, j + dy)] : -1; if (n2 >= 0 && comp[n2]) access = true; }
                            if (!access) Bad($"{tag} vein sealed in {room.Name} at {m.Pos}");
                        }
                    }
                foreach (var sp in info.MinerSpawns) if (!InMain(sp)) Bad($"{tag} spawn {sp} not walkable");
                if (!InMain(info.GoblinPos) || !InMain(info.GoblinPos + new Vector3(0, 0, 2.3f))) Bad($"{tag} goblin spot blocked");
                // a path must exist from camp to every room centre marker
                foreach (var room in info.Rooms.Skip(1))
                {
                    var m = room.Markers.FirstOrDefault(x => x.Kind == "vein" || x.Kind == "ore" || x.Kind == "monster");
                    if (m == null) continue;
                    var path = info.FindPath(info.GoblinPos, m.Pos);
                    if (path.Count < 2) Bad($"{tag} no path to {room.Name}");
                }
            }
        foreach (var m in CaveModule.All) if (!usage.ContainsKey(m.Name)) Bad($"module {m.Name} never used in {caves} caves");
        Util.Log("CAVECHECK modules: " + string.Join(", ", usage.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}")));
        if (oilless > caves / 4) Bad($"{oilless} of {caves} caves have no oil drum marker");
        if (fails == 0) Util.Pass("cavecheck", $"caves={caves} oilless={oilless}");
        return fails;
    }
}

/// <summary>Debug camera with a lantern: --camroom=N [--camoff=x,y,z] [--camyaw=deg] [--campitch=deg] or --cam=x,y,z (world).</summary>
public partial class DebugCam : Camera3D
{
    public override void _Ready()
    {
        Fov = 80f; Near = 0.05f;
        AddChild(new OmniLight3D { LightColor = new Color(1f, 0.85f, 0.55f), LightEnergy = 2.2f, OmniRange = 14f, OmniAttenuation = 1.2f, Position = new Vector3(0.35f, 0.2f, -0.3f) });
    }
    public override void _Process(double delta) { if (!Current) MakeCurrent(); }

    static Vector3 V3(string s, Vector3 def)
    {
        var p = s.Split(',');
        if (p.Length != 3) return def;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        return new Vector3(float.Parse(p[0], ci), float.Parse(p[1], ci), float.Parse(p[2], ci));
    }

    public static DebugCam Make(CaveInfo cave)
    {
        Vector3 pos;
        if (Util.HasArg("cam")) pos = V3(Util.Arg("cam"), Vector3.Zero);
        else
        {
            int n = int.TryParse(Util.Arg("camroom", "0"), out var nn) ? nn : 0;
            n = Mathf.Clamp(n, 0, cave.Rooms.Count - 1);
            pos = cave.Rooms[n].Center + new Vector3(0, 1.6f, 0) + V3(Util.Arg("camoff", "0,0,0"), Vector3.Zero);
        }
        float yaw = float.TryParse(Util.Arg("camyaw", "0"), out var y) ? y : 0, pitch = float.TryParse(Util.Arg("campitch", "0"), out var p) ? p : 0;
        if (Util.HasArg("camat") && !Util.HasArg("cam"))
        {
            int n = int.TryParse(Util.Arg("camroom", "0"), out var nn) ? nn : 0;
            var mk = cave.Rooms[Mathf.Clamp(n, 0, cave.Rooms.Count - 1)].Markers.FirstOrDefault(m => m.Kind == Util.Arg("camat"));
            if (mk != null)
            {
                var d = (mk.Pos - pos) with { Y = 0 };
                yaw = Mathf.RadToDeg(Mathf.Atan2(-d.X, -d.Z));
                float back = float.TryParse(Util.Arg("camback", "5"), out var bk) ? bk : 5f;
                pos = mk.Pos - d.Normalized() * (d.Length() < 0.1f ? 0f : Mathf.Min(back, d.Length() + back)) + new Vector3(0, 1.6f, 0);
            }
        }
        var cam = new DebugCam { Name = "DebugCam", Position = pos, Rotation = new Vector3(Mathf.DegToRad(pitch), Mathf.DegToRad(yaw), 0) };
        return cam;
    }
}
