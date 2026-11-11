using System;

namespace AppStein;

/// <summary>Deterministic xorshift noise source (one per recipe, seeded from the sound name).</summary>
public sealed class SynthRng
{
    uint _s;
    public SynthRng(uint seed) { _s = seed * 2654435761u + 0x9E3779B9u; if (_s == 0) _s = 1; }
    /// <summary>Uniform in [-1, 1).</summary>
    public float Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return (_s & 0xFFFFFF) / (float)0x800000 - 1f; }
    /// <summary>Uniform in [0, 1).</summary>
    public float U() => (Next() + 1f) * 0.5f;
    public float Range(float a, float b) => a + (b - a) * U();
    public static uint Hash(string s) { uint h = 2166136261u; foreach (var c in s) { h ^= c; h *= 16777619u; } return h; }
}

public enum Wf { Sine, Tri, Saw, Square }
public enum Ft { Lp, Hp, Bp }

/// <summary>Small offline synth toolkit. All buffers are mono float[] at <see cref="SR"/>; nothing here touches the engine.</summary>
public static class Syn
{
    public const int SR = 22050;
    const float Tau = 6.2831853f;

    public static int N(float sec) => Math.Max(1, (int)(sec * SR));
    public static float[] Zero(float sec) => new float[N(sec)];

    // ------------------------------------------------------------ sources
    public static float[] Osc(Wf w, float dur, Func<float, float> hz, Func<float, float> amp = null)
    {
        var o = new float[N(dur)];
        double ph = 0;
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)SR;
            ph += hz(t) / SR; ph -= Math.Floor(ph);
            float p = (float)ph, v;
            switch (w)
            {
                case Wf.Sine: v = MathF.Sin(Tau * p); break;
                case Wf.Tri: v = 4f * MathF.Abs(p - 0.5f) - 1f; break;
                case Wf.Saw: v = 2f * p - 1f; break;
                default: v = p < 0.5f ? 1f : -1f; break;
            }
            o[i] = amp == null ? v : v * amp(t);
        }
        return o;
    }
    public static float[] Osc(Wf w, float dur, float hz) => Osc(w, dur, t => hz);

    public static float[] Noise(SynthRng r, float dur)
    {
        var o = new float[N(dur)];
        for (int i = 0; i < o.Length; i++) o[i] = r.Next();
        return o;
    }

    /// <summary>Exponential pitch drop sine: f1 + (f0-f1)*exp(-k t), times exp(-decay t).</summary>
    public static float[] Thump(float f0, float f1, float dur, float decay, float k = 12f)
    {
        var o = Osc(Wf.Sine, dur, t => f1 + (f0 - f1) * MathF.Exp(-k * t));
        return Exp(o, decay);
    }

    public static float[] Tone(Wf w, float hz, float dur, float decay) => Exp(Osc(w, dur, hz), decay);

    /// <summary>Band-limited noise burst with exponential decay.</summary>
    public static float[] Burst(SynthRng r, float dur, float lo, float hi, float decay)
    {
        var n = Noise(r, dur);
        if (hi > 0) n = Filt(n, Ft.Lp, hi);
        if (lo > 0) n = Filt(n, Ft.Hp, lo);
        return Exp(n, decay);
    }

    /// <summary>Struck-metal / bell: inharmonic partials, higher ones die faster.</summary>
    public static float[] Bell(float f, float dur, float decay, float bright = 1f)
    {
        float[] ratios = { 1f, 2.76f, 5.40f, 8.93f };
        float[] amps = { 1f, 0.5f, 0.28f, 0.14f };
        var o = new float[N(dur)];
        for (int p = 0; p < ratios.Length; p++)
        {
            float fp = f * ratios[p]; if (fp > SR * 0.45f) continue;
            float a = amps[p] * (p == 0 ? 1f : bright), d = decay * (1f + p * 0.9f);
            for (int i = 0; i < o.Length; i++) { float t = i / (float)SR; o[i] += a * MathF.Sin(Tau * fp * t) * MathF.Exp(-d * t); }
        }
        return o;
    }

    /// <summary>Crude vowel: sawtooth through two formant band-passes plus breath noise.</summary>
    public static float[] Voice(SynthRng r, float dur, Func<float, float> f0, float f1, float f2, float breath = 0.15f, float att = 0.03f, float rel = 0.08f)
    {
        var s = Osc(Wf.Saw, dur, f0);
        var a = Filt(s, Ft.Bp, f1, 4f);
        var b = Filt(s, Ft.Bp, f2, 5f);
        var n = Filt(Noise(r, dur), Ft.Bp, f2, 1.5f);
        var o = new float[s.Length];
        for (int i = 0; i < o.Length; i++)
        {
            float t = i / (float)SR;
            float e = MathF.Min(1f, t / att) * MathF.Min(1f, MathF.Max(0f, (dur - t) / rel));
            o[i] = (a[i] * 1.2f + b[i] * 0.7f + n[i] * breath) * e;
        }
        return o;
    }

    // ------------------------------------------------------------ filters (RBJ biquad, optionally time-varying)
    public static float[] Filt(float[] x, Ft kind, float fc, float q = 0.707f) => Filt(x, kind, t => fc, q);
    public static float[] Filt(float[] x, Ft kind, Func<float, float> fc, float q = 0.707f)
    {
        var y = new float[x.Length];
        float b0 = 0, b1 = 0, b2 = 0, a1 = 0, a2 = 0, x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < x.Length; i++)
        {
            if ((i & 7) == 0)
            {
                float f = Math.Clamp(fc(i / (float)SR), 20f, SR * 0.45f);
                float w = Tau * f / SR, cs = MathF.Cos(w), al = MathF.Sin(w) / (2f * q);
                float a0 = 1f + al;
                a1 = -2f * cs / a0; a2 = (1f - al) / a0;
                switch (kind)
                {
                    case Ft.Lp: b0 = (1f - cs) / 2f / a0; b1 = (1f - cs) / a0; b2 = b0; break;
                    case Ft.Hp: b0 = (1f + cs) / 2f / a0; b1 = -(1f + cs) / a0; b2 = b0; break;
                    default: b0 = al / a0; b1 = 0; b2 = -al / a0; break;
                }
            }
            float v = b0 * x[i] + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x[i]; y2 = y1; y1 = v; y[i] = v;
        }
        return y;
    }

    // ------------------------------------------------------------ envelopes / shaping (in place, return the same array)
    public static float[] Env(float[] x, Func<float, float> e)
    {
        for (int i = 0; i < x.Length; i++) x[i] *= e(i / (float)SR);
        return x;
    }
    public static float[] Exp(float[] x, float decay) { if (decay > 0) Env(x, t => MathF.Exp(-decay * t)); return x; }
    /// <summary>Linear attack then exponential decay (time constant d).</summary>
    public static float[] Ad(float[] x, float a, float d) => Env(x, t => (a <= 0 ? 1f : MathF.Min(1f, t / a)) * MathF.Exp(-MathF.Max(0, t - a) / d));
    /// <summary>Attack / sustain / release over the whole buffer.</summary>
    public static float[] Ar(float[] x, float a, float rel)
    {
        float dur = x.Length / (float)SR;
        return Env(x, t => MathF.Min(1f, t / MathF.Max(a, 1e-3f)) * MathF.Min(1f, MathF.Max(0f, (dur - t) / MathF.Max(rel, 1e-3f))));
    }
    /// <summary>Half-sine swell over the whole buffer.</summary>
    public static float[] Swell(float[] x) { float d = x.Length / (float)SR; return Env(x, t => MathF.Sin(MathF.PI * t / d)); }
    public static float[] Gain(float[] x, float g) { for (int i = 0; i < x.Length; i++) x[i] *= g; return x; }
    public static float[] Am(float[] x, float rate, float depth) => Env(x, t => 1f - depth + depth * (0.5f + 0.5f * MathF.Sin(Tau * rate * t)));
    public static float[] Dist(float[] x, float drive)
    {
        float n = MathF.Tanh(drive);
        for (int i = 0; i < x.Length; i++) x[i] = MathF.Tanh(x[i] * drive) / n;
        return x;
    }

    // ------------------------------------------------------------ mixing
    /// <summary>Adds src into dst starting at atSec. Returns dst, or a longer copy if src runs past the end.</summary>
    public static float[] Mix(float[] dst, float[] src, float atSec, float gain = 1f)
    {
        int off = Math.Max(0, (int)(atSec * SR));
        if (off + src.Length > dst.Length) Array.Resize(ref dst, off + src.Length);
        for (int i = 0; i < src.Length; i++) dst[off + i] += src[i] * gain;
        return dst;
    }

    public static float[] Echo(float[] x, float delay, float fb, int taps)
    {
        int d = Math.Max(1, (int)(delay * SR));
        var o = new float[x.Length + d * taps];
        float g = 1f;
        for (int k = 0; k <= taps; k++, g *= fb)
            for (int i = 0; i < x.Length; i++) o[i + k * d] += x[i] * g;
        return o;
    }

    /// <summary>Schroeder reverb (4 damped combs + 2 allpass). Extends the buffer by tail seconds.</summary>
    public static float[] Reverb(float[] x, float tail, float wet, float size = 1f)
    {
        int len = x.Length + N(tail);
        var inp = new float[len]; Array.Copy(x, inp, x.Length);
        float[] cd = { 0.0297f, 0.0371f, 0.0411f, 0.0437f };
        var acc = new float[len];
        for (int c = 0; c < cd.Length; c++)
        {
            int d = Math.Max(1, (int)(cd[c] * size * SR));
            var buf = new float[d]; int p = 0; float lp = 0;
            float fb = 0.84f + 0.03f * c / 3f;
            for (int i = 0; i < len; i++)
            {
                float y = buf[p];
                lp += (y - lp) * 0.35f;
                buf[p] = inp[i] + lp * fb;
                if (++p == d) p = 0;
                acc[i] += y * 0.25f;
            }
        }
        foreach (float ad in new[] { 0.005f, 0.0017f })
        {
            int d = Math.Max(1, (int)(ad * SR));
            var buf = new float[d]; int p = 0;
            for (int i = 0; i < len; i++)
            {
                float z = buf[p], xin = acc[i];
                buf[p] = xin + 0.5f * z;
                acc[i] = z - 0.5f * xin;
                if (++p == d) p = 0;
            }
        }
        for (int i = 0; i < len; i++) inp[i] += acc[i] * wet * 2.5f;
        return inp;
    }

    // ------------------------------------------------------------ finishing
    /// <summary>Makes a seamless loop: result is (len - overlap) long, tail crossfaded into the head.</summary>
    public static float[] MakeLoop(float[] x, float overlapSec)
    {
        int ov = Math.Min(N(overlapSec), x.Length / 3), L = x.Length - ov;
        var o = new float[L];
        Array.Copy(x, o, L);
        for (int i = 0; i < ov; i++)
        {
            float th = MathF.PI * 0.5f * i / ov;
            o[i] = x[i] * MathF.Sin(th) + x[L + i] * MathF.Cos(th);
        }
        return o;
    }

    /// <summary>Removes DC, fades the ends (one-shots), normalises the peak to peakDb (default -3 dBFS).</summary>
    public static float[] Finish(float[] x, bool loop, float peakDb = -3f)
    {
        var o = (float[])x.Clone();
        for (int i = 0; i < o.Length; i++) if (!float.IsFinite(o[i])) o[i] = 0f;
        var win = new float[o.Length];
        Array.Fill(win, 1f);
        if (!loop)
        {
            int fi = Math.Min(N(0.002f), o.Length / 4), fo = Math.Min(N(0.012f), o.Length / 3);
            for (int i = 0; i < fi; i++) win[i] = i / (float)fi;
            for (int i = 0; i < fo; i++) win[o.Length - 1 - i] = MathF.Min(win[o.Length - 1 - i], i / (float)fo);
        }
        for (int i = 0; i < o.Length; i++) o[i] *= win[i];
        for (int it = 0; it < 4; it++)   // remove DC while keeping the faded ends at zero
        {
            double m = 0; foreach (var v in o) m += v; m /= Math.Max(1, o.Length);
            for (int i = 0; i < o.Length; i++) o[i] -= (float)m * win[i];
        }
        float pk = 0; foreach (var v in o) pk = MathF.Max(pk, MathF.Abs(v));
        if (pk > 1e-9f) { float g = MathF.Pow(10f, peakDb / 20f) / pk; for (int i = 0; i < o.Length; i++) o[i] *= g; }
        return o;
    }

    public static byte[] ToPcm16(float[] x)
    {
        var b = new byte[x.Length * 2];
        for (int i = 0; i < x.Length; i++)
        {
            short s = (short)Math.Clamp((int)MathF.Round(x[i] * 32767f), -32767, 32767);
            b[2 * i] = (byte)(s & 0xff); b[2 * i + 1] = (byte)((s >> 8) & 0xff);
        }
        return b;
    }

    public static byte[] WavFile(float[] x)
    {
        var pcm = ToPcm16(x);
        using var ms = new System.IO.MemoryStream();
        using var w = new System.IO.BinaryWriter(ms);
        w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + pcm.Length); w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
        w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(SR); w.Write(SR * 2); w.Write((short)2); w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(pcm.Length); w.Write(pcm);
        return ms.ToArray();
    }

    public struct Stats { public float Peak, Rms, Dc; public bool Nan; public float Dur; }
    public static Stats Analyse(float[] x)
    {
        var s = new Stats { Dur = x.Length / (float)SR };
        double sum = 0, sq = 0;
        foreach (var v in x) { if (!float.IsFinite(v)) { s.Nan = true; continue; } sum += v; sq += v * v; s.Peak = MathF.Max(s.Peak, MathF.Abs(v)); }
        s.Rms = (float)Math.Sqrt(sq / Math.Max(1, x.Length)); s.Dc = (float)(sum / Math.Max(1, x.Length));
        return s;
    }
}
