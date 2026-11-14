using System;
using System.Collections.Generic;
using static AppStein.Syn;
using static AppStein.SfxNames;

namespace AppStein;

/// <summary>Synthesised sounds of the v2.3 "other miners" step. Registered from SfxRecipes' static constructor; --sfxcheck verifies every one.</summary>
public static class SocialSfx
{
    static void Add(Dictionary<string, Recipe> t, string name, float maxDist, float db, Func<SynthRng, int, float[]> build, int variants = 1, float pitch = 0.04f, float vol = 1f, string bus = "SFX")
        => t[name] = new Recipe { Name = name, Build = build, MaxDist = maxDist, Db = db, Variants = variants, PitchVar = pitch, VolVar = vol, Bus = bus };

    public static void Register(Dictionary<string, Recipe> T)
    {
        // a two-handed push: a short cloth whoosh with a breath behind it
        Add(T, Shove, 20, -5, (r, v) =>
        {
            var w = Swell(Filt(Noise(r, 0.24f), Ft.Bp, t => 350f + 1100f * MathF.Sin(MathF.PI * t / 0.24f), 1.0f));
            return Mix(w, Voice(r, 0.2f, t => 150f - 40f * t, 520, 980, 0.35f), 0.05f, 0.35f);
        }, 2, 0.06f);

        // the push lands: a dull thump and a slap of jacket
        Add(T, ShoveHit, 24, -1, (r, v) =>
        {
            var o = Mix(Thump(120, 52, 0.22f, 16f), Burst(r, 0.14f, 150, 2600, 26f), 0, 0.7f);
            return Mix(o, Burst(r, 0.05f, 1200, 5000, 90f), 0, 0.3f);
        }, 2, 0.07f);

        // a stone knocked out of a sack: it hops and clatters on the rock (clear, quite loud, a metre or two of rolling)
        Add(T, StoneClatter, 30, -1, (r, v) =>
        {
            var o = Zero(0.9f);
            float[] hops = { 0f, 0.13f, 0.25f, 0.34f, 0.42f, 0.49f, 0.55f, 0.6f };
            for (int i = 0; i < hops.Length; i++)
            {
                float g = 1f / (1f + i * 0.5f);
                o = Mix(o, Burst(r, 0.05f, 900, 6500, 70f), hops[i], g);
                o = Mix(o, Bell(r.Range(1100f, 1900f), 0.14f, 38f, 0.7f), hops[i], g * 0.5f);
                o = Mix(o, Thump(260, 120, 0.06f, 60f), hops[i], g * 0.4f);
            }
            return Mix(o, Burst(r, 0.3f, 500, 3500, 9f), 0.6f, 0.15f);
        }, 2, 0.05f);

        // cloth rustle: someone is working at a sack. Soft and short ranged (heard from about 4 m).
        Add(T, Rustle, 5, -12, (r, v) =>
        {
            var x = Filt(Noise(r, 1.6f), Ft.Bp, t => 1600f + 700f * MathF.Sin(6.28f * 1.7f * t), 0.9f);
            Env(x, t => 0.25f + 0.75f * MathF.Abs(MathF.Sin(6.28f * 3.1f * t) * MathF.Sin(6.28f * 1.3f * t + 0.8f)));
            return Ar(Gain(x, 0.9f), 0.12f, 0.25f);
        }, 2, 0.04f, 0.8f);

        // a fumbled pickpocket: the whole sack clatters (loud)
        Add(T, PickFumble, 36, 0, (r, v) =>
        {
            var o = Zero(0.9f);
            for (int i = 0; i < 11; i++)
            {
                float at = r.Range(0f, 0.6f);
                o = Mix(o, Burst(r, 0.06f, 700, 6500, 55f), at, r.Range(0.4f, 1f));
                o = Mix(o, Bell(r.Range(900f, 2300f), 0.2f, 24f, 0.7f), at, r.Range(0.25f, 0.6f));
            }
            o = Mix(o, Thump(140, 60, 0.3f, 12f), 0, 0.8f);
            return Reverb(o, 0.3f, 0.18f);
        }, 2, 0.05f);

        // helped up: a gasp and a hand clap on the shoulder
        Add(T, Revive, 22, -4, (r, v) =>
        {
            var o = Voice(r, 0.4f, t => 120f + 90f * t / 0.4f, 600, 1100, 0.5f, 0.06f, 0.15f);
            return Mix(o, Mix(Thump(150, 70, 0.14f, 22f), Burst(r, 0.08f, 300, 3000, 40f), 0, 0.6f), 0.02f, 0.8f);
        });
    }
}
