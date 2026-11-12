using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>
/// Small flat-shaded modelling kit for the Goblin (and other hand-built props): ellipsoids, tubes (tapered, elliptical), rotated boxes and quads,
/// all baked into one vertex-coloured ArrayMesh. A transform stack lets a part be built in its own space. Every triangle gets a tiny colour variation
/// so big flat surfaces read as hand-faceted low poly instead of plastic.
/// </summary>
public class GMesh
{
    readonly MeshBuilder _mb = new();
    Transform3D _x = Transform3D.Identity;
    readonly Stack<Transform3D> _st = new();
    int _n;
    public float Variation = 0.07f;

    public void Push(Vector3 pos, Vector3? euler = null)
    {
        _st.Push(_x);
        var b = euler.HasValue ? Basis.FromEuler(euler.Value) : Basis.Identity;
        _x = _x * new Transform3D(b, pos);
    }
    public void Push(Transform3D t) { _st.Push(_x); _x = _x * t; }
    public void Pop() { _x = _st.Pop(); }

    Vector3 W(Vector3 p) => _x * p;
    Vector3 D(Vector3 d) => _x.Basis * d;
    Color V(Color c)
    {
        _n++;
        uint h = unchecked((uint)_n * 2654435761u); h ^= h >> 15; h = unchecked(h * 2246822519u); h ^= h >> 13;
        float k = 1f + Variation * (((h & 255) / 127.5f) - 1f);
        return new Color(Mathf.Clamp(c.R * k, 0, 1), Mathf.Clamp(c.G * k, 0, 1), Mathf.Clamp(c.B * k, 0, 1), c.A);
    }

    public void Tri(Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 hint) => _mb.Tri(W(a), W(b), W(c), V(col), D(hint));
    public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector3 hint) { Tri(a, b, c, col, hint); Tri(a, c, d, col, hint); }

    /// <summary>Faceted ellipsoid (radii r) around c. thetaFrom/To (0..pi, pole to pole along +Y) allow domes.</summary>
    public void Ellipsoid(Vector3 c, Vector3 r, Color col, int seg = 10, int rings = 6, float thetaFrom = 0f, float thetaTo = Mathf.Pi)
    {
        for (int i = 0; i < rings; i++)
        {
            float t0 = Mathf.Lerp(thetaFrom, thetaTo, (float)i / rings), t1 = Mathf.Lerp(thetaFrom, thetaTo, (float)(i + 1) / rings);
            for (int j = 0; j < seg; j++)
            {
                float f0 = Mathf.Tau * j / seg, f1 = Mathf.Tau * (j + 1) / seg;
                Vector3 P(float t, float f) => c + new Vector3(r.X * Mathf.Sin(t) * Mathf.Cos(f), r.Y * Mathf.Cos(t), r.Z * Mathf.Sin(t) * Mathf.Sin(f));
                var p00 = P(t0, f0); var p01 = P(t0, f1); var p11 = P(t1, f1); var p10 = P(t1, f0);
                var mid = (p00 + p01 + p11 + p10) * 0.25f - c;
                var hint = new Vector3(mid.X / (r.X * r.X), mid.Y / (r.Y * r.Y), mid.Z / (r.Z * r.Z));
                Quad(p00, p01, p11, p10, col, hint);
            }
        }
    }

    public void Ellipsoid(Vector3 c, Vector3 r, Vector3 euler, Color col, int seg = 10, int rings = 6)
    {
        Push(c, euler); Ellipsoid(Vector3.Zero, r, col, seg, rings); Pop();
    }

    /// <summary>Tapered tube from a to b (radius ra -> rb), elliptical cross section (squash scales the second axis), optional end caps.</summary>
    public void Tube(Vector3 a, Vector3 b, float ra, float rb, Color col, int sides = 8, float squash = 1f, bool capA = true, bool capB = true, Vector3? side = null)
    {
        var z = (b - a); float len = z.Length(); if (len < 1e-4f) return; z /= len;
        var up = side ?? (Mathf.Abs(z.Y) > 0.9f ? Vector3.Right : Vector3.Up);
        var x = z.Cross(up).Normalized(); var y = z.Cross(x).Normalized();
        Vector3 Ring(Vector3 o, float rad, int k) { float f = Mathf.Tau * k / sides; return o + x * (Mathf.Cos(f) * rad) + y * (Mathf.Sin(f) * rad * squash); }
        for (int k = 0; k < sides; k++)
        {
            var a0 = Ring(a, ra, k); var a1 = Ring(a, ra, k + 1); var b0 = Ring(b, rb, k); var b1 = Ring(b, rb, k + 1);
            var mid = (a0 + a1 + b0 + b1) * 0.25f - (a + b) * 0.5f;
            Quad(a0, a1, b1, b0, col, mid);
        }
        if (capA) for (int k = 1; k < sides - 1; k++) Tri(Ring(a, ra, 0), Ring(a, ra, k), Ring(a, ra, k + 1), col, -z);
        if (capB) for (int k = 1; k < sides - 1; k++) Tri(Ring(b, rb, 0), Ring(b, rb, k), Ring(b, rb, k + 1), col, z);
    }

    /// <summary>Box with centre c, size, and an Euler rotation (radians).</summary>
    public void Box(Vector3 c, Vector3 size, Color col, Vector3? euler = null)
    {
        Push(c, euler);
        var h = size * 0.5f;
        var p = new Vector3[8];
        for (int i = 0; i < 8; i++) p[i] = new Vector3((i & 1) == 0 ? -h.X : h.X, (i & 2) == 0 ? -h.Y : h.Y, (i & 4) == 0 ? -h.Z : h.Z);
        void F(int a, int b, int cc, int d) { var mid = (p[a] + p[b] + p[cc] + p[d]) * 0.25f; Quad(p[a], p[b], p[cc], p[d], col, mid); }
        F(0, 1, 3, 2); F(4, 5, 7, 6); F(0, 1, 5, 4); F(2, 3, 7, 6); F(0, 2, 6, 4); F(1, 3, 7, 5);
        Pop();
    }

    /// <summary>Wedge: a box whose top face is pinched to a ridge (triangular prism), length along Z, width X, height Y.</summary>
    public void Prism(Vector3 c, Vector3 size, Color col, Vector3? euler = null)
    {
        Push(c, euler);
        var h = size * 0.5f;
        Vector3 a0 = new(-h.X, -h.Y, -h.Z), a1 = new(h.X, -h.Y, -h.Z), a2 = new(0, h.Y, -h.Z), b0 = new(-h.X, -h.Y, h.Z), b1 = new(h.X, -h.Y, h.Z), b2 = new(0, h.Y, h.Z);
        Tri(a0, a1, a2, col, Vector3.Back * -1f); Tri(b0, b1, b2, col, Vector3.Back);
        Quad(a0, a1, b1, b0, col, Vector3.Down);
        Quad(a0, a2, b2, b0, col, new Vector3(-1, 0.6f, 0)); Quad(a1, a2, b2, b1, col, new Vector3(1, 0.6f, 0));
        Pop();
    }

    public int Count => _mb.Count;
    public ArrayMesh Build() => _mb.Build();

    /// <summary>Builds a MeshInstance3D with the shared vertex-colour material.</summary>
    public MeshInstance3D Instance(Node3D parent, Vector3? pos = null, string name = null, Material mat = null)
    {
        var mi = new MeshInstance3D { Mesh = Build(), MaterialOverride = mat ?? MeshBuilder.RockMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        if (pos.HasValue) mi.Position = pos.Value;
        if (name != null) mi.Name = name;
        parent.AddChild(mi);
        return mi;
    }
}
