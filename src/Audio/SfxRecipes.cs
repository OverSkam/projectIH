using System;
using System.Collections.Generic;
using static AppStein.Syn;
using static AppStein.SfxNames;

namespace AppStein;

/// <summary>One synthesised sound: how to build it and how the engine plays it. Buffers are normalised, so Db is the true relative loudness.</summary>
public sealed class Recipe
{
    public string Name;
    public Func<SynthRng, int, float[]> Build;
    public int Variants = 1;
    public bool Loop;
    public float LoopOverlap = 0.3f;
    public float Db;               // playback gain
    public float MaxDist = 30f;    // 3D: inaudible beyond this
    public float PitchVar = 0.05f, VolVar = 1.5f;
    public string Bus = "SFX";    // SFX (cave reverb) | UI (dry) | Ambience
}

public static class SfxRecipes
{
    public static readonly Dictionary<string, Recipe> Table = new();
    public const float MaxOneShotSec = 8f;

    static void Add(string name, float maxDist, float db, Func<SynthRng, int, float[]> build, int variants = 1, float pitch = 0.05f, float vol = 1.5f, string bus = "SFX")
        => Table[name] = new Recipe { Name = name, Build = build, MaxDist = maxDist, Db = db, Variants = variants, PitchVar = pitch, VolVar = vol, Bus = bus };
    static void AddLoop(string name, float maxDist, float db, float dur, Func<SynthRng, int, float[]> build, float overlap = 0.3f, string bus = "SFX")
        => Table[name] = new Recipe { Name = name, Build = (r, v) => build(r, v), Loop = true, LoopOverlap = overlap, MaxDist = maxDist, Db = db, PitchVar = 0f, VolVar = 0f, Bus = bus };

    /// <summary>Deterministic render of one variant. Raw (pre-normalisation) buffer is returned via raw for NaN checks.</summary>
    public static float[] Render(Recipe rc, int variant, out float[] raw)
    {
        var r = new SynthRng(SynthRng.Hash(rc.Name) + (uint)variant * 7919u);
        raw = rc.Build(r, variant);
        if (rc.Loop) raw = MakeLoop(raw, rc.LoopOverlap);
        return Finish(raw, rc.Loop);
    }

    // ------------------------------------------------------------ building blocks
    static float[] Rub(SynthRng r, float dur, float lo, float hi, float decay, float gain = 1f) => Gain(Burst(r, dur, lo, hi, decay), gain);

    static float[] Debris(SynthRng r, float from, float to, int count, float gain, float lo = 500, float hi = 4500)
    {
        var o = Zero(to + 0.1f);
        for (int i = 0; i < count; i++)
            o = Mix(o, Burst(r, r.Range(0.03f, 0.09f), lo, hi, r.Range(45f, 80f)), r.Range(from, to), gain * r.Range(0.3f, 1f));
        return o;
    }

    static float[] Horn(float f, float dur, float bright = 2400f)
    {
        var a = Osc(Wf.Saw, dur, t => f * (1f + 0.004f * MathF.Sin(6.28f * 5.5f * t)));
        var b = Osc(Wf.Saw, dur, t => f * 1.004f);
        var s = Zero(dur); for (int i = 0; i < s.Length; i++) s[i] = a[i] * 0.6f + b[i] * 0.6f;
        s = Filt(s, Ft.Lp, t => 500f + (bright - 500f) * MathF.Min(1f, t / 0.12f), 0.9f);
        return Ar(s, 0.05f, MathF.Min(0.25f, dur * 0.5f));
    }

    static float[] Marimba(float f, float dur) => Mix(Tone(Wf.Tri, f, dur, 7f), Tone(Wf.Sine, f * 3.9f, dur, 25f), 0, 0.3f);

    static float[] Crumble(SynthRng r, float dur, float fc, float decayStart, float decayRate, int debris)
    {
        var n = Filt(Noise(r, dur), Ft.Lp, t => fc * (1f - 0.55f * t / dur), 1.1f);
        Env(n, t => MathF.Min(1f, t / 0.6f) * MathF.Exp(-MathF.Max(0f, t - decayStart) * decayRate));
        n = Mix(n, Debris(r, 0.3f, dur * 0.85f, debris, 0.35f, 300, 2500), 0);
        return n;
    }

    static float[] SparklePops(SynthRng r, float dur, int count, float gain, float lo, float hi)
    {
        var o = Zero(dur);
        for (int i = 0; i < count; i++)
            o = Mix(o, Burst(r, r.Range(0.008f, 0.03f), lo, hi, r.Range(150f, 320f)), r.Range(0f, dur - 0.05f), gain * r.Range(0.3f, 1f));
        return o;
    }

    static float[] OreRing(SynthRng r, int v, float f, float dur, float decay, float bright, bool sparkle)
    {
        var o = PickBase(r, v);
        o = Mix(o, Bell(f, dur, decay, bright), 0.004f, 0.8f);
        if (sparkle) for (int i = 0; i < 5; i++) o = Mix(o, Bell(r.Range(4200f, 5200f), 0.25f, 18f, 0.2f), 0.03f + i * r.Range(0.04f, 0.1f), 0.18f);
        return o;
    }

    static float[] PickBase(SynthRng r, int v)
    {
        var o = Burst(r, 0.1f, 800, 6000, 50f);
        o = Mix(o, Tone(Wf.Sine, 900 + v * 250, 0.08f, 60f), 0, 0.5f);
        return Mix(o, Thump(180, 90, 0.1f, 30f), 0, 0.7f);
    }

    static float[] Arp(float[] ratios, float baseF, float step, float dur, float decay, float gain)
    {
        var o = Zero(dur);
        for (int k = 0; k < ratios.Length; k++) o = Mix(o, Bell(baseF * ratios[k], dur - k * step, decay, 0.5f), k * step, gain);
        return o;
    }

    static float[] Shimmer(float baseF, float[] ratios, float dur)
    {
        var o = Arp(ratios, baseF, 0.07f, dur, 5f, 0.6f);
        var pad = Mix(Osc(Wf.Sine, dur, baseF * 0.75f), Osc(Wf.Sine, dur, baseF * 1.12f), 0);
        Swell(pad);
        o = Mix(o, pad, 0, 0.15f);
        o = Echo(o, 0.18f, 0.4f, 3);
        return Reverb(o, 0.6f, 0.3f);
    }

    static float[] Growl(SynthRng r, float dur, float f0, float f1, float lp, float am, float drive)
    {
        var s = Osc(Wf.Saw, dur, t => f0 + (f1 - f0) * t / dur);
        s = Dist(s, drive);
        s = Filt(s, Ft.Lp, lp, 0.9f);
        s = Am(s, am, 0.5f);
        var n = Gain(Filt(Noise(r, dur), Ft.Bp, lp * 0.6f, 1.2f), 0.35f);
        return Mix(s, n, 0);
    }

    // ------------------------------------------------------------ the table
    static SfxRecipes()
    {
        // ---------- movement
        Add(StepRock, 18, -6, (r, v) =>
        {
            float f = 90 + v * 25;
            var o = Thump(f * 1.6f, f * 0.7f, 0.14f, 30f);
            o = Mix(o, Burst(r, 0.12f, 250, 2200 + v * 500, 38f), 0, 0.8f);
            return Mix(o, Burst(r, 0.05f, 2500, 7000, 90f), 0.004f, 0.25f);
        }, 3, 0.08f);
        Add(StepWood, 18, -5, (r, v) =>
        {
            var o = Thump(220, 120, 0.16f, 22f);
            o = Mix(o, Burst(r, 0.1f, 300, 1200, 35f), 0, 0.6f);
            return Mix(o, Tone(Wf.Sine, 260 + v * 30, 0.12f, 40f), 0, 0.3f);
        }, 3, 0.08f);
        Add(StepWater, 20, -6, (r, v) =>
        {
            var o = Ar(Burst(r, 0.28f, 500, 3500, 14f), 0.01f, 0.05f);
            o = Gain(o, 0.6f);
            for (int k = 0; k < 3; k++)
            {
                float f = r.Range(250f, 500f);
                o = Mix(o, Osc(Wf.Sine, 0.07f, t => f * (1f + 2.2f * t / 0.07f), t => MathF.Exp(-30f * t)), r.Range(0.02f, 0.2f), 0.3f);
            }
            return Mix(o, Thump(120, 70, 0.14f, 25f), 0, 0.4f);
        }, 3, 0.08f);
        Add(Jump, 14, -8, (r, v) => Mix(Rub(r, 0.14f, 200, 900, 22f, 0.5f), Thump(150, 100, 0.1f, 30f), 0, 0.4f));
        Add(Land, 20, -4, (r, v) => Mix(Thump(110, 55, 0.2f, 16f), Burst(r, 0.15f, 150, 1500, 28f), 0, 0.7f));
        Add(LandHard, 28, 0, (r, v) =>
        {
            var o = Mix(Thump(90, 40, 0.35f, 9f), Burst(r, 0.25f, 100, 3000, 16f), 0, 0.9f);
            o = Mix(o, Burst(r, 0.03f, 1500, 5000, 150f), 0, 0.5f);
            return Mix(o, Voice(r, 0.22f, t => 120f - 60f * t, 500, 1000), 0.03f, 0.5f);
        });

        // ---------- combat / mining
        Add(Swing, 16, -6, (r, v) => Swell(Gain(Filt(Noise(r, 0.26f), Ft.Bp, t => 500f + 1400f * MathF.Sin(MathF.PI * t / 0.26f), 1.2f), 1f)), 1, 0.1f);
        Add(PickRock, 32, -2, (r, v) => PickBase(r, v), 2, 0.08f);
        Add(PickCoal, 30, -3, (r, v) => OreRing(r, 0, 620, 0.25f, 18f, 0.3f, false));
        Add(PickIron, 32, -3, (r, v) => OreRing(r, 1, 980, 0.35f, 14f, 0.5f, false));
        Add(PickSilver, 34, -3, (r, v) => OreRing(r, 0, 1500, 0.5f, 11f, 0.7f, false));
        Add(PickGold, 36, -3, (r, v) => OreRing(r, 1, 1900, 0.7f, 8f, 0.9f, false));
        Add(PickRuby, 38, -3, (r, v) => OreRing(r, 0, 2500, 0.9f, 6f, 1.0f, false));
        Add(PickDiamond, 40, -3, (r, v) => OreRing(r, 1, 3300, 1.2f, 4f, 1.0f, true));
        Add(VeinBreak, 42, 0, (r, v) =>
        {
            var o = Mix(Burst(r, 0.5f, 100, 3500, 6f), Thump(100, 45, 0.4f, 8f), 0, 0.9f);
            o = Mix(o, Debris(r, 0.1f, 0.5f, 6, 0.5f, 800, 4000), 0);
            o = Mix(o, Bell(1800, 0.4f, 12f), 0.12f, 0.25f);
            return Reverb(o, 0.3f, 0.2f);
        });
        Add(Punch, 20, -2, (r, v) => Mix(Thump(160, 70, 0.14f, 28f), Burst(r, 0.1f, 0, 1400, 40f), 0, 0.8f));
        Add(HitMiner, 22, -1, (r, v) =>
        {
            var o = Gain(Burst(r, 0.16f, 0, 1800, 28f), 0.9f);
            o = Mix(o, Thump(130, 60, 0.18f, 22f), 0);
            return Mix(o, Burst(r, 0.03f, 2000, 5000, 120f), 0, 0.3f);
        }, 2, 0.08f);
        Add(Hurt, 1, -2, (r, v) => Mix(Voice(r, 0.28f, t => (150f + v * 25f) - 70f * t, 560, 1100, 0.2f), Burst(r, 0.12f, 0, 1500, 30f), 0, 0.3f), 2, 0.07f, 1f, "UI");
        Add(Grunt, 25, -4, (r, v) => Voice(r, 0.26f, t => (140f + v * 40f) - 60f * t, 540, 1050, 0.2f), 2, 0.1f);
        Add(Death, 38, 0, (r, v) =>
        {
            var o = Voice(r, 0.9f, t => 140f * (1f - 0.5f * t) + 5f * MathF.Sin(40f * t), 450, 900, 0.25f, 0.05f, 0.3f);
            o = Mix(o, Thump(100, 40, 0.6f, 6f), 0.5f, 0.7f);
            return Mix(o, Burst(r, 0.2f, 100, 1000, 20f), 0.55f, 0.5f);
        });

        // ---------- items
        Add(OrePickup, 1, -6, (r, v) =>
        {
            var o = Gain(Bell(1760, 0.3f, 14f, 0.5f), 0.7f);
            o = Mix(o, Bell(2640, 0.25f, 16f, 0.5f), 0.06f, 0.5f);
            return Mix(o, Burst(r, 0.06f, 1500, 6000, 80f), 0, 0.4f);
        }, 1, 0.06f, 1f, "UI");
        Add(SteinPickup, 1, -4, (r, v) => Shimmer(880, new[] { 1f, 1.25f, 1.5f, 1.875f, 2.25f }, 0.9f), 1, 0f, 0f, "UI");
        Add(SteinPickupRare, 1, -3, (r, v) => Shimmer(1175, new[] { 1f, 1.2f, 1.5f, 1.8f, 2f, 2.4f, 3f }, 1.2f), 1, 0f, 0f, "UI");
        Add(SteinPickupLegend, 1, -2, (r, v) =>
        {
            var low = Mix(Osc(Wf.Sine, 2.4f, 55), Osc(Wf.Sine, 2.4f, 58.3f), 0); Swell(low);
            var o = Gain(low, 0.8f);
            o = Mix(o, Swell(Bell(220, 2.2f, 2f)), 0, 0.5f);
            o = Mix(o, Swell(Bell(233, 2.2f, 2f)), 0, 0.4f);
            o = Mix(o, Swell(Voice(r, 2.2f, t => 110f, 400, 800)), 0, 0.25f);
            o = Mix(o, Bell(1319, 1.5f, 3f, 0.4f), 0.6f, 0.15f);
            return Reverb(o, 1.2f, 0.5f);
        }, 1, 0f, 0f, "UI");
        Add(ItemDrop, 18, -4, (r, v) =>
        {
            var o = Mix(Thump(120, 60, 0.18f, 18f), Burst(r, 0.15f, 300, 3000, 30f), 0, 0.5f);
            return Mix(o, Burst(r, 0.2f, 1000, 5000, 12f), 0.06f, 0.2f);
        });
        Add(SackFull, 1, -10, (r, v) =>
        {
            var b = Ar(Filt(Osc(Wf.Square, 0.14f, 150), Ft.Lp, 1200), 0.005f, 0.02f);
            return Mix(Mix(Zero(0.34f), b, 0), b, 0.18f);
        }, 1, 0f, 0f, "UI");

        // ---------- goblin
        Add(Feed, 1, -3, (r, v) =>
        {
            var o = Burst(r, 0.03f, 1000, 5000, 150f);
            o = Mix(o, Bell(2093, 0.5f, 7f, 0.6f), 0.08f, 0.7f);
            o = Mix(o, Bell(3136, 0.7f, 6f, 0.5f), 0.12f, 0.6f);
            for (int i = 0; i < 6; i++) o = Mix(o, Bell(r.Range(3800f, 6200f), 0.18f, 25f, 0.3f), 0.25f + i * 0.045f, 0.35f);
            return Reverb(o, 0.3f, 0.15f);
        }, 1, 0.02f, 0.5f, "UI");
        Add(GoblinLaugh, 45, 0, (r, v) =>
        {
            var o = Zero(0.9f);
            float b = 290 + v * 40;
            for (int k = 0; k < 6; k++)
                o = Mix(o, Voice(r, 0.085f, t => (b + k * 8f) * (1f + 0.05f * MathF.Sin(40f * t)), 450, 1900, 0.3f, 0.01f, 0.03f), k * 0.12f, 0.9f - 0.08f * k);
            return Mix(o, Voice(r, 0.22f, t => b * (1f - 0.3f * t / 0.22f), 420, 1700, 0.3f, 0.01f, 0.08f), 0.74f, 0.7f);
        }, 2, 0.04f);
        Add(GoblinGrowl, 45, 0, (r, v) => Ar(Growl(r, 1.0f, 75, 60, 700, 35, 2f), 0.1f, 0.25f), 1, 0.04f);
        Add(GoblinBlip, 40, -8, (r, v) =>
            Voice(r, 0.075f, t => (180f + v * 35f) * (1f + 0.15f * t / 0.075f), 350 + v * 80, 1200 + v * 250, 0.2f, 0.008f, 0.03f), 6, 0.06f, 2f);

        // ---------- ui / round
        Add(UiClick, 1, -8, (r, v) => Mix(Gain(Tone(Wf.Sine, 1400, 0.05f, 70f), 0.6f), Gain(Burst(r, 0.02f, 2000, 6000, 150f), 0.3f), 0, 1f), 1, 0.03f, 1f, "UI");
        Add(UiHover, 1, -12, (r, v) => Gain(Tone(Wf.Sine, 2000, 0.035f, 90f), 0.4f), 1, 0.04f, 1f, "UI");
        Add(UiOpen, 1, -12, (r, v) => Ad(Osc(Wf.Sine, 0.14f, t => 500f + 400f * t / 0.14f), 0.01f, 0.1f), 1, 0f, 0f, "UI");
        Add(UiClose, 1, -12, (r, v) => Ad(Osc(Wf.Sine, 0.14f, t => 900f - 400f * t / 0.14f), 0.01f, 0.1f), 1, 0f, 0f, "UI");
        Add(RoundStart, 1, -2, (r, v) =>
        {
            var o = Horn(146.8f, 0.32f);
            o = Mix(o, Horn(146.8f, 1.2f), 0.36f);
            o = Mix(o, Horn(220f, 1.2f), 0.36f, 0.8f);
            o = Mix(o, Horn(293.7f, 1.2f), 0.36f, 0.4f);
            return Reverb(o, 0.8f, 0.25f);
        }, 1, 0f, 0f, "UI");
        Add(Warning, 1, -6, (r, v) => Mix(Gain(Tone(Wf.Tri, 880, 0.06f, 45f), 0.8f), Gain(Burst(r, 0.04f, 1500, 4000, 80f), 0.3f), 0), 1, 0f, 0f, "UI");
        Add(Heartbeat, 1, -2, (r, v) =>
        {
            var o = Filt(Thump(75, 45, 0.16f, 18f), Ft.Lp, 220f);
            o = Mix(o, Filt(Thump(65, 40, 0.18f, 18f), Ft.Lp, 200f), 0.24f, 0.7f);
            return Reverb(o, 0.3f, 0.15f);
        }, 1, 0.02f, 0.5f, "UI");
        Add(LiftRumble, 1, 0, (r, v) =>
        {
            var o = Crumble(r, 4.2f, 140f, 2.5f, 1.2f, 14);
            o = Mix(o, Env(Thump(45, 30, 4f, 0.5f, 2f), t => MathF.Min(1f, t / 0.5f)), 0, 0.6f);
            return Reverb(o, 1.0f, 0.2f);
        }, 1, 0f, 0f);
        Add(RockCrash, 1, 0, (r, v) =>
        {
            var o = Mix(Burst(r, 0.5f, 80, 5000, 7f), Thump(70, 30, 1.2f, 3.5f), 0, 0.9f);
            o = Mix(o, Debris(r, 0.05f, 1.6f, 18, 0.5f), 0);
            return Reverb(o, 1.0f, 0.35f);
        }, 1, 0.03f, 0f);
        Add(StingerSafe, 1, 1, (r, v) =>
        {
            var o = Zero(1.5f);
            float[] n = { 523f, 659f, 784f, 1047f };
            for (int i = 0; i < 4; i++) o = Mix(o, Marimba(n[i], 0.5f), i * 0.12f);
            for (int i = 0; i < 4; i++) o = Mix(o, Marimba(n[i], 0.9f), 0.36f, 0.5f);
            return Reverb(o, 0.5f, 0.2f);
        }, 1, 0f, 0f, "UI");
        Add(StingerVerdict, 1, -2, (r, v) =>
        {
            float[] n = { 330f, 262f };
            var o = Zero(1.4f);
            for (int i = 0; i < 2; i++)
            {
                float f = n[i];
                var s = Filt(Osc(Wf.Saw, 0.55f, t => f * (1f - 0.3f * t / 0.55f) * (1f + 0.01f * MathF.Sin(40f * t))), Ft.Lp, 900f);
                o = Mix(o, Ar(s, 0.04f, 0.15f), i * 0.5f);
            }
            return Reverb(o, 0.4f, 0.2f);
        }, 1, 0f, 0f, "UI");
        Add(StingerThrow, 1, -2, (r, v) =>
        {
            var d = Mix(Osc(Wf.Saw, 2.5f, 82.4f), Osc(Wf.Saw, 2.5f, 116.5f), 0);
            d = Am(Filt(d, Ft.Lp, 400f), 6f, 0.5f); Swell(d);
            var o = Gain(d, 0.7f);
            o = Mix(o, Swell(Osc(Wf.Sine, 2.5f, t => 900f - 600f * t / 2.5f)), 0, 0.2f);
            o = Mix(o, Bell(98, 2f, 1.5f), 0.1f, 0.5f);
            return Reverb(o, 1.0f, 0.4f);
        }, 1, 0f, 0f, "UI");
        Add(Fanfare, 1, -1, (r, v) =>
        {
            var o = Zero(3.4f);
            (float f, float t, float d)[] m = { (262, 0, 0.2f), (262, 0.22f, 0.2f), (262, 0.44f, 0.2f), (349, 0.66f, 0.55f), (330, 1.25f, 0.25f), (392, 1.5f, 0.25f), (523, 1.75f, 1.3f) };
            foreach (var (f, t, d) in m)
            {
                o = Mix(o, Horn(f, d), t);
                o = Mix(o, Horn(f * 1.5f, d), t, 0.5f);
            }
            o = Echo(o, 0.2f, 0.3f, 2);
            return Reverb(o, 1.0f, 0.3f);
        }, 1, 0f, 0f, "UI");

        // ---------- world objects
        Add(ChestOpen, 25, -2, (r, v) =>
        {
            var c = Osc(Wf.Tri, 0.5f, t => 130f + 280f * t + 8f * MathF.Sin(113f * t));
            c = Ar(Filt(c, Ft.Lp, 700f), 0.04f, 0.1f);
            c = Mix(c, Gain(Burst(r, 0.5f, 400, 900, 3f), 0.2f), 0);
            var o = Mix(Gain(Burst(r, 0.03f, 1000, 5000, 150f), 0.7f), c, 0.02f);
            return Mix(o, Bell(2400, 0.5f, 10f), 0.5f, 0.2f);
        });
        AddLoop(ShrineHum, 22, -8, 3.5f, (r, v) =>
        {
            var o = Zero(3.5f);
            (float f, float a, float am)[] p = { (110f, 1f, 0.5f), (164.8f, 0.6f, 0.7f), (220.5f, 0.4f, 0.9f), (330f, 0.15f, 1.3f) };
            foreach (var (f, a, am) in p) o = Mix(o, Am(Osc(Wf.Sine, 3.5f, f), am, 0.4f), 0, a);
            return Mix(o, Gain(Filt(Noise(r, 3.5f), Ft.Bp, 800f, 3f), 0.2f), 0);
        }, 0.5f);
        Add(ShrineSuccess, 35, -2, (r, v) =>
        {
            var o = Arp(new[] { 1f, 1.26f, 1.5f, 2f }, 659f, 0.12f, 1.2f, 5f, 0.7f);
            var pad = Swell(Mix(Osc(Wf.Sine, 1.5f, 659f), Osc(Wf.Sine, 1.5f, 988f), 0));
            o = Mix(o, pad, 0, 0.2f);
            return Reverb(Echo(o, 0.17f, 0.4f, 2), 0.8f, 0.35f);
        });
        Add(ShrineFail, 35, -2, (r, v) =>
        {
            var o = Filt(Osc(Wf.Saw, 0.8f, t => 220f - 110f * t / 0.8f), Ft.Lp, 700f);
            o = Mix(o, Filt(Osc(Wf.Saw, 0.8f, t => 233f - 117f * t / 0.8f), Ft.Lp, 700f), 0);
            o = Ar(o, 0.02f, 0.3f);
            o = Mix(o, Thump(100, 40, 0.4f, 8f), 0, 0.8f);
            o = Mix(o, Am(Gain(Filt(Osc(Wf.Square, 0.4f, 80f), Ft.Lp, 600f), 0.3f), 20f, 0.8f), 0);
            return Reverb(o, 0.5f, 0.25f);
        });
        Add(PlateClick, 25, -2, (r, v) =>
        {
            var o = Mix(Gain(Thump(180, 90, 0.12f, 35f), 0.9f), Burst(r, 0.06f, 800, 4000, 70f), 0, 0.6f);
            return Mix(o, Bell(1200, 0.1f, 60f), 0, 0.3f);
        });
        Add(RopePlace, 20, -3, (r, v) =>
        {
            var o = Mix(Burst(r, 0.1f, 200, 2000, 35f), Thump(110, 70, 0.12f, 30f), 0, 0.6f);
            return Mix(o, Burst(r, 0.18f, 1500, 5000, 14f), 0.02f, 0.3f);
        });
        Add(RopeCut, 25, -2, (r, v) =>
        {
            var o = Burst(r, 0.04f, 2000, 8000, 90f);
            o = Mix(o, Exp(Osc(Wf.Sine, 0.3f, t => 180f + 520f * MathF.Exp(-14f * t)), 9f), 0, 0.5f);
            return Mix(o, Burst(r, 0.12f, 500, 3000, 25f), 0.08f, 0.3f);
        });
        AddLoop(FuseHiss, 25, -8, 1.4f, (r, v) =>
        {
            var o = Am(Filt(Filt(Noise(r, 1.4f), Ft.Hp, 2500f), Ft.Lp, 9000f), 30f, 0.3f);
            return Mix(Gain(o, 0.5f), SparklePops(r, 1.4f, 10, 0.5f, 3000, 9000), 0);
        }, 0.2f);
        Add(Explosion, 130, 6, (r, v) =>
        {
            var o = Burst(r, 0.06f, 500, 9000, 40f);
            var boom = Filt(Noise(r, 2.5f), Ft.Lp, t => 150f + 4000f * MathF.Exp(-2.6f * t));
            Env(boom, t => MathF.Exp(-1.8f * t));
            o = Mix(o, boom, 0, 1.4f);
            o = Mix(o, Thump(95, 28, 2.5f, 1.4f, 4f), 0, 1.2f);
            o = Mix(o, Debris(r, 0.4f, 2.8f, 25, 0.5f), 0);
            return Reverb(o, 2.5f, 0.5f);
        }, 1, 0.03f, 0f);
        Add(BearTrapSnap, 28, 2, (r, v) =>
        {
            var o = Gain(Bell(1700, 0.5f, 12f, 1.5f), 0.7f);
            o = Mix(o, Burst(r, 0.04f, 1500, 8000, 100f), 0);
            o = Mix(o, Thump(160, 80, 0.1f, 30f), 0);
            return Mix(o, Exp(Osc(Wf.Sine, 0.25f, t => 300f + 500f * MathF.Exp(-12f * t) + 15f * MathF.Sin(150f * t)), 10f), 0.03f, 0.3f);
        });
        Add(LanternOn, 12, -4, (r, v) =>
        {
            var o = Gain(Burst(r, 0.012f, 1500, 5000, 300f), 0.5f);
            o = Mix(o, Gain(Swell(Filt(Noise(r, 0.3f), Ft.Lp, t => 300f + 1500f * t / 0.3f)), 0.6f), 0.04f);
            return Mix(o, Thump(80, 50, 0.2f, 15f), 0.04f, 0.3f);
        });
        Add(LanternOut, 12, -4, (r, v) =>
        {
            var o = Gain(Burst(r, 0.012f, 1500, 5000, 300f), 0.4f);
            return Mix(o, Gain(Ad(Filt(Noise(r, 0.35f), Ft.Lp, t => 1200f - 900f * t / 0.35f), 0.01f, 0.08f), 0.5f), 0.01f);
        });
        Add(EtherealWhoosh, 25, -4, (r, v) =>
        {
            var n = Swell(Filt(Noise(r, 1.2f), Ft.Bp, t => 400f + 2600f * (t / 1.2f) * (t / 1.2f), 3f));
            var s = Swell(Am(Osc(Wf.Sine, 1.2f, t => 500f + 700f * t), 6f, 0.5f));
            var o = Mix(Gain(n, 0.7f), s, 0, 0.15f);
            return Reverb(Echo(o, 0.15f, 0.4f, 3), 0.8f, 0.4f);
        });
        Add(GhostPebble, 22, 0, (r, v) =>
        {
            var o = Zero(0.3f);
            foreach (float t in new[] { 0f, 0.09f, 0.15f })
                o = Mix(o, Burst(r, 0.03f, 1500, 5000, 120f), t, 1f - t * 2f);
            o = Mix(o, Thump(200, 120, 0.05f, 60f), 0, 0.3f);
            return Reverb(o, 0.3f, 0.3f);
        });

        // ---------- traps
        Add(SpikeClick, 25, -3, (r, v) => Mix(Gain(Burst(r, 0.04f, 2500, 9000, 120f), 0.7f), Tone(Wf.Tri, 2200, 0.05f, 100f), 0, 0.3f));
        Add(SpikeThrust, 28, 0, (r, v) =>
        {
            var o = Gain(Filt(Noise(r, 0.15f), Ft.Bp, t => 5000f - 3000f * t / 0.15f, 1.5f), 0.6f);
            o = Mix(o, Thump(140, 70, 0.15f, 30f), 0, 0.9f);
            return Mix(o, Bell(2600, 0.2f, 25f), 0, 0.2f);
        });
        Add(RockfallRumble, 50, 2, (r, v) =>
        {
            var n = Filt(Noise(r, 2f), Ft.Lp, t => 180f + 60f * MathF.Sin(9f * t), 1.2f);
            Env(n, t => MathF.Min(1f, t / 0.7f));
            n = Mix(n, Debris(r, 0.4f, 1.8f, 6, 0.2f, 800, 3000), 0);
            return Reverb(n, 0.5f, 0.2f);
        });
        Add(RockfallCrash, 55, 3, (r, v) =>
        {
            var o = Mix(Burst(r, 0.4f, 80, 4500, 8f), Thump(60, 30, 0.9f, 5f), 0, 1f);
            o = Mix(o, Debris(r, 0.05f, 1.3f, 10, 0.5f), 0);
            return Reverb(o, 0.8f, 0.3f);
        });
        Add(RockPebble, 18, -8, (r, v) => Mix(Gain(Burst(r, 0.04f, 1500, 5000, 110f), 0.6f), Thump(300, 200, 0.03f, 80f), 0, 0.3f), 2, 0.12f);
        Add(GasHiss, 25, -2, (r, v) => Ar(Filt(Noise(r, 1.6f), Ft.Bp, 4500f, 0.6f), 0.25f, 0.6f));
        Add(DartTwang, 30, -2, (r, v) =>
        {
            var o = Exp(Osc(Wf.Sine, 0.6f, t => 520f * (1f + 0.01f * MathF.Sin(250f * t))), 7f);
            o = Mix(o, Tone(Wf.Sine, 1040, 0.4f, 12f), 0, 0.3f);
            return Mix(o, Burst(r, 0.02f, 1000, 6000, 150f), 0, 0.4f);
        });
        Add(DartWhoosh, 25, -4, (r, v) => Gain(Swell(Filt(Noise(r, 0.22f), Ft.Bp, t => 3000f - 1500f * t / 0.22f, 1.5f)), 0.5f));
        Add(DartThud, 25, -3, (r, v) => Mix(Thump(180, 90, 0.12f, 35f), Burst(r, 0.05f, 300, 2000, 70f), 0, 0.5f), 2, 0.1f);
        Add(TrapdoorCreak, 28, 0, (r, v) =>
        {
            var o = Osc(Wf.Saw, 0.95f, t => 150f + 70f * MathF.Sin(22f * t) + 60f * t);
            o = Swell(Filt(o, Ft.Lp, 900f));
            return Mix(o, Gain(Burst(r, 0.95f, 300, 1500, 2f), 0.1f), 0);
        });
        Add(TrapdoorDrop, 35, 1, (r, v) =>
        {
            var o = Gain(Swell(Filt(Noise(r, 0.5f), Ft.Bp, t => 2000f - 1500f * t / 0.5f, 1f)), 0.4f);
            o = Mix(o, Thump(70, 35, 0.6f, 6f), 0.35f);
            o = Mix(o, Burst(r, 0.3f, 100, 3000, 12f), 0.35f, 0.7f);
            return Reverb(o, 0.4f, 0.25f);
        });
        Add(LeverClunk, 25, 0, (r, v) =>
        {
            var o = Gain(Thump(200, 80, 0.12f, 30f), 0.9f);
            o = Mix(o, Bell(900, 0.3f, 18f, 0.8f), 0, 0.4f);
            o = Mix(o, Burst(r, 0.04f, 800, 4000, 80f), 0, 0.5f);
            return Mix(o, Thump(150, 70, 0.1f, 30f), 0.12f, 0.5f);
        });
        Add(GateChains, 35, 0, (r, v) =>
        {
            var o = Gain(Exp(Filt(Noise(r, 1.1f), Ft.Bp, 400f), 1.5f), 0.4f);
            float t = 0f;
            for (int k = 0; k < 14; k++)
            {
                o = Mix(o, Bell(r.Range(2200f, 4000f), 0.12f, 35f), t, 0.3f);
                o = Mix(o, Burst(r, 0.04f, 1500, 7000, 90f), t, 0.5f * r.Range(0.5f, 1f));
                t += 0.075f * (1f - 0.012f * k);
            }
            return o;
        });
        Add(GateSlam, 45, 3, (r, v) =>
        {
            var o = Gain(Bell(180, 1.2f, 3.5f, 2f), 0.5f);
            o = Mix(o, Thump(80, 35, 0.8f, 5f), 0, 0.9f);
            o = Mix(o, Burst(r, 0.1f, 300, 6000, 35f), 0, 0.8f);
            o = Mix(o, Bell(620, 0.8f, 6f), 0, 0.3f);
            return Reverb(o, 0.7f, 0.3f);
        });

        // ---------- monsters (v2.1 monsters: src/Monsters/MonsterSfx.cs)
        MonsterSfx.Register();
        Add(GolemStep, 75, 3, (r, v) =>
        {
            var o = Mix(Thump(60 + v * 6, 28, 0.5f, 8f, 20f), Burst(r, 0.3f, 50, 500, 10f), 0, 0.7f);
            o = Mix(o, Burst(r, 0.08f, 600, 3000, 45f), 0.02f, 0.3f);
            return Reverb(o, 0.4f, 0.25f);
        }, 2, 0.06f);
        Add(GolemWindup, 40, 0, (r, v) =>
        {
            var s = Osc(Wf.Saw, 1.2f, t => 55f + 40f * t / 1.2f);
            s = Am(Filt(Dist(s, 2f), Ft.Lp, 500f), 7f, 0.4f);
            s = Mix(s, Gain(Filt(Noise(r, 1.2f), Ft.Bp, 300f, 1.5f), 0.3f), 0);
            return Swell(s);
        });
        Add(GolemSlam, 60, 4, (r, v) =>
        {
            var o = Gain(Thump(55, 22, 1.6f, 2.5f), 1.1f);
            o = Mix(o, Burst(r, 0.6f, 60, 3000, 5f), 0);
            o = Mix(o, Debris(r, 0.05f, 1.2f, 12, 0.5f), 0);
            return Reverb(o, 1.0f, 0.35f);
        });
        Add(GolemCrack, 30, 0, (r, v) =>
        {
            var o = Burst(r, 0.05f, 3500, 9000, 100f);
            o = Mix(o, Bell(2400, 0.6f, 9f, 1.2f), 0, 0.5f);
            o = Mix(o, Bell(3700, 0.5f, 12f), 0.02f, 0.35f);
            for (int i = 0; i < 6; i++) o = Mix(o, Bell(r.Range(3000f, 6000f), 0.15f, 30f), r.Range(0.05f, 0.6f), 0.25f);
            return o;
        });
        Add(GolemStun, 55, 3, (r, v) =>
        {
            var o = Crumble(r, 3.2f, 300f, 0.8f, 1.1f, 20);
            o = Mix(o, Thump(50, 25, 3f, 1f), 0, 0.7f);
            return Reverb(o, 1.0f, 0.3f);
        });
        AddLoop(EmberCrackle, 20, 2, 2.0f, (r, v) =>
        {
            var o = Gain(Filt(Noise(r, 2.0f), Ft.Lp, 900f), 0.12f);
            o = Mix(o, SparklePops(r, 2.0f, 22, 0.8f, 1000, 3500), 0);
            return o;
        });
        Add(EmberLunge, 25, 0, (r, v) =>
        {
            var o = Gain(Ad(Filt(Noise(r, 0.6f), Ft.Bp, t => 400f + 2600f * t / 0.6f, 0.8f), 0.25f, 0.2f), 0.8f);
            return Mix(o, SparklePops(r, 0.6f, 8, 0.5f, 1000, 4000), 0);
        });
        Add(EmberSizzle, 25, 0, (r, v) =>
        {
            var o = Filt(Filt(Noise(r, 1.4f), Ft.Hp, 3000f), Ft.Lp, 9000f);
            Env(o, t => MathF.Exp(-1.6f * t) * (0.6f + 0.4f * MathF.Sin(80f * t) * MathF.Sin(37f * t)));
            return Mix(Gain(o, 0.6f), SparklePops(r, 1.2f, 8, 0.5f, 2000, 6000), 0);
        });
        AddLoop(WispHiss, 22, -4, 2.5f, (r, v) =>
        {
            var o = Gain(Filt(Noise(r, 2.5f), Ft.Bp, t => 2400f + 700f * MathF.Sin(6.28f * 0.4f * t), 2f), 0.5f);
            o = Mix(o, Gain(Am(Osc(Wf.Sine, 2.5f, 700f), 3f, 0.8f), 0.08f), 0);
            return Mix(o, Gain(Filt(Noise(r, 2.5f), Ft.Bp, 5000f, 2f), 0.2f), 0);
        });
        AddLoop(DragonBreath, 45, -4, 4.5f, (r, v) =>
        {
            Func<float, float> e = t => 0.15f + 0.85f * MathF.Pow(0.5f - 0.5f * MathF.Cos(6.2832f * t / 4f), 1.5f);
            var o = Env(Filt(Noise(r, 4.5f), Ft.Lp, 220f), e);
            o = Mix(o, Env(Osc(Wf.Sine, 4.5f, 38f), e), 0, 0.5f);
            return Mix(o, Env(Gain(Filt(Noise(r, 4.5f), Ft.Bp, 900f, 2f), 0.1f), e), 0);
        }, 0.5f);
        Add(DragonStir, 55, 2, (r, v) =>
        {
            var o = Ar(Growl(r, 1.8f, 48, 62, 500, 22, 2.5f), 0.3f, 0.4f);
            return Reverb(o, 0.6f, 0.3f);
        });
        Add(DragonRoar, 110, 4, (r, v) =>
        {
            var s = Mix(Osc(Wf.Saw, 3.6f, t => 70f + 60f * MathF.Sin(MathF.PI * t / 3.2f)), Osc(Wf.Saw, 3.6f, t => 140f + 120f * MathF.Sin(MathF.PI * t / 3.2f)), 0, 0.5f);
            s = Dist(s, 3f);
            var f = Filt(s, Ft.Bp, t => 300f + 900f * MathF.Sin(MathF.PI * t / 3.4f), 2f);
            s = Mix(Filt(s, Ft.Lp, 800f), f, 0, 0.8f);
            s = Mix(s, Gain(Filt(Noise(r, 3.6f), Ft.Bp, 1500f, 1f), 0.3f), 0);
            s = Am(s, 28f, 0.4f);
            Env(s, t => MathF.Min(1f, t / 0.2f) * MathF.Exp(-MathF.Max(0f, t - 2.2f) * 1.2f));
            return Reverb(s, 1.5f, 0.45f);
        });
        Add(DragonFire, 70, 3, (r, v) =>
        {
            var n = Filt(Noise(r, 2.4f), Ft.Bp, t => 700f + 1800f * MathF.Min(1f, t / 1.2f), 0.7f);
            var rum = Gain(Filt(Noise(r, 2.4f), Ft.Lp, 120f), 1.2f);
            var o = Mix(n, rum, 0);
            o = Mix(o, SparklePops(r, 2.4f, 25, 0.4f, 1000, 4000), 0);
            Ad(o, 0.35f, 1.4f);
            return Reverb(o, 0.8f, 0.3f);
        });

        // ---------- ambience
        AddLoop(AmbDrone, 1, -22, 8f, (r, v) =>
        {
            var o = Zero(8f);
            o = Mix(o, Osc(Wf.Sine, 8f, 55f), 0, 1f);
            o = Mix(o, Osc(Wf.Sine, 8f, 55.35f), 0, 0.8f);
            o = Mix(o, Osc(Wf.Sine, 8f, 82.4f), 0, 0.35f);
            o = Am(o, 0.12f, 0.4f);
            o = Mix(o, Am(Gain(Filt(Noise(r, 8f), Ft.Lp, 200f), 0.25f), 0.1f, 0.5f), 0);
            return Mix(o, Gain(Filt(Noise(r, 8f), Ft.Bp, 1200f, 3f), 0.015f), 0);
        }, 1f, "Ambience");
        Add(AmbDrip, 30, -14, (r, v) =>
        {
            float f0 = 700 + v * 250;
            var o = Exp(Osc(Wf.Sine, 0.15f, t => f0 * (0.7f + 0.6f * (1f - MathF.Exp(-60f * t)))), 30f);
            return Reverb(o, 0.9f, 0.5f, 1.5f);
        }, 3, 0.1f, 2f, "Ambience");
        Add(AmbCreak, 30, -16, (r, v) =>
        {
            var o = Osc(Wf.Tri, 1.4f, t => 90f + v * 40f + 50f * MathF.Sin(MathF.PI * t / 1.4f) + 8f * MathF.Sin(31f * t));
            o = Swell(Filt(o, Ft.Lp, 600f));
            o = Mix(o, Gain(Swell(Filt(Noise(r, 1.4f), Ft.Bp, 300f, 1f)), 0.08f), 0);
            return Reverb(o, 0.6f, 0.25f);
        }, 2, 0.08f, 2f, "Ambience");
        Add(AmbSettle, 32, -15, (r, v) =>
        {
            var o = Mix(Gain(Thump(70, 40, 0.5f, 6f), 0.3f), Burst(r, 0.7f, 200, 1800, 5f), 0, 0.4f);
            for (int i = 0; i < 5; i++) o = Mix(o, Burst(r, 0.02f, 1200, 4500, 100f), r.Range(0.1f, 0.8f), 0.3f);
            return Reverb(o, 0.6f, 0.35f);
        }, 2, 0.08f, 2f, "Ambience");
        AddLoop(Campfire, 18, 0, 3f, (r, v) =>
        {
            var bed = Env(Gain(Filt(Noise(r, 3f), Ft.Lp, 700f), 0.18f), t => 1f + 0.25f * MathF.Sin(6.28f * 0.7f * t) + 0.1f * MathF.Sin(6.28f * 2.3f * t));
            var o = Mix(bed, SparklePops(r, 3f, 30, 0.7f, 800, 4500), 0);
            for (int i = 0; i < 4; i++) o = Mix(o, Burst(r, 0.04f, 400, 3000, 80f), r.Range(0.1f, 2.9f), 0.9f);
            return Mix(o, Gain(Filt(Noise(r, 3f), Ft.Bp, 4000f, 1f), 0.05f), 0);
        }, 0.4f, "Ambience");
        AddLoop(WaterLap, 14, -8, 4f, (r, v) =>
        {
            var n = Filt(Noise(r, 4f), Ft.Bp, t => 600f + 400f * MathF.Sin(6.28f * 0.5f * t), 1f);
            Env(n, t => MathF.Pow(0.5f + 0.5f * MathF.Sin(6.28f * 0.5f * t), 2f));
            var o = Mix(n, Gain(Filt(Noise(r, 4f), Ft.Lp, 500f), 0.15f), 0);
            for (int i = 0; i < 6; i++)
            {
                float f = r.Range(350f, 900f);
                o = Mix(o, Osc(Wf.Sine, 0.08f, t => f * (1f + 1.5f * t / 0.08f), t => MathF.Exp(-25f * t)), r.Range(0f, 3.8f), 0.15f);
            }
            return o;
        }, 0.5f, "Ambience");
        LootSfx.Register(Table);   // step 2 (src/Loot2): geodes, bright chests, mimics
    }
}
