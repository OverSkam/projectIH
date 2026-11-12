using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

public static partial class CaveVisuals
{
    /// <summary>Static dressing of the Hall of the Scale (merged into the rock mesh) + its sickly lights + the cage lift. The Goblin, Scale and shaft are separate nodes.</summary>
    static void HallDecor(Ctx cx, Slot s, Rnd r)
    {
        var info = cx.G.Info; var mb = cx.R(s.Id);
        var seat = info.GoblinSeat; var h0 = seat + new Vector3(0, 0, 9.2f);
        // huge stalactites in the dark of the ceiling
        for (int k = 0; k < 16; k++)
        {
            var p = h0 + new Vector3(r.Range(-11f, 11f), 12.1f, r.Range(-11f, 11f));
            mb.Cone(p, r.Range(0.5f, 1.4f), -r.Range(1.8f, 4.5f), Rock(0, 2, (uint)r.Next(), true, 0), 6, r.F() * 3f);
        }
        // banners either side of the Goblin (flat on the north wall)
        foreach (float sx in new[] { -9.0f, 9.0f })
        {
            mb.Box(h0 + new Vector3(sx, 7.6f, -11.55f), new Vector3(2.4f, 5.4f, 0.12f), new Color(0.30f, 0.06f, 0.07f));
            mb.Box(h0 + new Vector3(sx, 10.4f, -11.5f), new Vector3(2.8f, 0.2f, 0.25f), new Color(0.28f, 0.2f, 0.1f));
            mb.Box(h0 + new Vector3(sx, 7.9f, -11.45f), new Vector3(0.9f, 0.9f, 0.05f), new Color(0.55f, 0.42f, 0.15f), 0.785f);
        }
        // the sickly light of the Hall: a dim greenish-yellow fill high up (the Goblin's own face light is part of his rig)
        cx.Light(h0 + new Vector3(0, 9.5f, -2f), new Color(0.55f, 0.72f, 0.22f), 0.85f, 24f, false);
        cx.Light(h0 + new Vector3(0, 4f, 8f), new Color(0.50f, 0.62f, 0.25f), 0.5f, 14f, false);
        BuildCage(cx, info.CagePos, r);
    }

    /// <summary>Barred cage on a chain hanging from the ceiling over a floor plate (visual only; a box collider keeps miners out of the bars).</summary>
    static void BuildCage(Ctx cx, Vector3 p, Rnd r)
    {
        var mb = cx.R(0);
        var iron = new Color(0.16f, 0.16f, 0.18f); var rust = new Color(0.30f, 0.17f, 0.10f);
        const float W = 2.3f, H = 2.6f, lift = 0.15f;
        mb.Box(p + new Vector3(0, 0.05f, 0), new Vector3(W + 1.2f, 0.1f, W + 1.2f), new Color(0.2f, 0.2f, 0.22f));          // floor plate / landing
        mb.Box(p + new Vector3(0, lift + 0.06f, 0), new Vector3(W, 0.12f, W), iron);                                      // cage floor
        mb.Box(p + new Vector3(0, lift + H, 0), new Vector3(W, 0.14f, W), iron);                                          // cage roof
        for (int k = 0; k < 4; k++)
        {
            float cxk = (k % 2 == 0 ? -1 : 1) * (W * 0.5f - 0.05f), czk = (k / 2 == 0 ? -1 : 1) * (W * 0.5f - 0.05f);
            mb.Box(p + new Vector3(cxk, lift + H * 0.5f, czk), new Vector3(0.12f, H, 0.12f), rust);
        }
        for (float t = -W * 0.5f + 0.25f; t < W * 0.5f - 0.1f; t += 0.3f)
            foreach (float e in new[] { -1f, 1f })
            {
                mb.Box(p + new Vector3(t, lift + H * 0.5f, e * (W * 0.5f - 0.05f)), new Vector3(0.05f, H - 0.1f, 0.05f), iron);
                mb.Box(p + new Vector3(e * (W * 0.5f - 0.05f), lift + H * 0.5f, t), new Vector3(0.05f, H - 0.1f, 0.05f), iron);
            }
        foreach (float yy in new[] { 0.9f, 1.8f })
        {
            mb.Box(p + new Vector3(0, lift + yy, -(W * 0.5f - 0.05f)), new Vector3(W, 0.06f, 0.06f), iron);
            mb.Box(p + new Vector3(0, lift + yy, W * 0.5f - 0.05f), new Vector3(W, 0.06f, 0.06f), iron);
            mb.Box(p + new Vector3(-(W * 0.5f - 0.05f), lift + yy, 0), new Vector3(0.06f, 0.06f, W), iron);
            mb.Box(p + new Vector3(W * 0.5f - 0.05f, lift + yy, 0), new Vector3(0.06f, 0.06f, W), iron);
        }
        // chain up to a pulley beam under the ceiling
        for (float y = lift + H + 0.1f; y < 11.4f; y += 0.42f)
            mb.Box(p + new Vector3(0, y, 0), y % 0.84f < 0.42f ? new Vector3(0.1f, 0.34f, 0.26f) : new Vector3(0.26f, 0.34f, 0.1f), iron);
        mb.Box(p + new Vector3(0, 11.6f, 0), new Vector3(0.9f, 0.9f, 0.9f), rust, 0.4f);
        mb.Box(p + new Vector3(0, 11.9f, 0), new Vector3(4.5f, 0.35f, 0.35f), new Color(0.25f, 0.15f, 0.09f));
        // a small hooded lantern on the cage: warm, dim (a lantern is a legal light source)
        cx.Gl("lantern").Box(p + new Vector3(W * 0.5f + 0.05f, lift + H - 0.35f, 0), new Vector3(0.18f, 0.26f, 0.18f), Colors.White);
        cx.Light(p + new Vector3(W * 0.5f + 0.4f, lift + H - 0.3f, 0), new Color(1f, 0.68f, 0.3f), 0.55f, 5.5f, true, 0.15f);
        cx.Colliders.Add((p + new Vector3(0, lift + H * 0.5f, 0), new Vector3(W, H, W)));
    }
}
