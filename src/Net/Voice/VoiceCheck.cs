using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>`-- --voicecheck`: headless self-check of the voice rules (falloff, who hears whom, noise, rate buckets), the jitter buffer and the fake codec. Exit code 1 on failure.</summary>
[ContentModule]
public static class VoiceTools
{
    public static void Register()
    {
        if (!Util.HasArg("voicecheck")) return;
        int fails = VoiceCheck.Run();
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree != null) tree.ProcessFrame += () => tree.Quit(fails == 0 ? 0 : 1);
    }
}

public static class VoiceCheck
{
    static int _fails, _checks;
    static void Ok(bool cond, string what) { _checks++; if (!cond) { _fails++; Util.Fail("voicecheck", what); } }
    static bool Near(float a, float b, float eps = 0.002f) => Math.Abs(a - b) <= eps;

    public static int Run()
    {
        _fails = 0; _checks = 0;
        Falloff(); Rules(); Noise(); Buckets(); Jitter(); Codec();
        Util.Log($"VOICECHECK: {_checks} checks, {_fails} failed");
        if (_fails == 0) Util.Pass("voicecheck", $"{_checks} checks");
        return _fails;
    }

    // ------------------------------------------------------------ falloff and gain
    static void Falloff()
    {
        float F(float d) => VoiceRules.Falloff(d, VoiceTuning.NearFullM, VoiceTuning.NearMaxM);
        Ok(F(0) == 1f && F(3) == 1f && F(6) == 1f, "full volume within 6 m");
        Ok(Near(F(17), 0.25f), $"17 m (halfway) is a quarter: {F(17)}");
        Ok(F(28) == 0f && F(40) == 0f, "silent from 28 m");
        float prev = 2f; bool mono = true;
        for (float d = 0; d <= 30; d += 0.5f) { float g = F(d); if (g > prev + 1e-6f) mono = false; prev = g; }
        Ok(mono, "falloff never rises with distance");
        Ok(Near(F(12), (float)Math.Pow(1 - 6f / 22f, 2)), "falloff shape at 12 m");
        Ok(VoiceRules.Gain(VoiceMode.Near, 4, false) == 1f && Near(VoiceRules.Gain(VoiceMode.Near, 17, false), 0.25f) && VoiceRules.Gain(VoiceMode.Near, 30, false) == 0f, "gain Near at 4 / 17 / 30 m");
        Ok(Near(VoiceRules.Gain(VoiceMode.DownedNear, 1, false), VoiceTuning.DownedGain) && VoiceRules.Gain(VoiceMode.DownedNear, 10, false) == 0f, "a downed miner is quieter and ends at 10 m");
        Ok(VoiceRules.Gain(VoiceMode.DownedNear, 5, false) < VoiceRules.Gain(VoiceMode.Near, 5, false) + 1e-6f && VoiceRules.Gain(VoiceMode.DownedNear, 5, false) < 0.6f, "downed at 5 m is quieter than a living voice at 5 m");
        Ok(VoiceRules.Gain(VoiceMode.Global, 0, false) == 1f && Near(VoiceRules.Gain(VoiceMode.Global, 0, true), VoiceTuning.GhostHearsLivingGain), "global voices: full, ghosts hear the living a little lower");
    }

    // ------------------------------------------------------------ who hears whom
    static bool Hears(bool lobby, bool cer, VoiceRole s, float sx, VoiceRole l, float lx, out VoiceMode m) =>
        VoiceRules.Decide(lobby, cer, s, new Vector3(sx, 0, 0), l, new Vector3(lx, 0, 0), out m);

    static void Rules()
    {
        const VoiceRole Liv = VoiceRole.Living, Dn = VoiceRole.Downed, Gh = VoiceRole.Ghost, Lob = VoiceRole.Lobby;
        Ok(Hears(false, false, Liv, 0, Liv, 4, out var m) && m == VoiceMode.Near, "round: a miner 4 m away is heard (positional)");
        Ok(Hears(false, false, Liv, 0, Liv, 27.9f, out _), "round: just inside 28 m is heard");
        Ok(!Hears(false, false, Liv, 0, Liv, 28.5f, out _), "round: beyond 28 m nothing is sent");
        Ok(!Hears(false, false, Liv, 0, Liv, 200, out _), "round: far away nothing is sent");
        Ok(Hears(false, false, Dn, 0, Liv, 9, out m) && m == VoiceMode.DownedNear, "round: a downed miner is heard within 10 m, quieter mode");
        Ok(!Hears(false, false, Dn, 0, Liv, 11, out _), "round: a downed miner is not heard beyond 10 m");
        Ok(Hears(false, false, Liv, 0, Dn, 20, out m) && m == VoiceMode.Near, "round: a downed miner still hears the living by normal range");
        Ok(Hears(false, false, Liv, 0, Gh, 500, out m) && m == VoiceMode.Global, "ghosts hear the living from anywhere (global)");
        Ok(Hears(false, false, Dn, 0, Gh, 500, out m) && m == VoiceMode.Global, "ghosts hear a downed miner too");
        Ok(!Hears(false, false, Gh, 0, Liv, 0.5f, out _), "the living never hear a ghost, even standing next to one");
        Ok(!Hears(false, false, Gh, 0, Dn, 0.5f, out _), "a downed miner does not hear ghosts either");
        Ok(Hears(false, false, Gh, 0, Gh, 500, out m) && m == VoiceMode.Global, "ghosts hear each other globally");
        // ceremony
        Ok(Hears(false, true, Liv, 0, Liv, 500, out m) && m == VoiceMode.Global, "ceremony: every living miner hears every living miner at full volume");
        Ok(Hears(false, true, Dn, 0, Liv, 500, out m) && m == VoiceMode.Global, "ceremony: includes downed");
        Ok(!Hears(false, true, Gh, 0, Liv, 0, out _), "ceremony: the living still do not hear ghosts");
        Ok(Hears(false, true, Liv, 0, Gh, 500, out _) && Hears(false, true, Gh, 0, Gh, 500, out _), "ceremony: ghosts hear the living and each other");
        // lobby / match end
        foreach (var a in new[] { Lob, Liv, Gh }) foreach (var b in new[] { Lob, Liv, Gh })
                Ok(Hears(true, false, a, 0, b, 900, out m) && m == VoiceMode.Global, $"lobby: {a} -> {b} everyone hears everyone, not positional");
    }

    static void Noise()
    {
        Ok(VoiceRules.NoiseRadius(0f) == 0f && VoiceRules.NoiseRadius(VoiceTuning.NoiseLevelThreshold - 0.01f) == 0f, "a whisper makes no noise");
        Ok(Near(VoiceRules.NoiseRadius(VoiceTuning.NoiseLevelThreshold), VoiceTuning.NoiseMinM), "talking at the threshold = minimum noise radius");
        Ok(Near(VoiceRules.NoiseRadius(1f), VoiceTuning.NoiseMaxM), "shouting = maximum noise radius");
        Ok(VoiceRules.NoiseRadius(0.5f) > VoiceTuning.NoiseMinM && VoiceRules.NoiseRadius(0.5f) < VoiceTuning.NoiseMaxM, "in between scales");
        Ok(VoiceRules.NoiseRadius(7f) == VoiceTuning.NoiseMaxM && VoiceRules.NoiseRadius(-1f) == 0f, "levels outside 0..1 are clamped");
        var pcm = Enumerable.Repeat(0.25f, 100).ToArray();
        Ok(Near(VoiceRules.LevelOf(pcm, 100), 1f) && Near(VoiceRules.LevelOf(pcm.Select(x => x * 0.1f).ToArray(), 100), 0.1f) && VoiceRules.LevelOf(pcm, 0) == 0f, "level = rms / full scale");
    }

    static void Buckets()
    {
        var b = new VoiceBucket(70, 14);
        int ok = 0; for (int i = 0; i < 100; i++) if (b.Take(1, 10.0)) ok++;
        Ok(ok == 14, $"a burst of 100 frames at one instant: exactly the burst of 14 passes ({ok})");
        Ok(!b.Take(1, 10.0), "still empty at the same instant");
        Ok(b.Take(1, 10.0 + 1.0 / 70 + 0.001), "one frame is allowed again after 1/70 s");
        int steady = 0; double t = 20; var b2 = new VoiceBucket(70, 14);
        for (int i = 0; i < 500; i++) { t += 0.02; if (b2.Take(1, t)) steady++; }   // a normal 50 frames/s stream
        Ok(steady == 500, "a normal 50 frames per second stream is never limited");
        var by = new VoiceBucket(14000, 5000); Ok(by.Take(5000, 0) && !by.Take(1000, 0) && by.Take(1000, 0.1), "byte bucket: burst then refill");
    }

    // ------------------------------------------------------------ jitter buffer
    const int R = 12000, FR = 240;   // 12 kHz, 20 ms frames
    static float[] Frame(float v) { var f = new float[FR]; Array.Fill(f, v); return f; }
    static JitterBuffer NewJb() => new(R, 100, 400, 3);

    static void Jitter()
    {
        // in order: output is the frames in order (after the 3 ms fade-in)
        var jb = NewJb();
        for (ushort i = 0; i < 10; i++) jb.Push(i, Frame((i + 1) / 100f));
        var o = new float[FR * 10];
        int real = jb.Pop(o, o.Length);
        Ok(real == FR * 10, "in order: all samples real");
        bool seq = true; for (int k = 0; k < 10; k++) if (!Near(o[k * FR + 100], (k + 1) / 100f, 1e-5f)) seq = false;
        Ok(seq, "in order: frames come out in order");
        Ok(o[0] == 0f && o[10] > 0f && o[10] < o[30] && Near(o[60], 0.01f, 1e-5f), "playout starts with a short fade-in (no click)");
        Ok(jb.Played == FR * 10 && jb.Late == 0 && jb.Lost == 0 && jb.Duplicates == 0, "in order: nothing lost or late");

        // waits for the target before playing, and a short utterance is released by ForceStart
        jb = NewJb(); jb.Push(0, Frame(0.2f)); jb.Push(1, Frame(0.2f));
        o = new float[FR];
        Ok(jb.Pop(o, FR) == 0 && jb.Idle && jb.Buffering, "below the target: silence, buffering");
        jb.ForceStart();
        Ok(jb.Pop(o, FR) == FR, "ForceStart plays a short utterance anyway");

        // reorder within the buffer
        jb = NewJb();
        foreach (ushort i in new ushort[] { 0, 2, 1, 4, 3, 5 }) jb.Push(i, Frame((i + 1) / 100f));
        o = new float[FR * 6]; jb.Pop(o, o.Length);
        seq = true; for (int k = 0; k < 6; k++) if (!Near(o[k * FR + 100], (k + 1) / 100f, 1e-5f)) seq = false;
        Ok(seq && jb.Lost == 0, "reordered frames are put back in order");

        // loss: frame 2 never arrives
        jb = NewJb();
        foreach (ushort i in new ushort[] { 0, 1, 3, 4, 5 }) jb.Push(i, Frame((i + 1) / 100f));
        o = new float[FR * 5]; real = jb.Pop(o, o.Length);
        Ok(real == FR * 5 && jb.Lost == 1 && Near(o[2 * FR + 100], 0.04f, 1e-5f), "a missing frame is skipped and counted, playback goes on");

        // late and duplicate
        jb = NewJb();
        for (ushort i = 0; i < 6; i++) jb.Push(i, Frame(0.1f));
        o = new float[FR * 3]; jb.Pop(o, o.Length);
        Ok(!jb.Push(1, Frame(0.1f)) && jb.Late == 1, "a frame that arrives after its turn is dropped");
        Ok(!jb.Push(4, Frame(0.1f)) && jb.Duplicates == 1, "a duplicate is dropped");

        // sequence wrap-around, also reordered across the wrap
        jb = NewJb();
        foreach (ushort i in new ushort[] { 65533, 65534, 0, 65535, 1, 2 }) jb.Push(i, Frame(((i + 3) % 65536) % 100 / 100f + 0.01f));
        o = new float[FR * 6]; jb.Pop(o, o.Length);
        float[] expect = new[] { 65533, 65534, 65535, 0, 1, 2 }.Select(x => (int)((x + 3) % 65536) % 100 / 100f + 0.01f).ToArray();
        seq = true; for (int k = 0; k < 6; k++) if (!Near(o[k * FR + 100], expect[k], 1e-5f)) seq = false;
        Ok(seq && jb.Late == 0, "sequence numbers wrap around 65535 -> 0 in order");

        // underrun: runs dry -> fade of the last sample (no step), silence after, buffering again
        jb = NewJb();
        for (ushort i = 0; i < 5; i++) jb.Push(i, Frame(0.5f));
        o = new float[FR * 5 + 100]; real = jb.Pop(o, o.Length);
        Ok(real == FR * 5 && jb.Underruns == 1, "underrun counted when the buffer runs dry");
        float maxStep = 0; for (int i = FR * 5 - 40; i < o.Length; i++) maxStep = Math.Max(maxStep, Math.Abs(o[i] - o[i - 1]));
        Ok(maxStep < 0.5f / 36 + 1e-4f, $"running dry fades out instead of a click (max step {maxStep:0.0000})");
        Ok(o[FR * 5] > 0.45f && o[FR * 5 + 36] == 0f && o[o.Length - 1] == 0f, "the fade ends in silence");
        Ok(jb.Buffering && jb.Idle, "after running dry the buffer is idle");
        // resume: fades in again, only after the target is queued again
        for (ushort i = 5; i < 8; i++) jb.Push(i, Frame(0.5f));
        o = new float[FR]; Ok(jb.Pop(o, FR) == 0, "resuming waits for the target again (3 frames is not enough)");
        for (ushort i = 8; i < 11; i++) jb.Push(i, Frame(0.5f));
        o = new float[FR]; jb.Pop(o, FR);
        Ok(o[0] == 0f && o[5] > o[1] && Near(o[100], 0.5f, 1e-5f), "resuming fades in");

        // overrun: 600 ms arrive at once, the oldest are dropped, the newest are kept, the delay is bounded
        jb = NewJb();
        for (ushort i = 0; i < 30; i++) jb.Push(i, Frame((i + 1) / 100f));
        Ok(jb.Overruns >= 1 && jb.QueuedSamples <= R * 400 / 1000, $"overrun: queue bounded to the maximum ({jb.QueuedSamples} samples)");
        o = new float[FR * 30]; real = jb.Pop(o, o.Length);
        float lastVal = 0; for (int i = 0; i < real; i++) if (o[i] != 0f) lastVal = o[i];
        Ok(Near(lastVal, 0.30f, 1e-5f) && real <= R * 400 / 1000, "overrun: the newest audio survives");
        // a long run of lost / reordered / duplicated packets never crashes and never returns garbage
        var rnd = new Random(5); jb = NewJb(); bool finite = true; var buf = new float[300];
        for (int i = 0; i < 3000; i++)
        {
            if (rnd.NextDouble() < 0.7) jb.Push((ushort)(i + rnd.Next(-3, 4)), Frame((float)(rnd.NextDouble() * 2 - 1)));
            jb.Pop(buf, 100 + rnd.Next(200));
            foreach (var x in buf) if (float.IsNaN(x) || Math.Abs(x) > 1.0001f) finite = false;
        }
        Ok(finite, "random loss / reorder / duplicates: output stays finite and in range");
    }

    // ------------------------------------------------------------ fake codec / source
    static void Codec()
    {
        var c = new FakeVoiceCodec();
        var pcm = Enumerable.Range(0, 240).Select(i => (float)Math.Sin(i * 0.1) * 0.8f).ToArray();
        var enc = FakeVoiceCodec.Encode(pcm); var dec = c.Decode(enc, enc.Length);
        Ok(enc.Length == 240 && dec.Length == 240 && dec.Zip(pcm, (a, b) => Math.Abs(a - b)).Max() < 0.01f, "fake codec round trip within 1%");
        Ok(c.Decode(null, 5) == null && c.Decode(enc, 0) == null && c.Decode(enc, 999) == null, "fake codec rejects bad input");
        var src = new FakeVoiceSource { Amplitude = 0.25f, Speak = () => true };
        src.Start(); var got = new List<byte[]>(); src.Poll(got, 0.1);
        Ok(got.Count == 5 && got.All(g => g.Length == 240), "fake source makes 20 ms frames (5 in 100 ms)");
        float lv = VoiceRules.LevelOf(c.Decode(got[2], 240), 240);
        Ok(lv > VoiceTuning.NoiseLevelThreshold && lv > VoiceTuning.OpenMicGate, $"fake speech level {lv:0.00} is above the noise threshold");
        var whisper = new FakeVoiceSource { Amplitude = 0.05f, Speak = () => true }; whisper.Start(); got.Clear(); whisper.Poll(got, 0.1);
        float wl = VoiceRules.LevelOf(c.Decode(got[2], 240), 240);
        Ok(wl < VoiceTuning.NoiseLevelThreshold && wl > VoiceTuning.OpenMicGate, $"fake whisper level {wl:0.00} is above the open-mic gate but below the noise threshold");
        var silent = new FakeVoiceSource { Speak = () => false }; silent.Start(); got.Clear(); silent.Poll(got, 0.1);
        Ok(got.Count == 0, "silent fake source produces nothing");
    }
}
