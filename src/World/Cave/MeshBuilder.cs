using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Accumulates flat-shaded, vertex-coloured triangles and bakes them into one ArrayMesh (one draw call per builder).</summary>
public class MeshBuilder
{
    readonly List<Vector3> _v = new(), _n = new();
    readonly List<Color> _c = new();
    public int Count => _v.Count;

    public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 hint)
    {
        var n = (b - a).Cross(c - a);
        if (n.LengthSquared() < 1e-9f) return;
        n = n.Normalized();
        // Godot front faces are clockwise: make the geometric (CCW) normal point away from the visible side, store the visible-side normal
        if (n.Dot(hint) > 0) (b, c) = (c, b); else n = -n;
        _v.Add(a); _v.Add(b); _v.Add(c);
        _n.Add(n); _n.Add(n); _n.Add(n);
        _c.Add(col); _c.Add(col); _c.Add(col);
    }

    /// <summary>Quad a-b-c-d (convex, in order); hint = direction the visible side faces.</summary>
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector3 hint, bool flip = false)
    {
        if (flip) { Tri(b, c, d, col, hint); Tri(b, d, a, col, hint); }
        else { Tri(a, b, c, col, hint); Tri(a, c, d, col, hint); }
    }

    public void Box(Vector3 center, Vector3 size, Color col, float rotY = 0f)
    {
        var basis = new Basis(Vector3.Up, rotY);
        var h = size * 0.5f;
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++) p[i] = center + basis * new Vector3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z);
        void F(int a, int b, int c, int d) { var mid = (p[a] + p[b] + p[c] + p[d]) * 0.25f - center; Quad(p[a], p[b], p[c], p[d], col, mid); }
        F(0, 1, 3, 2); F(4, 5, 7, 6); F(0, 1, 5, 4); F(2, 3, 7, 6); F(0, 2, 6, 4); F(1, 3, 7, 5);
    }

    /// <summary>Cone/frustum with n sides standing on baseCenter; negative height hangs downwards.</summary>
    public void Cone(Vector3 baseCenter, float radius, float height, Color col, int sides = 5, float rot = 0f, float topRadius = 0f)
    {
        var apex = baseCenter + new Vector3(0, height, 0);
        float sgn = height < 0 ? -1f : 1f;
        for (int i = 0; i < sides; i++)
        {
            float a0 = rot + i * Mathf.Tau / sides, a1 = rot + (i + 1) * Mathf.Tau / sides;
            var b0 = baseCenter + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
            var b1 = baseCenter + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
            var mid = (b0 + b1) * 0.5f - baseCenter; mid.Y = radius * 0.4f * sgn;
            if (topRadius <= 0f) Tri(b0, b1, apex, col, mid);
            else
            {
                var t0 = apex + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * topRadius;
                var t1 = apex + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * topRadius;
                Quad(b0, b1, t1, t0, col, mid);
            }
        }
        if (topRadius > 0f)
            for (int i = 1; i < sides - 1; i++)
            {
                float a1 = rot + i * Mathf.Tau / sides, a2 = rot + (i + 1) * Mathf.Tau / sides;
                Tri(apex + new Vector3(Mathf.Cos(rot), 0, Mathf.Sin(rot)) * topRadius, apex + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * topRadius,
                    apex + new Vector3(Mathf.Cos(a2), 0, Mathf.Sin(a2)) * topRadius, col, Vector3.Up * sgn);
            }
    }

    public ArrayMesh Build()
    {
        var mesh = new ArrayMesh();
        if (_v.Count == 0) return mesh;
        var arr = new Godot.Collections.Array();
        arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = _v.ToArray();
        arr[(int)Mesh.ArrayType.Normal] = _n.ToArray();
        arr[(int)Mesh.ArrayType.Color] = _c.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
        return mesh;
    }

    static StandardMaterial3D _rock;
    public static StandardMaterial3D RockMaterial => _rock ??= new StandardMaterial3D
    {
        VertexColorUseAsAlbedo = true, Roughness = 1f, Metallic = 0f, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };
}

/// <summary>Tiny deterministic RNG (identical on every peer and platform).</summary>
public class Rnd
{
    ulong _s;
    public Rnd(long seed) { _s = (ulong)seed * 6364136223846793005UL + 1442695040888963407UL; if (_s == 0) _s = 88172645463325252UL; for (int i = 0; i < 4; i++) Next(); }
    public uint Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; return (uint)(_s >> 16); }
    public float F() => (Next() & 0xffffff) / (float)0x1000000;
    public float Range(float a, float b) => a + (b - a) * F();
    public int I(int n) => n <= 1 ? 0 : (int)(Next() % (uint)n);
    public bool Chance(float p) => F() < p;
    public void Shuffle<T>(IList<T> l) { for (int i = l.Count - 1; i > 0; i--) { int j = I(i + 1); (l[i], l[j]) = (l[j], l[i]); } }
}
