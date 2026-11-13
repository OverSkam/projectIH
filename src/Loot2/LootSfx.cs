using System;
using System.Collections.Generic;
using static AppStein.Syn;
using static AppStein.SfxNames;

namespace AppStein;

/// <summary>Synthesised sounds of step 2. Registered from SfxRecipes' static constructor (names are in SfxNames; --sfxcheck verifies every one).</summary>
public static class LootSfx
{
    static void Add(Dictionary<string, Recipe> t, string name, float maxDist, float db, Func<SynthRng, int, float[]> build, int variants = 1, float pitch = 0.04f, float vol = 1f, string bus = "SFX")
        => t[name] = new Recipe { Name = name, Build = build, MaxDist = maxDist, Db = db, Variants = variants, PitchVar = pitch, VolVar = vol, Bus = bus };

    static float[] Glass(SynthRng r, float at, int shards, float lo, float hi, float gain)
    {
        var o = Zero(at + 0.5f);
        for (int i = 0; i < shards; i++) o = Mix(o, Bell(r.Range(lo, hi), 0.35f, 14f, 0.8f), at + i * r.Range(0.01f, 0.05f), gain * r.Range(0.4f, 1f));
        return o;
    }

    public static void Register(Dictionary<string, Recipe> T)
    {
        // ---------- geode hits: each crack louder and more glassy
        Add(T, GeodeHit1, 22, -5, (r, v) =>
        {
            var o = Mix(Burst(r, 0.12f, 1500, 6000, 50f), Thump(240, 110, 0.12f, 30f), 0, 0.8f);
            return Mix(o, Bell(2600, 0.25f, 22f, 0.5f), 0.01f, 0.18f);
        }, 2, 0.06f);
        Add(T, GeodeHit2, 28, -2, (r, v) =>
        {
            var o = Mix(Burst(r, 0.2f, 900, 7000, 30f), Thump(170, 70, 0.2f, 18f), 0, 1f);
            o = Mix(o, Glass(r, 0.02f, 3, 2800, 4200, 0.35f), 0);
            return Mix(o, SparklePops(r, 0.3f, 6, 0.4f, 3000, 8000), 0.03f);
        }, 2, 0.06f);
        Add(T, GeodeHit3, 38, 1, (r, v) =>
        {
            var o = Mix(Burst(r, 0.4f, 500, 8000, 14f), Thump(120, 45, 0.35f, 10f), 0, 1.1f);
            o = Mix(o, Glass(r, 0.02f, 6, 2200, 5200, 0.5f), 0);
            o = Mix(o, Burst(r, 0.2f, 3000, 9000, 18f), 0.06f, 0.5f);
            return Reverb(o, 0.25f, 0.18f);
        }, 1, 0.03f);

        // ---------- the reveal, by rarity
        Add(T, RevealDull, 22, -2, (r, v) =>
        {
            var o = Mix(Thump(95, 42, 0.45f, 9f), Burst(r, 0.45f, 0, 900, 7f), 0, 0.7f);
            return Mix(o, Burst(r, 0.25f, 200, 1800, 14f), 0.02f, 0.35f);
        }, 2, 0.05f);
        Add(T, RevealBright, 42, 1, (r, v) =>
        {
            var o = Ar(Filt(Noise(r, 0.22f), Ft.Bp, t => 2500f + 9000f * t, 1.5f), 0.01f, 0.12f);
            o = Mix(o, Thump(180, 70, 0.3f, 12f), 0, 0.6f);
            float[] f = { 1760f, 2217f, 2637f, 3520f };
            for (int i = 0; i < f.Length; i++) o = Mix(o, Bell(f[i], 0.9f, 5f, 0.6f), 0.04f + i * 0.07f, 0.55f);
            o = Mix(o, SparklePops(r, 0.6f, 10, 0.4f, 3500, 9000), 0.1f);
            return Reverb(o, 0.5f, 0.25f);
        }, 1, 0.03f);
        Add(T, RevealGem, 64, 3, (r, v) =>
        {
            var o = Mix(Thump(110, 38, 0.6f, 6f), Ar(Filt(Noise(r, 0.35f), Ft.Bp, t => 1800f + 11000f * t, 1.4f), 0.01f, 0.2f), 0, 0.7f);
            float[] f = { 1568f, 1976f, 2349f, 2794f, 3136f };
            for (int i = 0; i < f.Length; i++) o = Mix(o, Bell(f[i], 2.0f, 2.4f, 0.8f), 0.03f + i * 0.06f, 0.6f);
            var pad = Mix(Osc(Wf.Sine, 2.2f, 392f), Osc(Wf.Sine, 2.2f, 588f), 0); Swell(pad);
            o = Mix(o, pad, 0.05f, 0.22f);
            o = Mix(o, SparklePops(r, 1.4f, 24, 0.35f, 3500, 9500), 0.1f);
            o = Echo(o, 0.16f, 0.42f, 3);
            return Reverb(o, 1.1f, 0.4f);
        }, 1, 0.02f);
        Add(T, RevealStein, 48, 5, (r, v) =>
        {
            var boom = Mix(Thump(70, 30, 1.3f, 3f, 5f), Burst(r, 0.9f, 0, 600, 4f), 0, 0.6f);
            var o = Gain(boom, 1.1f);
            var rise = Osc(Wf.Saw, 2.4f, t => 110f * (1f + 1.6f * t / 2.4f));
            rise = Filt(rise, Ft.Lp, t => 300f + 2600f * t / 2.4f, 0.9f); Swell(rise);
            o = Mix(o, rise, 0.0f, 0.28f);
            var voice = Voice(r, 2.4f, t => 220f + 40f * t, 650, 1100, 0.1f, 0.5f, 0.6f);
            o = Mix(o, voice, 0.1f, 0.25f);
            float[] f = { 1175f, 1480f, 1760f, 2349f, 2960f, 3520f };
            for (int i = 0; i < f.Length; i++) o = Mix(o, Bell(f[i], 2.6f, 1.8f, 0.8f), 0.06f + i * 0.08f, 0.5f);
            o = Mix(o, SparklePops(r, 2.2f, 40, 0.4f, 3500, 9500), 0.1f);
            o = Echo(o, 0.21f, 0.45f, 3);
            return Reverb(o, 1.6f, 0.5f);
        }, 1, 0f);

        // ---------- chests
        Add(T, ChestCreak, 20, -6, (r, v) =>
        {
            var c = Osc(Wf.Saw, 1.0f, t => 70f + 55f * MathF.Sin(6.28f * 2.2f * t) + 30f * t);
            c = Filt(c, Ft.Lp, t => 260f + 500f * MathF.Sin(3.1f * t), 4f);
            Swell(c);
            var n = Gain(Filt(Noise(r, 1.0f), Ft.Bp, 700f, 5f), 0.25f); Swell(n);
            return Mix(c, n, 0, 0.8f);
        }, 2, 0.08f);
        float[] pings = { 1047f, 1175f, 1319f, 1568f, 1760f, 2093f };
        string[] pingNames = { ChestPing1, ChestPing2, ChestPing3, ChestPing4, ChestPing5, ChestPing6 };
        for (int k = 0; k < 6; k++)
        {
            float pf = pings[k]; int kk = k;
            Add(T, pingNames[k], 30, -3 + kk * 0.5f, (r, v) =>
            {
                var o = Bell(pf, 0.7f, 6f, 0.7f);
                o = Mix(o, Bell(pf * 2f, 0.4f, 12f, 0.4f), 0.01f, 0.35f);
                o = Mix(o, Burst(r, 0.03f, 3000, 9000, 120f), 0, 0.3f);
                return Reverb(o, 0.3f, 0.25f);
            }, 1, 0f, 0f);
        }
        Add(T, ChestBurst, 44, 1, (r, v) =>
        {
            var o = Ar(Filt(Noise(r, 0.5f), Ft.Bp, t => 1500f + 6000f * t / 0.5f, 1.2f), 0.02f, 0.3f);
            o = Mix(o, Thump(150, 60, 0.3f, 10f), 0, 0.5f);
            o = Mix(o, Bell(2100, 1.2f, 3f, 0.8f), 0.04f, 0.4f);
            o = Mix(o, Bell(3150, 1.0f, 3.5f, 0.8f), 0.1f, 0.3f);
            o = Mix(o, SparklePops(r, 0.9f, 22, 0.4f, 3500, 9000), 0.05f);
            return Reverb(o, 0.8f, 0.3f);
        }, 1, 0.02f);

        // ---------- mimic
        Add(T, MimicBreath, 6, -9, (r, v) =>
        {
            var o = Filt(Noise(r, 1.5f), Ft.Bp, t => 420f + 140f * MathF.Sin(2.1f * t), 1.2f);
            Env(o, t => MathF.Pow(MathF.Sin(MathF.PI * t / 1.5f), 2f));
            return Mix(o, Gain(Swell(Osc(Wf.Sine, 1.5f, 62f)), 0.2f), 0);
        }, 2, 0.05f, 0.5f);
        Add(T, MimicSnap, 32, 0, (r, v) =>
        {
            var o = Mix(Thump(190, 70, 0.22f, 16f), Burst(r, 0.14f, 400, 4500, 26f), 0, 0.9f);
            o = Mix(o, Mix(Thump(210, 90, 0.18f, 22f), Burst(r, 0.1f, 600, 5000, 34f), 0, 0.8f), 0.13f, 0.8f);
            o = Mix(o, Bell(520, 0.5f, 7f, 0.4f), 0.01f, 0.3f);
            return Reverb(o, 0.25f, 0.15f);
        }, 1, 0.03f);
        Add(T, MimicShriek, 95, 3, (r, v) =>
        {
            var s = Osc(Wf.Saw, 1.4f, t => 560f + 1300f * MathF.Sin(MathF.PI * MathF.Min(1f, t / 1.1f) * 0.5f) + 40f * MathF.Sin(48f * t));
            s = Dist(s, 4f);
            s = Filt(s, Ft.Bp, t => 1400f + 1500f * t, 1.6f);
            var n = Gain(Filt(Noise(r, 1.4f), Ft.Bp, 2600f, 1.4f), 0.4f);
            var o = Mix(s, n, 0);
            Env(o, t => MathF.Min(1f, t / 0.03f) * MathF.Exp(-MathF.Max(0f, t - 0.8f) * 3.5f));
            o = Mix(o, Thump(120, 50, 0.5f, 5f), 0, 0.5f);
            return Reverb(o, 0.8f, 0.3f);
        }, 1, 0f, 0f);
        Add(T, MimicScuttle, 24, -4, (r, v) =>
        {
            var o = Zero(0.9f);
            for (int i = 0; i < 14; i++) o = Mix(o, Burst(r, 0.02f, 1800, 6500, 140f), i * 0.06f + r.Range(0f, 0.02f), r.Range(0.5f, 1f));
            return Mix(o, Gain(Filt(Noise(r, 0.9f), Ft.Bp, 900f, 1.5f), 0.05f), 0);
        }, 2, 0.06f);
        Add(T, Bats, 70, 0, (r, v) =>
        {
            var o = Zero(2.0f);
            for (int i = 0; i < 26; i++)
            {
                float at = r.Range(0f, 1.6f), f0 = r.Range(3200f, 5600f);
                o = Mix(o, Osc(Wf.Sine, 0.09f, t => f0 * (1f + 0.5f * t / 0.09f), t => MathF.Exp(-30f * t)), at, 0.35f);
                o = Mix(o, Burst(r, 0.06f, 700, 3000, 40f), at + r.Range(0f, 0.1f), 0.5f);
            }
            var flutter = Env(Filt(Noise(r, 2.0f), Ft.Bp, 1100f, 0.9f), t => (0.55f + 0.45f * MathF.Sin(6.28f * 17f * t)) * MathF.Sin(MathF.PI * t / 2.0f));
            return Mix(o, flutter, 0, 0.5f);
        }, 1, 0.02f);
        Add(T, GasPuff, 26, -3, (r, v) =>
        {
            var o = Ar(Filt(Noise(r, 0.9f), Ft.Bp, t => 3800f - 1600f * t, 0.7f), 0.01f, 0.6f);
            return Mix(o, Thump(110, 55, 0.25f, 14f), 0, 0.4f);
        }, 1, 0.04f);
    }

    static float[] SparklePops(SynthRng r, float dur, int count, float gain, float lo, float hi)
    {
        var o = Zero(dur);
        for (int i = 0; i < count; i++)
            o = Mix(o, Burst(r, r.Range(0.008f, 0.03f), lo, hi, r.Range(150f, 320f)), r.Range(0f, dur - 0.05f), gain * r.Range(0.3f, 1f));
        return o;
    }
}
