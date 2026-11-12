using System;
using Godot;

namespace AppStein;

/// <summary>Geometry of the giant Goblin (all coordinates in metres, built with <see cref="GMesh"/>). Rig space: origin on the floor under his seat, he faces +Z.</summary>
public static class GoblinParts
{
    // ---- palette
    public static readonly Color Skin = new(0.37f, 0.54f, 0.25f);
    public static readonly Color SkinLight = new(0.50f, 0.70f, 0.28f);
    public static readonly Color SkinDark = new(0.30f, 0.46f, 0.18f);
    public static readonly Color Bag = new(0.27f, 0.38f, 0.19f);
    public static readonly Color Nose = new(0.52f, 0.68f, 0.26f);
    public static readonly Color Lip = new(0.30f, 0.38f, 0.20f);
    public static readonly Color Hair = new(0.70f, 0.72f, 0.77f);
    public static readonly Color HairShade = new(0.48f, 0.50f, 0.57f);
    public static readonly Color Brow = new(0.20f, 0.20f, 0.24f);
    public static readonly Color Navy = new(0.13f, 0.19f, 0.60f);
    public static readonly Color NavyLight = new(0.20f, 0.29f, 0.75f);
    public static readonly Color Shirt = new(0.80f, 0.80f, 0.80f);
    public static readonly Color Pants = new(0.12f, 0.12f, 0.15f);
    public static readonly Color Leather = new(0.20f, 0.12f, 0.08f);
    public static readonly Color Gold = new(0.85f, 0.65f, 0.2f);
    public static readonly Color Stone = new(0.27f, 0.27f, 0.27f);

    // ---- skeleton anchors (rig space)
    public static readonly Vector3 HeadPos = new(0, 7.2f, 0.05f);
    public static readonly Vector3 TorsoPos = new(0, 2.3f, -0.3f);
    public static readonly Vector3 ShoulderR = new(-3.1f, 5.0f, -0.4f);   // his right = world -X (the feeding hand)
    public static readonly Vector3 ShoulderL = new(3.1f, 5.0f, -0.4f);    // his left = world +X (the throwing arm)
    public static readonly Vector3 PalmWrist = new(-4.2f, 1.2f, 2.7f);    // wrist of the feeding hand; the palm centre is 1.7 m beyond it (z = 4.4)
    public static readonly Vector3 RestWristL = new(1.7f, 4.05f, 1.3f);   // left hand resting on his thigh
    public const float UpperLen = 3.0f, ForeLen = 3.3f;

    // ------------------------------------------------------------------ throne
    public static void Throne(Node3D parent)
    {
        var m = new GMesh { Variation = 0.12f };
        m.Box(new Vector3(0, 0.75f, -0.3f), new Vector3(8.6f, 1.5f, 4.8f), Stone);
        m.Box(new Vector3(0, 0.35f, 2.4f), new Vector3(9.4f, 0.7f, 1.2f), Stone * 0.85f);
        m.Box(new Vector3(-4.7f, 1.2f, -0.6f), new Vector3(1.4f, 2.4f, 2.6f), Stone * 1.1f, new Vector3(0, 0.2f, 0));
        m.Box(new Vector3(4.7f, 1.1f, -0.6f), new Vector3(1.4f, 2.2f, 2.8f), Stone * 1.05f, new Vector3(0, -0.25f, 0));
        // a cushion of gold coins and a ledger on the seat: he is a financier
        for (int i = 0; i < 7; i++)
            m.Ellipsoid(new Vector3(-2.6f + i * 0.2f, 1.58f, -1.4f + (i % 3) * 0.25f), new Vector3(0.25f, 0.05f, 0.25f), Gold, 7, 2);
        m.Instance(parent, null, "Throne");
    }

    // ------------------------------------------------------------------ legs (static, trousers + shoes)
    public static void Legs(Node3D parent)
    {
        var m = new GMesh();
        foreach (float s in new[] { -1f, 1f })
        {
            float x = 1.6f * s;
            m.Box(new Vector3(x, 2.7f, 1.8f), new Vector3(2.0f, 1.7f, 4.2f), Pants);                       // thigh
            m.Ellipsoid(new Vector3(x, 2.7f, 3.9f), new Vector3(1.05f, 0.95f, 0.9f), Pants, 8, 5);          // knee
            m.Box(new Vector3(x, 1.4f, 4.15f), new Vector3(1.7f, 2.7f, 1.5f), Pants * 1.1f);               // shin
            m.Box(new Vector3(x, 0.4f, 4.9f), new Vector3(1.9f, 0.8f, 2.6f), Leather);                       // shoe
            m.Ellipsoid(new Vector3(x, 0.55f, 6.0f), new Vector3(0.95f, 0.5f, 0.7f), Leather * 1.15f, 8, 4); // toe cap
        }
        m.Instance(parent, null, "Legs");
    }

    // ------------------------------------------------------------------ torso: navy quarter-zip pullover, popped collar over a white shirt collar
    public static void Torso(Node3D torso)
    {
        var m = new GMesh();
        m.Ellipsoid(new Vector3(0, -0.1f, 0.3f), new Vector3(2.5f, 0.9f, 2.0f), Pants, 10, 5);             // hips in trousers
        m.Box(new Vector3(0, 0.55f, 0.35f), new Vector3(4.5f, 0.25f, 3.2f), Leather * 1.2f);                // belt
        m.Box(new Vector3(0, 0.55f, 2.12f), new Vector3(0.7f, 0.4f, 0.12f), Gold);                           // buckle
        m.Ellipsoid(new Vector3(0, 1.1f, 0.65f), new Vector3(2.1f, 1.3f, 1.5f), Navy, 12, 6);            // paunch
        m.Ellipsoid(new Vector3(0, 2.15f, 0.1f), new Vector3(2.35f, 1.3f, 1.4f), Navy, 12, 6);            // chest
        foreach (float s in new[] { -1f, 1f })
        {
            m.Ellipsoid(new Vector3(2.4f * s, 3.0f, -0.1f), new Vector3(0.95f, 0.8f, 0.95f), Navy, 9, 5);   // shoulders
            m.Ellipsoid(new Vector3(0.9f * s, 3.25f, -0.15f), new Vector3(1.4f, 0.55f, 1.0f), Navy, 8, 4); // trapezius
        }
        // ribbed hem + quarter-zip placket
        m.Box(new Vector3(0, 0.62f, 0.4f), new Vector3(4.4f, 0.18f, 3.1f), NavyLight * 0.9f);
        m.Box(new Vector3(0, 2.85f, 1.46f), new Vector3(0.12f, 1.6f, 0.08f), new Color(0.75f, 0.77f, 0.82f), new Vector3(-0.12f, 0, 0));
        m.Box(new Vector3(0, 2.0f, 1.55f), new Vector3(0.22f, 0.3f, 0.1f), new Color(0.80f, 0.82f, 0.86f));  // zip pull
        m.Box(new Vector3(-1.0f, 2.45f, 1.45f), new Vector3(0.3f, 0.2f, 0.07f), Gold * 0.7f, new Vector3(0, 0.1f, 0.05f)); // chest emblem
        // collar: white shirt collar points under a big popped navy collar
        m.Box(new Vector3(-0.75f, 3.78f, 0.85f), new Vector3(1.0f, 0.75f, 0.14f), Shirt, new Vector3(-0.35f, 0.45f, 0.35f));
        m.Box(new Vector3(0.75f, 3.78f, 0.85f), new Vector3(1.0f, 0.75f, 0.14f), Shirt, new Vector3(-0.35f, -0.45f, -0.35f));
        m.Box(new Vector3(-1.25f, 3.95f, 0.25f), new Vector3(1.5f, 1.4f, 0.26f), NavyLight, new Vector3(-0.25f, 1.25f, 0.45f));
        m.Box(new Vector3(1.25f, 3.95f, 0.25f), new Vector3(1.5f, 1.4f, 0.26f), NavyLight, new Vector3(-0.25f, -1.25f, -0.45f));
        m.Box(new Vector3(0, 4.05f, -0.85f), new Vector3(2.9f, 1.4f, 0.26f), NavyLight * 0.9f, new Vector3(0.3f, 0, 0));
        m.Instance(torso, null, "TorsoMesh");
    }

    // ------------------------------------------------------------------ arm bones (built along -Z, length 1 m scaled by the rig)
    public static MeshInstance3D UpperArm(Node3D holder)
    {
        var m = new GMesh();
        m.Tube(Vector3.Zero, new Vector3(0, 0, -1f), 0.78f, 0.62f, Navy, 8, 1f, false, false);
        m.Ellipsoid(new Vector3(0, 0, -0.5f), new Vector3(0.84f, 0.84f, 0.35f), NavyLight * 0.9f, 8, 3);   // bulky sleeve fold
        return m.Instance(holder);
    }
    public static MeshInstance3D ForeArm(Node3D holder)
    {
        var m = new GMesh();
        m.Tube(Vector3.Zero, new Vector3(0, 0, -1f), 0.62f, 0.46f, Skin, 8, 1f, false, false);
        m.Tube(new Vector3(0, 0, -0.03f), new Vector3(0, 0, -0.2f), 0.72f, 0.70f, Navy, 8, 1f, true, true);   // pushed-up sleeve cuff at the elbow
        m.Tube(new Vector3(0, 0, -0.80f), new Vector3(0, 0, -0.9f), 0.52f, 0.52f, Gold, 8, 1f, true, true);   // gold watch
        m.Box(new Vector3(0, 0.5f, -0.85f), new Vector3(0.5f, 0.1f, 0.4f), Gold * 1.1f);
        return m.Instance(holder);
    }
    public static MeshInstance3D Ball(Node3D parent, float r, Color c)
    {
        var m = new GMesh(); m.Ellipsoid(Vector3.Zero, new Vector3(r, r, r), c, 8, 5);
        return m.Instance(parent);
    }

    // ------------------------------------------------------------------ hand (fingers along -Z, palm normal +Y, thumb at local +X)
    public static void Hand(Node3D hand, out Node3D[] fingers, out Node3D[] tips, out Node3D thumb)
    {
        var m = new GMesh();
        m.Box(new Vector3(0, 0, -0.95f), new Vector3(1.75f, 0.45f, 1.7f), Skin);
        m.Ellipsoid(new Vector3(0, 0, -0.15f), new Vector3(0.78f, 0.3f, 0.55f), Skin, 8, 4);
        m.Ellipsoid(new Vector3(0, -0.05f, -1.0f), new Vector3(0.95f, 0.32f, 0.9f), SkinDark * 1.1f, 8, 4);   // heel / pads
        m.Instance(hand, null, "Palm");
        fingers = new Node3D[4]; tips = new Node3D[4];
        float[] xs = { -0.62f, -0.21f, 0.21f, 0.62f };
        float[] len = { 0.8f, 0.9f, 0.88f, 0.72f };
        for (int i = 0; i < 4; i++)
        {
            var f = new Node3D { Position = new Vector3(xs[i], 0, -1.8f) };
            hand.AddChild(f); fingers[i] = f;
            var fm = new GMesh();
            fm.Tube(Vector3.Zero, new Vector3(0, 0, -len[i]), 0.2f, 0.17f, Skin, 6, 1f, false, false);
            fm.Ellipsoid(new Vector3(0, 0, -0.02f), new Vector3(0.21f, 0.2f, 0.18f), Skin * 1.05f, 6, 3);
            fm.Instance(f);
            var t = new Node3D { Position = new Vector3(0, 0, -len[i]) };
            f.AddChild(t); tips[i] = t;
            var tm = new GMesh();
            tm.Tube(Vector3.Zero, new Vector3(0, 0, -len[i] * 0.8f), 0.17f, 0.11f, Skin, 6, 1f, false, true);
            tm.Box(new Vector3(0, 0.1f, -len[i] * 0.65f), new Vector3(0.14f, 0.05f, 0.25f), new Color(0.78f, 0.72f, 0.45f));   // yellowed nail
            tm.Instance(t);
        }
        thumb = new Node3D { Position = new Vector3(0.85f, 0, -0.7f), Rotation = new Vector3(0, -0.5f, 0) };
        hand.AddChild(thumb);
        var thm = new GMesh();
        thm.Tube(Vector3.Zero, new Vector3(0, 0, -1.0f), 0.26f, 0.18f, Skin, 6, 1f, false, true);
        thm.Instance(thumb);
    }

    // ------------------------------------------------------------------ head
    // The face is a sculpted height field (z over x,y) made of Gaussian features: brow ridge, eye sockets, a big hooked nose, cheekbones, hollows, lips with a smirk,
    // chin, jowls. It is triangulated and flat shaded, so it reads as a faceted low-poly face with coherent planes instead of a bag of blobs.
    static float G(float x, float y, float cx, float cy, float sx, float sy) { float dx = x - cx, dy = y - cy; return Mathf.Exp(-(dx * dx / (2 * sx * sx) + dy * dy / (2 * sy * sy))); }
    static float MouthY(float x) => -1.12f + 0.14f * Mathf.Clamp(x / 0.7f, -1f, 1f);   // the mouth line rises to the right: the smug half-smile

    /// <summary>Height of the face surface at (x, y) plus its colour. Returns false outside the face outline.</summary>
    static bool FaceAt(float x, float y, out float z, out Color col)
    {
        z = 0; col = Skin;
        float u = x * x / (1.32f * 1.32f) + (y + 0.25f) * (y + 0.25f) / (1.7f * 1.7f);
        if (u >= 0.985f) return false;
        z = 0.35f + 1.0f * Mathf.Pow(1f - u, 0.38f);
        float ax = Mathf.Abs(x), sg = x < 0 ? -1f : 1f;
        float my = MouthY(x);
        // brow ridge and heavy brow bone, sockets, bags
        z += 0.24f * G(x, y, 0, 0.64f, 1.0f, 0.15f);
        float sock = G(ax, y, 0.52f, 0.28f, 0.32f, 0.22f);
        z -= 0.14f * sock;
        float bag = G(ax, y, 0.52f, -0.02f, 0.34f, 0.09f);
        z += 0.10f * bag;
        // nose: bridge, bulb, hooked tip, wings
        float bridge = G(x, y, 0, -0.12f, 0.15f, 0.42f), bulb = G(x, y, 0, -0.52f, 0.28f, 0.2f), hook = G(x, y, 0, -0.76f, 0.17f, 0.14f), wing = G(ax, y, 0.32f, -0.6f, 0.15f, 0.13f);
        z += 0.40f * bridge + 0.72f * bulb + 0.32f * hook + 0.34f * wing + 0.18f * G(x, y, 0, -0.22f, 0.13f, 0.2f) - 0.16f * G(x, y, 0, 0.38f, 0.16f, 0.12f);
        // cheekbones, gaunt hollows, jowls, chin
        z += 0.20f * G(ax, y, 0.8f, -0.25f, 0.3f, 0.22f);
        z -= 0.12f * G(ax, y, 0.72f, -0.72f, 0.26f, 0.24f);
        float jowl = G(ax, y, 1.0f, -1.25f, 0.4f, 0.45f);
        z += 0.28f * jowl;
        z += 0.62f * G(x, y, 0, -1.68f, 0.45f, 0.3f);
        // lips and the mouth groove between them
        float up = G(x, y, 0, my + 0.09f, 0.56f, 0.065f), lo = G(x, y, 0, my - 0.11f, 0.48f, 0.085f), groove = G(x, y, 0, my, 0.68f, 0.026f);
        z += 0.10f * up + 0.11f * lo - 0.16f * groove;
        // smirk: a cheek bulge on the raised side
        float smirk = x > 0 ? G(x, y, 0.78f, -0.8f, 0.24f, 0.2f) : 0f;
        z += 0.14f * smirk;
        // nasolabial folds
        float fold = G(ax, y, 0.45f, -0.85f, 0.07f, 0.28f);
        z -= 0.07f * fold;
        // forehead wrinkles
        z -= 0.025f * (Mathf.Sin(y * 28f) * 0.5f + 0.5f) * G(x, y, 0, 1.0f, 0.9f, 0.3f);
        // colour
        var c = Skin;
        c = c.Lerp(SkinLight, Mathf.Clamp(0.5f * G(ax, y, 0.8f, -0.25f, 0.3f, 0.22f) + 0.7f * G(x, y, 0, 0.64f, 1.0f, 0.15f), 0f, 1f));
        c = c.Lerp(Nose, Mathf.Clamp(bridge * 1.1f + bulb + hook + wing, 0f, 1f));
        c = c.Lerp(Bag * 0.8f, Mathf.Clamp(bag * 1.3f + sock * 0.7f, 0f, 1f));
        c = c.Lerp(SkinDark * 0.75f, Mathf.Clamp(wing * 0.5f + 0.5f * G(ax, y, 0.5f, -0.72f, 0.1f, 0.3f), 0f, 1f));
        c = c.Lerp(SkinDark * 1.1f, Mathf.Clamp(jowl * 0.7f + fold * 0.8f, 0f, 1f));
        c = c.Lerp(Lip, Mathf.Clamp((up + lo) * 1.1f, 0f, 1f));
        c = c.Lerp(new Color(0.04f, 0.06f, 0.03f), Mathf.Clamp(groove * 1.8f, 0f, 1f));
        col = c;
        return true;
    }

    /// <summary>Triangulates the face for y in [y0, y1] (grid step 0.1). Upper part (static head) and lower part (jaw) share the row at y = -1.05.</summary>
    static void FaceMesh(GMesh m, float yTop, float yBottom, Vector3 offset)
    {
        const float step = 0.1f;
        int rows = Mathf.RoundToInt((yTop - yBottom) / step), cols = 28;
        var P = new Vector3?[cols + 1, rows + 1]; var Cc = new Color[cols + 1, rows + 1];
        for (int i = 0; i <= cols; i++)
            for (int j = 0; j <= rows; j++)
            {
                float x = -1.4f + i * step, y = yTop - j * step;
                if (FaceAt(x, y, out float z, out var c)) { P[i, j] = new Vector3(x, y, z) + offset; Cc[i, j] = c; }
            }
        for (int i = 0; i < cols; i++)
            for (int j = 0; j < rows; j++)
            {
                var a = P[i, j]; var b = P[i + 1, j]; var c2 = P[i + 1, j + 1]; var d = P[i, j + 1];
                var ca = Cc[i, j]; var cb = Cc[i + 1, j]; var cc = Cc[i + 1, j + 1]; var cd = Cc[i, j + 1];
                bool flip = ((i + j) & 1) == 0;
                void T(Vector3? p, Vector3? q, Vector3? r, Color c0, Color c1, Color c2c)
                {
                    if (p == null || q == null || r == null) return;
                    m.Tri(p.Value, q.Value, r.Value, new Color((c0.R + c1.R + c2c.R) / 3f, (c0.G + c1.G + c2c.G) / 3f, (c0.B + c1.B + c2c.B) / 3f), Vector3.Back);
                }
                if (flip) { T(a, b, c2, ca, cb, cc); T(a, c2, d, ca, cc, cd); }
                else { T(a, b, d, ca, cb, cd); T(b, c2, d, cb, cc, cd); }
            }
    }

    /// <summary>Static part of the head: skull, upper face, nose, brow, nostrils, mouth cavity, hair, neck. Head space: origin = centre of the skull, +Y up, +Z the face.</summary>
    public static void HeadStatic(Node3D head)
    {
        var m = new GMesh { Variation = 0.035f };
        // skull and the mass under the face
        m.Ellipsoid(new Vector3(0, 0.45f, -0.2f), new Vector3(1.28f, 1.22f, 1.25f), Skin * 0.95f, 12, 7);
        m.Ellipsoid(new Vector3(0, -0.85f, 0.0f), new Vector3(1.05f, 0.85f, 0.95f), Skin * 0.9f, 10, 6);
        FaceMesh(m, 1.35f, -1.15f, Vector3.Zero);
        // nostrils and the dark behind the lips
        foreach (float s in new[] { -1f, 1f })
            m.Ellipsoid(new Vector3(0.13f * s, -0.80f, 1.62f), new Vector3(0.09f, 0.05f, 0.1f), new Color(0.03f, 0.05f, 0.02f), 6, 3);
        m.Ellipsoid(new Vector3(0, -1.12f, 0.95f), new Vector3(0.62f, 0.12f, 0.3f), new Color(0.02f, 0.03f, 0.02f), 8, 3);
        // neck
        m.Tube(new Vector3(0, -1.2f, -0.15f), new Vector3(0, -2.7f, -0.25f), 1.0f, 1.15f, Skin * 0.85f, 10);
        // swept-back grey hair: high forehead, big volume at the sides and the back
        m.Variation = 0.06f;
        m.Ellipsoid(new Vector3(0, 0.95f, -0.45f), new Vector3(1.42f, 0.82f, 1.5f), new Vector3(-0.22f, 0, 0), Hair * 0.96f, 12, 6);
        m.Ellipsoid(new Vector3(0, 1.3f, 0.12f), new Vector3(1.1f, 0.42f, 0.7f), new Vector3(-0.6f, 0, 0), Hair, 10, 4);
        for (int i = -4; i <= 4; i++)
            m.Ellipsoid(new Vector3(i * 0.27f, 1.42f - Mathf.Abs(i) * 0.09f, -0.2f - Mathf.Abs(i) * 0.04f), new Vector3(0.2f, 0.17f, 0.95f), new Vector3(-0.3f, -i * 0.07f, 0), (i & 1) == 0 ? Hair * 1.06f : HairShade, 6, 3);
        foreach (float s in new[] { -1f, 1f })
        {
            m.Ellipsoid(new Vector3(1.2f * s, 0.55f, -0.45f), new Vector3(0.46f, 0.88f, 0.95f), Hair * 0.97f, 9, 5);
            m.Ellipsoid(new Vector3(1.12f * s, 0.12f, 0.3f), new Vector3(0.14f, 0.42f, 0.28f), HairShade, 6, 3);            // sideburn
            m.Ellipsoid(new Vector3(1.0f * s, 0.95f, 0.35f), new Vector3(0.3f, 0.35f, 0.55f), Hair, 7, 4);                    // receding temple wave
        }
        m.Ellipsoid(new Vector3(0, 0.15f, -1.25f), new Vector3(1.18f, 1.05f, 0.7f), Hair * 0.95f, 10, 6);
        m.Ellipsoid(new Vector3(0, -0.6f, -1.1f), new Vector3(0.95f, 0.6f, 0.5f), HairShade, 9, 4);
        m.Instance(head, null, "HeadMesh");
    }

    /// <summary>The jaw: the face below the mouth line, chin, lower lip, jowls. Pivot at the hinge; returns the jaw node.</summary>
    public static Node3D Jaw(Node3D head)
    {
        var piv = new Node3D { Position = new Vector3(0, -0.55f, -0.1f), Name = "Jaw" };
        head.AddChild(piv);
        var m = new GMesh { Variation = 0.035f };
        FaceMesh(m, -1.15f, -2.05f, new Vector3(0, 0.55f, 0.1f));    // build in head space, minus the pivot offset
        m.Push(new Vector3(0, 0.55f, 0.1f));
        m.Ellipsoid(new Vector3(0, -1.25f, 0.25f), new Vector3(0.95f, 0.5f, 0.8f), Skin * 0.9f, 10, 4);
        m.Ellipsoid(new Vector3(0, -1.65f, 0.55f), new Vector3(0.5f, 0.35f, 0.5f), SkinDark * 1.2f, 8, 3);   // double chin
        m.Pop();
        m.Instance(piv);
        return piv;
    }

    /// <summary>One eye: eyeball + iris + pupil (the returned node rotates to look). Position it at the eye centre in head space.</summary>
    public static Node3D Eye(Node3D head, float side)
    {
        var eye = new Node3D { Position = new Vector3(0.5f * side, 0.32f, 1.02f), Name = side < 0 ? "EyeL" : "EyeR" };
        head.AddChild(eye);
        var m = new GMesh { Variation = 0.03f };
        m.Ellipsoid(Vector3.Zero, new Vector3(0.27f, 0.25f, 0.25f), new Color(0.93f, 0.91f, 0.68f), 10, 6);
        m.Ellipsoid(new Vector3(0, 0, 0.2f), new Vector3(0.16f, 0.16f, 0.07f), new Color(0.86f, 0.62f, 0.08f), 10, 3);
        m.Ellipsoid(new Vector3(0, 0, 0.245f), new Vector3(0.075f, 0.08f, 0.04f), new Color(0.01f, 0.01f, 0.01f), 8, 3);
        m.Instance(eye);
        return eye;
    }

    /// <summary>Heavy upper lid: a skin slab over the top of the eye; the node moves down to blink.</summary>
    public static Node3D Lid(Node3D head, float side)
    {
        var lid = new Node3D { Position = new Vector3(0.5f * side, 0.32f, 1.02f), Name = side < 0 ? "LidL" : "LidR" };
        head.AddChild(lid);
        var m = new GMesh { Variation = 0.04f };
        m.Ellipsoid(new Vector3(0, 0.2f, 0.07f), new Vector3(0.33f, 0.17f, 0.28f), new Vector3(0.25f, 0, 0.12f * side), SkinDark * 1.12f, 9, 4);
        m.Ellipsoid(new Vector3(0, 0.32f, 0.02f), new Vector3(0.36f, 0.12f, 0.27f), Skin, 9, 3);
        m.Instance(lid);
        return lid;
    }

    /// <summary>Heavy dark-grey eyebrow: a thick bushy arch, low at the nose and tapering to a tail (pivot at the brow centre).</summary>
    public static Node3D EyeBrow(Node3D head, float side)
    {
        var brow = new Node3D { Position = new Vector3(0.52f * side, 0.7f, 1.12f), Name = side < 0 ? "BrowL" : "BrowR" };
        head.AddChild(brow);
        var m = new GMesh { Variation = 0.12f };
        m.Tube(new Vector3(-0.46f * side, -0.08f, 0.0f), new Vector3(-0.02f * side, 0.1f, 0.02f), 0.13f, 0.16f, Brow, 5, 1.1f);
        m.Tube(new Vector3(-0.02f * side, 0.1f, 0.02f), new Vector3(0.5f * side, -0.04f, -0.08f), 0.16f, 0.06f, Brow * 0.92f, 5, 1.1f);
        m.Tube(new Vector3(-0.3f * side, 0.0f, 0.1f), new Vector3(0.2f * side, 0.12f, 0.1f), 0.07f, 0.07f, Brow * 1.15f, 4);
        m.Instance(brow);
        return brow;
    }

    /// <summary>Long pointed ear (pivot at the base on the side of the head).</summary>
    public static Node3D Ear(Node3D head, float side)
    {
        var piv = new Node3D { Position = new Vector3(1.2f * side, 0.15f, -0.2f), Name = side < 0 ? "EarL" : "EarR" };
        head.AddChild(piv);
        var m = new GMesh();
        m.Ellipsoid(new Vector3(0.1f * side, 0, 0), new Vector3(0.2f, 0.5f, 0.4f), Skin, 7, 4);
        m.Tube(new Vector3(0.15f * side, 0.15f, 0), new Vector3(2.0f * side, 1.5f, -0.35f), 0.52f, 0.04f, Skin, 6, 0.3f, true, true, Vector3.Back);
        m.Tube(new Vector3(0.25f * side, 0.2f, 0.07f), new Vector3(1.7f * side, 1.3f, -0.27f), 0.32f, 0.02f, new Color(0.60f, 0.45f, 0.28f), 6, 0.3f, true, true, Vector3.Back);
        m.Ellipsoid(new Vector3(0.3f * side, -0.35f, 0.05f), new Vector3(0.16f, 0.28f, 0.2f), SkinLight, 6, 3);   // lobe
        m.Instance(piv);
        return piv;
    }
}
