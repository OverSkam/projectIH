using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Turns a generated ModularCave grid into nodes: clean box/ramp collision here, the low-poly look in CaveVisuals.</summary>
public static class CaveBuilder
{
    const int None = int.MinValue;

    public static void Emit(ModularCave g, Node3D parent)
    {
        var body = new StaticBody3D { Name = "CaveBody", CollisionLayer = Layers.World, CollisionMask = 0 };
        parent.AddChild(body);
        BuildCollision(g, body);
        BuildThinWalls(g, parent);
        CaveVisuals.Build(g, parent);
    }

    static void Box(StaticBody3D body, Vector3 center, Vector3 size, Basis? basis = null)
    {
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = new Transform3D(basis ?? Basis.Identity, center) });
    }

    static void Merge(ModularCave g, Func<int, int, int> key, Action<int, int, int, int, int> emit)
    {
        var used = new bool[g.W * g.H];
        for (int j = g.OY; j < g.OY + g.H; j++)
            for (int i = g.OX; i < g.OX + g.W; i++)
            {
                int k = key(i, j);
                if (k == None || used[g.Idx(i, j)]) continue;
                int w = 1;
                while (i + w < g.OX + g.W && !used[g.Idx(i + w, j)] && key(i + w, j) == k) w++;
                int h = 1; bool ok = true;
                while (ok && j + h < g.OY + g.H)
                {
                    for (int x = 0; x < w; x++) if (used[g.Idx(i + x, j + h)] || key(i + x, j + h) != k) { ok = false; break; }
                    if (ok) h++;
                }
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) used[g.Idx(i + x, j + y)] = true;
                emit(i, j, w, h, k);
            }
    }

    static void BuildCollision(ModularCave g, StaticBody3D body)
    {
        const float C = ModularCave.CellSize, Eps = 0.04f;
        Vector3 Ctr(int i, int j, int w, int h) => new Vector3((i + (w - 1) * 0.5f) * C, 0, (j + (h - 1) * 0.5f) * C);
        // floors: columns from deep below up to the (lowest) floor height of the cell; ramps add their own boxes
        Merge(g, (i, j) => { var c = g.At(i, j); return c.Open ? (int)Math.Round(c.Min * 100) : None; }, (i, j, w, h, k) =>
        {
            float top = k / 100f; var c = Ctr(i, j, w, h);
            Box(body, c with { Y = (top - 30f) * 0.5f }, new Vector3(w * C + Eps, top + 30f, h * C + Eps));
        });
        // rock next to open space
        Merge(g, (i, j) =>
        {
            if (g.OpenAt(i, j)) return None;
            for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) if (g.OpenAt(i + di, j + dj)) return 0;
            return None;
        }, (i, j, w, h, k) => Box(body, Ctr(i, j, w, h) with { Y = -7f }, new Vector3(w * C + Eps, 46f, h * C + Eps)));
        // ceilings
        Merge(g, (i, j) => { var c = g.At(i, j); return c.Open ? (int)Math.Round(c.Ceil * 100) : None; }, (i, j, w, h, k) =>
        {
            float y = k / 100f; Box(body, Ctr(i, j, w, h) with { Y = y + 1f }, new Vector3(w * C + Eps, 2f, h * C + Eps));
        });
        // ramps
        foreach (var r in g.Ramps)
        {
            var d = new Vector3(r.X1 - r.X0, 0, r.Z1 - r.Z0); float plan = d.Length(); var dp = d / plan;
            float dh = r.H1 - r.H0, len = Mathf.Sqrt(plan * plan + dh * dh);
            var f = new Vector3(dp.X * plan, dh, dp.Z * plan) / len;
            var n = new Vector3(-dp.X * dh / len, plan / len, -dp.Z * dh / len);
            var basis = new Basis(f, n, f.Cross(n));
            var mid = new Vector3((r.X0 + r.X1) * 0.5f, (r.H0 + r.H1) * 0.5f, (r.Z0 + r.Z1) * 0.5f);
            Box(body, mid - n * 0.3f, new Vector3(len + 0.3f, 0.6f, C + Eps), basis);
        }
    }

    static ShaderMaterial _thinMat;
    static ShaderMaterial ThinMat()
    {
        if (_thinMat != null) return _thinMat;
        var sh = new Shader
        {
            Code = @"shader_type spatial;
render_mode blend_mix, cull_disabled, specular_disabled, depth_draw_never;
void fragment(){
  float w = 0.5 + 0.5 * sin(TIME * 1.7 + UV.y * 14.0 + UV.x * 6.0);
  float w2 = 0.5 + 0.5 * sin(TIME * 2.9 - UV.y * 9.0 + UV.x * 11.0);
  ALBEDO = vec3(0.35, 0.45, 0.75);
  EMISSION = vec3(0.30, 0.45, 1.0) * (0.35 + 0.9 * w * w2);
  ALPHA = 0.28 + 0.3 * w;
}",
        };
        return _thinMat = new ShaderMaterial { Shader = sh };
    }

    /// <summary>Ethereal-only walls: static bodies on Layers.ThinWall (shimmering panel as visual). One panel per cell.</summary>
    static void BuildThinWalls(ModularCave g, Node3D parent)
    {
        for (int j = g.OY; j < g.OY + g.H; j++)
            for (int i = g.OX; i < g.OX + g.W; i++)
            {
                var c = g.At(i, j);
                if (!c.Thin) continue;
                bool spanX = g.OpenAt(i, j - 1) && g.OpenAt(i, j + 1);   // corridor runs north-south -> wall spans x
                float height = c.Ceil - c.Mean;
                var size = spanX ? new Vector3(2f, height, 0.4f) : new Vector3(0.4f, height, 2f);
                var pos = new Vector3(i * 2f, c.Mean + height * 0.5f, j * 2f);
                var b = new StaticBody3D { Name = "ThinWall", CollisionLayer = Layers.ThinWall, CollisionMask = 0, Position = pos };
                b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
                b.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size * new Vector3(1f, 1f, 1f) - (spanX ? new Vector3(0, 0, 0.1f) : new Vector3(0.1f, 0, 0)) }, MaterialOverride = ThinMat() });
                parent.AddChild(b);
            }
    }
}
