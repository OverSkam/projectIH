using System;
using static AppStein.Syn;
using static AppStein.SfxNames;

namespace AppStein;

/// <summary>Synthesised sounds of the v2.1 monsters (registered from the SfxRecipes table; names live in SfxNames). Played from the monster classes on every peer.</summary>
public static class MonsterSfx
{
    static void Add(string name, float maxDist, float db, Func<SynthRng, int, float[]> build, int variants = 1, float pitch = 0.05f, float vol = 1.5f)
        => SfxRecipes.Table[name] = new Recipe { Name = name, Build = build, MaxDist = maxDist, Db = db, Variants = variants, PitchVar = pitch, VolVar = vol };
    static void AddLoop(string name, float maxDist, float db, float dur, Func<SynthRng, int, float[]> build, float overlap = 0.3f)
        => SfxRecipes.Table[name] = new Recipe { Name = name, Build = build, Loop = true, LoopOverlap = overlap, MaxDist = maxDist, Db = db, PitchVar = 0f, VolVar = 0f };

    public static void Register()
    {
        // The Plug: a slow wet groan with bubbling pops, heard a long way down the tunnel
        AddLoop(PlugCreep, 34, 1, 3.0f, (r, v) =>
        {
            var o = Gain(Filt(Noise(r, 3.0f), Ft.Lp, 160f, 1.2f), 1.4f);
            o = Mix(o, Am(Osc(Wf.Sine, 3.0f, t => 41f + 4f * MathF.Sin(2.1f * t)), 0.7f, 0.5f), 0, 0.9f);
            o = Mix(o, Am(Osc(Wf.Saw, 3.0f, 63f), 1.3f, 0.7f), 0, 0.12f);
            for (int i = 0; i < 9; i++) o = Mix(o, Burst(r, 0.05f, 180, 900, 45f), r.Range(0.05f, 2.9f), r.Range(0.1f, 0.35f));
            return o;
        });
        Add(PlugCrush, 28, 2, (r, v) =>
        {
            var o = Gain(Thump(70, 30, 0.7f, 5f), 1.0f);
            o = Mix(o, Filt(Burst(r, 0.5f, 120, 1500, 6f), Ft.Lp, 1200f), 0, 0.8f);
            for (int i = 0; i < 5; i++) o = Mix(o, Burst(r, 0.04f, 300, 2500, 50f), r.Range(0.02f, 0.4f), 0.4f);
            return Reverb(o, 0.5f, 0.25f);
        }, 2, 0.06f);
        // Wicks: a dry, many-voiced chitter
        AddLoop(WicksChitter, 36, -3, 1.6f, (r, v) =>
        {
            var o = Am(Gain(Filt(Noise(r, 1.6f), Ft.Bp, 5200f, 2.5f), 0.7f), 17f, 0.95f);
            o = Mix(o, Am(Osc(Wf.Sine, 1.6f, t => 3300f + 500f * MathF.Sin(31f * t)), 11f, 0.9f), 0, 0.12f);
            for (int i = 0; i < 14; i++) o = Mix(o, Bell(r.Range(3800f, 7500f), 0.08f, 40f), r.Range(0, 1.5f), r.Range(0.1f, 0.3f));
            return o;
        });
        Add(WicksDrain, 24, 0, (r, v) =>
        {
            var o = Filt(Filt(Noise(r, 1.0f), Ft.Hp, 2500f), Ft.Lp, 9000f);
            Env(o, t => MathF.Exp(-1.8f * t) * (0.55f + 0.45f * MathF.Sin(60f * t) * MathF.Sin(23f * t)));
            return Mix(Gain(o, 0.7f), Gain(Ad(Osc(Wf.Sine, 1.0f, t => 1800f - 900f * t), 0.02f, 0.4f), 0.15f), 0);
        });
        // The Lamplighter: a soft puff as the flame is pinched out, then the grab
        Add(LampSnuff, 22, 0, (r, v) =>
        {
            var o = Ad(Filt(Noise(r, 0.7f), Ft.Lp, t => 1400f - 900f * t, 0.9f), 0.01f, 0.18f);
            o = Mix(Gain(o, 0.9f), Thump(95, 50, 0.25f, 14f), 0, 0.35f);
            return Mix(o, Bell(2100, 0.2f, 22f), 0.02f, 0.1f);
        });
        Add(LampGrab, 24, 2, (r, v) =>
        {
            var o = Gain(Thump(120, 55, 0.35f, 9f), 0.8f);
            o = Mix(o, Burst(r, 0.25f, 200, 2200, 14f), 0, 0.7f);
            return Reverb(o, 0.5f, 0.2f);
        });
        // The Assessor: unhurried boots and a cane tick, a rubber stamp for the catch
        Add(AssessorStep, 26, -1, (r, v) =>
        {
            var o = Thump(170 + v * 15, 80, 0.14f, 22f);
            o = Mix(o, Burst(r, 0.1f, 500, 3500, 40f), 0, 0.5f);
            return Mix(o, Bell(1250 + v * 120, 0.12f, 45f, 0.8f), 0.07f, 0.3f);
        }, 3, 0.05f);
        Add(AssessorStamp, 30, 2, (r, v) =>
        {
            var o = Gain(Thump(110, 45, 0.45f, 8f), 1.0f);
            o = Mix(o, Burst(r, 0.2f, 300, 4000, 22f), 0, 0.6f);
            o = Mix(o, Bell(780, 0.9f, 7f), 0.05f, 0.15f);
            return Reverb(o, 0.7f, 0.25f);
        });
    }
}
