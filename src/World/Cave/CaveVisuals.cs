using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>OmniLight with a cheap fire flicker.</summary>
public partial class FlickerLight : OmniLight3D
{
    public float BaseEnergy = 2f, Amount = 0.25f;
    float _t, _ph;
    public override void _Ready() { _ph = (Position.X * 12.9898f + Position.Z * 78.233f) % 100f; }
    public override void _Process(double delta)
    {
        _t += (float)delta;
        float f = Mathf.Sin(_t * 11f + _ph) * 0.5f + Mathf.Sin(_t * 23.7f + _ph * 2f) * 0.3f + Mathf.Sin(_t * 5.3f) * 0.2f;
        LightEnergy = BaseEnergy * (1f + f * Amount);
    }
}

/// <summary>Builds the irregular low-poly rock shell, props, glow meshes and lights of a ModularCave.</summary>
public static class CaveVisuals
{
    const float C = ModularCave.CellSize;
    static float[] _levels;

    static float Sn(float y) => MathF.Round(y * 100f) / 100f;
    static uint Hs(int a, int b, int c, int d)
    {
        unchecked
        {
            uint h = 2166136261; h = (h ^ (uint)a) * 16777619; h = (h ^ (uint)b) * 16777619; h = (h ^ (uint)c) * 16777619; h = (h ^ (uint)d) * 16777619;
            h ^= h >> 15; h *= 2246822519; h ^= h >> 13; return h;
        }
    }
    static float Jf(uint h) => (h & 0xffff) / 32767.5f - 1f;
    /// <summary>Deterministic vertex jitter keyed on the lattice point, so all faces sharing a vertex stay watertight.</summary>
    static Vector3 P(float x, float y, float z)
    {
        y = Sn(y);
        int ix = (int)MathF.Round(x * 2), iy = (int)MathF.Round(y * 100), iz = (int)MathF.Round(z * 2);
        return new Vector3(x + Jf(Hs(ix, iy, iz, 1)) * 0.26f, y + Jf(Hs(iz, iy, ix, 2)) * 0.13f, z + Jf(Hs(iy, ix, iz, 3)) * 0.26f);
    }

    static List<float> Span(float lo, float hi)
    {
        lo = Sn(lo); hi = Sn(hi);
        var l = new List<float> { lo };
        if (hi - lo < 0.005f) return l;
        foreach (var v in _levels) if (v > lo + 0.005f && v < hi - 0.005f) l.Add(v);
        l.Add(hi);
        return l;
    }

    static Color Rock(int depth, int kind, uint h, bool camp, float y)
    {
        float t = Mathf.Clamp(depth / 6f, 0, 1);
        var c = new Color(0.46f, 0.40f, 0.33f).Lerp(new Color(0.26f, 0.29f, 0.42f), t);
        if (camp) c = new Color(0.58f, 0.47f, 0.34f);
        float m = kind == 0 ? 0.8f : kind == 2 ? 0.62f : 1f;
        if (y < -2f) m *= 0.8f;
        float n = 0.8f + 0.4f * ((h & 255) / 255f);
        float tint = 0.94f + 0.12f * (((h >> 8) & 255) / 255f);
        return new Color(c.R * m * n, c.G * m * n * tint, c.B * m * n);
    }

    static void Wall(MeshBuilder mb, float x0, float z0, float lo0, float hi0, float x1, float z1, float lo1, float hi1, Vector3 hint, int depth, bool camp)
    {
        var y0 = Span(lo0, hi0); var y1 = Span(lo1, hi1);
        int i = 0, j = 0;
        while (i < y0.Count - 1 || j < y1.Count - 1)
        {
            Vector3 a, b, c;
            if (j >= y1.Count - 1 || (i < y0.Count - 1 && y0[i + 1] <= y1[j + 1]))
            { a = P(x0, y0[i], z0); b = P(x0, y0[i + 1], z0); c = P(x1, y1[j], z1); i++; }
            else
            { a = P(x1, y1[j], z1); b = P(x0, y0[i], z0); c = P(x1, y1[j + 1], z1); j++; }
            uint h = Hs((int)(a.X * 10), (int)(a.Y * 10), (int)(a.Z * 10), 9);
            mb.Tri(a, b, c, Rock(depth, 1, h, camp, a.Y), hint);
        }
    }

    class Ctx
    {
        public ModularCave G;
        public Dictionary<int, MeshBuilder> Rock = new();
        public Dictionary<string, MeshBuilder> Glow = new();
        public MeshBuilder Water = new();
        public List<Node3D> Lights = new();
        public MeshBuilder R(int owner) { if (!Rock.TryGetValue(owner, out var m)) Rock[owner] = m = new MeshBuilder(); return m; }
        public MeshBuilder Gl(string k) { if (!Glow.TryGetValue(k, out var m)) Glow[k] = m = new MeshBuilder(); return m; }
        public void Light(Vector3 pos, Color col, float energy, float range, bool flicker, float amt = 0.25f)
        {
            if (flicker) Lights.Add(new FlickerLight { Position = pos, LightColor = col, BaseEnergy = energy, LightEnergy = energy, OmniRange = range, Amount = amt, OmniAttenuation = 1.3f });
            else Lights.Add(new OmniLight3D { Position = pos, LightColor = col, LightEnergy = energy, OmniRange = range, OmniAttenuation = 1.3f });
        }
    }

    static readonly Color Wood = new(0.36f, 0.22f, 0.10f);
    static Color WoodC(Rnd r) => Wood * (0.8f + 0.4f * r.F());

    public static void Build(ModularCave g, Node3D parent)
    {
        var set = new SortedSet<int>();
        for (int v = -14; v <= 14; v += 2) set.Add(v * 100);
        foreach (var c in g.G) if (c.Open) { set.Add((int)MathF.Round(c.A * 100)); set.Add((int)MathF.Round(c.B * 100)); set.Add((int)MathF.Round(c.C * 100)); set.Add((int)MathF.Round(c.D * 100)); set.Add((int)MathF.Round(c.Ceil * 100)); }
        _levels = set.Select(v => v / 100f).ToArray();

        var cx = new Ctx { G = g };
        for (int j = g.OY; j < g.OY + g.H; j++)
            for (int i = g.OX; i < g.OX + g.W; i++)
                if (g.OpenAt(i, j)) RockCell(cx, i, j);
        Props(cx);

        var root = new Node3D { Name = "CaveVisual" };
        parent.AddChild(root);
        foreach (var kv in cx.Rock)
            root.AddChild(new MeshInstance3D { Name = "Rock" + kv.Key, Mesh = kv.Value.Build(), MaterialOverride = MeshBuilder.RockMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        foreach (var kv in cx.Glow)
        {
            var (col, em) = kv.Key switch
            {
                "fire" => (new Color(1f, 0.55f, 0.15f), 3.5f), "crystal" => (new Color(0.6f, 0.3f, 1f), 2.5f),
                "lantern" => (new Color(1f, 0.75f, 0.35f), 3f), "gold" => (new Color(1f, 0.8f, 0.2f), 1.0f), _ => (Colors.White, 1f),
            };
            root.AddChild(new MeshInstance3D { Name = "Glow_" + kv.Key, Mesh = kv.Value.Build(), MaterialOverride = Util.Mat(col, em), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        if (cx.Water.Count > 0)
        {
            var wm = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, AlbedoColor = new Color(0.05f, 0.22f, 0.38f, 0.72f), Roughness = 0.15f, Metallic = 0.2f,
                EmissionEnabled = true, Emission = new Color(0.02f, 0.08f, 0.16f), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
            root.AddChild(new MeshInstance3D { Name = "Water", Mesh = cx.Water.Build(), MaterialOverride = wm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        foreach (var l in cx.Lights) root.AddChild(l);
    }

    static void RockCell(Ctx cx, int i, int j)
    {
        var g = cx.G; var c = g.At(i, j);
        int depth = g.OwnerDepth.GetValueOrDefault(c.Owner); bool camp = c.Owner == 0;
        var mb = cx.R(c.Owner);
        float x0 = i * C - 1, x1 = i * C + 1, z0 = j * C - 1, z1 = j * C + 1;
        uint h = Hs(i, j, 5, 5);
        Vector3 pa = P(x0, c.A, z0), pb = P(x1, c.B, z0), pc = P(x0, c.C, z1), pd = P(x1, c.D, z1);
        var col = Rock(depth, 0, h, camp, c.Mean);
        if ((h & 1) == 0) { mb.Tri(pa, pb, pd, col, Vector3.Up); mb.Tri(pa, pd, pc, Rock(depth, 0, h >> 3, camp, c.Mean), Vector3.Up); }
        else { mb.Tri(pa, pb, pc, col, Vector3.Up); mb.Tri(pb, pd, pc, Rock(depth, 0, h >> 3, camp, c.Mean), Vector3.Up); }
        // ceiling
        float ce = c.Ceil;
        Vector3 qa = P(x0, ce, z0), qb = P(x1, ce, z0), qc = P(x0, ce, z1), qd = P(x1, ce, z1);
        var cc = Rock(depth, 2, h >> 5, camp, ce);
        if ((h & 2) == 0) { mb.Tri(qa, qb, qd, cc, Vector3.Down); mb.Tri(qa, qd, qc, Rock(depth, 2, h >> 7, camp, ce), Vector3.Down); }
        else { mb.Tri(qa, qb, qc, cc, Vector3.Down); mb.Tri(qb, qd, qc, Rock(depth, 2, h >> 7, camp, ce), Vector3.Down); }
        if (c.Water) AddWater(cx, x0, x1, z0, z1);

        // sides: N(z-) E(x+) S(z+) W(x-)
        for (int d = 0; d < 4; d++)
        {
            var (dx, dy) = ModularCave.Dirs[d];
            var n = g.At(i + dx, j + dy);
            float e0, e1, f0, f1, ax, az, bx, bz; Vector3 hint;
            switch (d)
            {
                case 0: e0 = c.A; e1 = c.B; f0 = n.C; f1 = n.D; ax = x0; az = z0; bx = x1; bz = z0; hint = Vector3.Back; break;
                case 1: e0 = c.B; e1 = c.D; f0 = n.A; f1 = n.C; ax = x1; az = z0; bx = x1; bz = z1; hint = Vector3.Left; break;
                case 2: e0 = c.C; e1 = c.D; f0 = n.A; f1 = n.B; ax = x0; az = z1; bx = x1; bz = z1; hint = Vector3.Forward; break;
                default: e0 = c.A; e1 = c.C; f0 = n.B; f1 = n.D; ax = x0; az = z0; bx = x0; bz = z1; hint = Vector3.Right; break;
            }
            if (!n.Open) { Wall(mb, ax, az, e0, ce, bx, bz, e1, ce, hint, depth, camp); continue; }
            if (f0 > e0 + 0.02f || f1 > e1 + 0.02f) Wall(mb, ax, az, e0, Math.Max(e0, f0), bx, bz, e1, Math.Max(e1, f1), hint, depth, camp);
            if (n.Ceil < ce - 0.02f) Wall(mb, ax, az, n.Ceil, ce, bx, bz, n.Ceil, ce, hint, depth, camp);
        }
    }

    static void AddWater(Ctx cx, float x0, float x1, float z0, float z1)
    {
        float y = -0.35f;
        cx.Water.Quad(new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Colors.White, Vector3.Up);
    }

    // ------------------------------------------------------------------ props
    static void Frame(Ctx cx, Rnd r, MeshBuilder mb, Vector3 center, bool alongX, float ceil)
    {
        // timber frame spanning the tunnel width (4 m); 'alongX' = tunnel axis
        float rot = alongX ? 0f : Mathf.Pi * 0.5f;
        var basis = new Basis(Vector3.Up, rot);
        float h = ceil - 0.15f;
        foreach (float s in new[] { -1.75f, 1.75f })
            mb.Box(center + basis * new Vector3(0, h * 0.5f, s), new Vector3(0.22f, h, 0.22f), WoodC(r));
        mb.Box(center + basis * new Vector3(0, h - 0.1f, 0), new Vector3(0.3f, 0.3f, 4f), WoodC(r));
        mb.Box(center + basis * new Vector3(0, h - 0.5f, -1.4f), new Vector3(0.18f, 0.18f, 1.0f), WoodC(r), 0.7f);
        mb.Box(center + basis * new Vector3(0, h - 0.5f, 1.4f), new Vector3(0.18f, 0.18f, 1.0f), WoodC(r), -0.7f);
    }

    static void Props(Ctx cx)
    {
        var g = cx.G;
        var r = new Rnd(g.Seed * 31L + g.Round * 17L + 5);
        // tunnels: frames, rails, lanterns
        foreach (var t in g.Tunnels)
        {
            var mb = cx.R(t.Owner);
            var axis = (t.AlongX ? t.Cells.Select(c => c.i) : t.Cells.Select(c => c.j)).Distinct().OrderBy(v => v).ToList();
            float lat = t.AlongX ? t.Z : t.X;
            foreach (int k in new[] { 0, axis.Count - 1 })
            {
                float a = axis[k] * C;
                Frame(cx, r, mb, t.AlongX ? new Vector3(a, 0, lat) : new Vector3(lat, 0, a), t.AlongX, 4.5f);
            }
            float a0 = axis[0] * C - 1, a1 = axis[^1] * C + 1, mid = (a0 + a1) * 0.5f;
            foreach (float off in new[] { -0.8f, 0.8f })
            {
                var size = t.AlongX ? new Vector3(a1 - a0, 0.1f, 0.1f) : new Vector3(0.1f, 0.1f, a1 - a0);
                mb.Box(t.AlongX ? new Vector3(mid, 0.14f, lat + off) : new Vector3(lat + off, 0.14f, mid), size, new Color(0.3f, 0.3f, 0.34f));
            }
            for (float a = a0 + 0.5f; a < a1; a += 1.0f)
                mb.Box(t.AlongX ? new Vector3(a, 0.08f, lat) : new Vector3(lat, 0.08f, a), t.AlongX ? new Vector3(0.18f, 0.1f, 2.1f) : new Vector3(2.1f, 0.1f, 0.18f), WoodC(r));
            var lp = t.AlongX ? new Vector3(mid, 3.5f, lat) : new Vector3(lat, 3.5f, mid);
            cx.Gl("lantern").Box(lp, new Vector3(0.25f, 0.35f, 0.25f), Colors.White);
            mb.Box(lp + new Vector3(0, 0.5f, 0), new Vector3(0.05f, 0.7f, 0.05f), Wood);
        }
        foreach (var s in g.Slots) RoomProps(cx, s, new Rnd(g.Seed * 131L + s.Id * 977L + g.Round));
        // rope bridges over deep gaps
        for (int j = g.OY; j < g.OY + g.H; j++)
            for (int i = g.OX; i < g.OX + g.W; i++)
            {
                var c = g.At(i, j);
                if (!c.Open || c.Ramp || c.Water) continue;
                bool nsGap = Deep(g, c, g.At(i, j - 1)) && Deep(g, c, g.At(i, j + 1)), ewGap = Deep(g, c, g.At(i - 1, j)) && Deep(g, c, g.At(i + 1, j));
                if (!nsGap && !ewGap) continue;
                var mb = cx.R(c.Owner); var ctr = new Vector3(i * C, c.A, j * C);
                for (int k = 0; k < 4; k++)
                {
                    float o = -0.75f + k * 0.5f;
                    if (ewGap) mb.Box(ctr + new Vector3(o, 0.15f, 0), new Vector3(0.4f, 0.1f, 1.9f), WoodC(r));
                    else mb.Box(ctr + new Vector3(0, 0.15f, o), new Vector3(1.9f, 0.1f, 0.4f), WoodC(r));
                }
                foreach (float s in new[] { -0.95f, 0.95f })
                {
                    if (ewGap) { mb.Box(ctr + new Vector3(0, 0.95f, s), new Vector3(2f, 0.06f, 0.06f), new Color(0.5f, 0.4f, 0.25f)); mb.Box(ctr + new Vector3(0.9f, 0.5f, s), new Vector3(0.1f, 1f, 0.1f), Wood); }
                    else { mb.Box(ctr + new Vector3(s, 0.95f, 0), new Vector3(0.06f, 0.06f, 2f), new Color(0.5f, 0.4f, 0.25f)); mb.Box(ctr + new Vector3(s, 0.5f, 0.9f), new Vector3(0.1f, 1f, 0.1f), Wood); }
                }
            }
    }
    static bool Deep(ModularCave g, Cell me, Cell n) => n.Open && n.Mean < me.Mean - 3f;

    static void RoomProps(Ctx cx, Slot s, Rnd r)
    {
        var g = cx.G; var mb = cx.R(s.Id);
        bool camp = s.Id == 0;
        var free = new List<(int i, int j, int wallDir)>();
        for (int row = 1; row < 13; row++)
            for (int col = 1; col < 13; col++)
            {
                if (col == 6 || col == 7 || row == 6 || row == 7) continue;
                int i = s.OX + col, j = s.OY + row; var c = g.At(i, j);
                if (!c.Open || c.Ramp || c.Water || c.Crawl || c.Thin || s.Map[row][col] != '.') continue;
                for (int d = 0; d < 4; d++)
                {
                    var (dx, dy) = ModularCave.Dirs[d];
                    if (!g.OpenAt(i + dx, j + dy)) { free.Add((i, j, d)); break; }
                }
            }
        r.Shuffle(free);
        int pi = 0;
        // stalagmites against the walls
        int nSt = camp ? 0 : 4 + r.I(4);
        for (int k = 0; k < nSt && pi < free.Count; k++, pi++)
        {
            var (i, j, d) = free[pi]; var (dx, dy) = ModularCave.Dirs[d];
            var p = new Vector3(i * C + dx * 0.55f + r.Range(-.3f, .3f), g.At(i, j).A, j * C + dy * 0.55f + r.Range(-.3f, .3f));
            float rad = r.Range(0.25f, 0.5f);
            mb.Cone(p + new Vector3(0, -0.1f, 0), rad, r.Range(0.8f, 2.0f), Rock(s.Depth, 1, (uint)r.Next(), false, 0), 5, r.F() * 3f);
        }
        // crates and barrels
        int nCr = camp ? 5 : (s.Depth >= 1 ? r.I(4) : 0);
        for (int k = 0; k < nCr && pi < free.Count; k++, pi++)
        {
            var (i, j, d) = free[pi]; var (dx, dy) = ModularCave.Dirs[d];
            var p = new Vector3(i * C + dx * 0.6f, g.At(i, j).A, j * C + dy * 0.6f);
            if (r.Chance(0.5f))
            {
                mb.Box(p + new Vector3(0, 0.45f, 0), new Vector3(0.9f, 0.9f, 0.9f), WoodC(r), r.F() * 1.5f);
                if (r.Chance(0.4f)) mb.Box(p + new Vector3(0.1f, 1.2f, 0.05f), new Vector3(0.6f, 0.6f, 0.6f), WoodC(r), r.F());
            }
            else mb.Cone(p, 0.42f, 1.0f, WoodC(r), 8, 0f, 0.38f);
        }
        // stalactites
        int nSc = 5 + r.I(5);
        for (int k = 0; k < nSc; k++)
        {
            int i = s.OX + 1 + r.I(12), j = s.OY + 1 + r.I(12); var c = g.At(i, j);
            if (!c.Open || c.Crawl || c.Ceil < 3f) continue;
            mb.Cone(new Vector3(i * C + r.Range(-.7f, .7f), c.Ceil + 0.1f, j * C + r.Range(-.7f, .7f)), r.Range(0.2f, 0.45f), -r.Range(0.7f, 1.9f), Rock(s.Depth, 2, (uint)r.Next(), false, 0), 5, r.F() * 3f);
        }
        Vector3 CellP(int col, int row) => new Vector3((s.OX + col) * C, 0, (s.OY + row) * C);
        if (camp)
        {
            var mid = new Vector3((s.OX + 6.5f) * C, 0, (s.OY + 6.5f) * C);
            for (int k = 0; k < 9; k++)
            {
                float a = k * Mathf.Tau / 9f;
                mb.Box(mid + new Vector3(Mathf.Cos(a) * 1.3f, 0.2f, Mathf.Sin(a) * 1.3f), new Vector3(0.5f, 0.4f, 0.5f), new Color(0.35f, 0.33f, 0.32f) * (0.8f + 0.4f * r.F()), a);
            }
            mb.Box(mid + new Vector3(0, 0.12f, 0), new Vector3(1.6f, 0.2f, 0.25f), new Color(0.2f, 0.12f, 0.07f), 0.5f);
            mb.Box(mid + new Vector3(0, 0.12f, 0), new Vector3(1.6f, 0.2f, 0.25f), new Color(0.2f, 0.12f, 0.07f), -0.5f);
            cx.Gl("fire").Cone(mid + new Vector3(0, 0.2f, 0), 0.5f, 1.0f, Colors.White, 5, 0.3f);
            cx.Gl("fire").Cone(mid + new Vector3(0.2f, 0.2f, 0.1f), 0.3f, 1.4f, Colors.White, 4, 0.9f);
            cx.Light(mid + new Vector3(0, 1.4f, 0), new Color(1f, 0.62f, 0.28f), 3.2f, 24f, true, 0.2f);
            foreach (var (col, row) in new[] { (3, 3), (10, 3), (3, 10), (10, 10) })
            {
                var p = CellP(col, row);
                mb.Box(p + new Vector3(0, 1.0f, 0), new Vector3(0.14f, 2f, 0.14f), WoodC(r));
                cx.Gl("fire").Cone(p + new Vector3(0, 2.0f, 0), 0.2f, 0.5f, Colors.White, 4);
                cx.Light(p + new Vector3(0, 2.5f, 0), new Color(1f, 0.6f, 0.25f), 1.8f, 13f, true, 0.3f);
            }
            // banner above the goblin
            var gp = CellP(4, 2);
            mb.Box(gp + new Vector3(0, 3.2f, -1.2f), new Vector3(3f, 1.4f, 0.08f), new Color(0.5f, 0.1f, 0.1f));
            // sacks / table
            mb.Box(CellP(5, 2) + new Vector3(0, 0.45f, 0.3f), new Vector3(1.6f, 0.9f, 0.8f), WoodC(r));
        }
        switch (s.Mod.Name)
        {
            case "shrine":
                for (int row = 0; row < 14; row++) for (int col = 0; col < 14; col++)
                    if (s.Map[row][col] == 'S')
                    {
                        var p = CellP(col, row);
                        mb.Box(p + new Vector3(0, 0.45f, 0), new Vector3(1.2f, 0.9f, 1.2f), new Color(0.4f, 0.4f, 0.45f), 0.4f);
                        for (int k = 0; k < 5; k++)
                        {
                            float a = k * Mathf.Tau / 5f;
                            cx.Gl("crystal").Cone(p + new Vector3(Mathf.Cos(a) * 2.2f, 0, Mathf.Sin(a) * 2.2f), 0.3f, r.Range(0.9f, 1.8f), Colors.White, 4, a);
                        }
                        cx.Light(p + new Vector3(0, 2f, 0), new Color(0.55f, 0.35f, 1f), 1.6f, 11f, true, 0.12f);
                    }
                break;
            case "lair":
                for (int row = 0; row < 14; row++) for (int col = 0; col < 14; col++)
                    if (s.Map[row][col] == 'D')
                    {
                        var p = CellP(col, row);
                        for (int k = 0; k < 9; k++)
                        {
                            float a = r.F() * Mathf.Tau, d = r.Range(1.5f, 5f);
                            cx.Gl("gold").Cone(p + new Vector3(Mathf.Cos(a) * d, -0.1f, Mathf.Sin(a) * d), r.Range(0.6f, 1.3f), r.Range(0.4f, 1.0f), Colors.White, 6, a);
                        }
                        cx.Light(p + new Vector3(0, 3f, 0), new Color(1f, 0.45f, 0.2f), 1.2f, 16f, true, 0.2f);
                    }
                break;
            case "flooded":
                cx.Light(CellP(6, 7) + new Vector3(0.9f, 2.5f, 0), new Color(0.3f, 0.55f, 1f), 0.5f, 9f, false);
                break;
            case "traps":
                for (int row = 0; row < 14; row++) for (int col = 0; col < 14; col++)
                    if (s.Map[row][col] == 'l') cx.Gl("lantern").Box(CellP(col, row) + new Vector3(0, 1.6f, 0), new Vector3(0.2f, 0.2f, 0.2f), Colors.White);
                break;
        }
    }
}
