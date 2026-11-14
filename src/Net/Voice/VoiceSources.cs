using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Steamworks;

namespace AppStein;

/// <summary>Turns compressed voice data back into audio (what the listener plays and what the sender measures its own level with).</summary>
public interface IVoiceCodec
{
    int SampleRate { get; }
    /// <summary>Decodes one compressed chunk to mono samples in -1..1, or null when the data is not valid.</summary>
    float[] Decode(byte[] data, int length);
}

/// <summary>Microphone side: delivers compressed chunks.</summary>
public interface IVoiceSource
{
    string Name { get; }
    /// <summary>False when there is no capture at all (no Steam).</summary>
    bool Available { get; }
    bool Recording { get; }
    void Start();
    void Stop();
    /// <summary>Appends the compressed chunks captured since the last call (each already small enough to send). dt = seconds since the last call.</summary>
    void Poll(List<byte[]> into, double dt);
    /// <summary>Tells the platform that the local player is speaking (Steam shows the speaker icon next to the name on the friends list).</summary>
    void SetSpeaking(bool on);
}

// ====================================================================================== fakes (tests, screenshots)

/// <summary>Trivial codec for the tests: one byte per sample (signed, /127). 12 kHz, so a 20 ms frame is 240 bytes.</summary>
public sealed class FakeVoiceCodec : IVoiceCodec
{
    public const int Rate = 12000;
    public int SampleRate => Rate;
    public float[] Decode(byte[] data, int length)
    {
        if (data == null || length <= 0 || length > data.Length) return null;
        var r = new float[length];
        for (int i = 0; i < length; i++) r[i] = (sbyte)data[i] / 127f;
        return r;
    }
    public static byte[] Encode(float[] pcm)
    {
        var b = new byte[pcm.Length];
        for (int i = 0; i < pcm.Length; i++) b[i] = (byte)(sbyte)Math.Round(Math.Clamp(pcm[i], -1f, 1f) * 127f);
        return b;
    }
}

/// <summary>"Speech" for the tests: a two-tone sine with a syllable-rate envelope, produced in 20 ms frames while <see cref="Speak"/> says so.</summary>
public sealed class FakeVoiceSource : IVoiceSource
{
    public string Name => "fake";
    public bool Available => true;
    public bool Recording { get; private set; }
    public Func<bool> Speak = () => false;
    /// <summary>Peak amplitude 0..1. 0.25 is a normal speaking level (level ~0.7), 0.05 a whisper (level ~0.14).</summary>
    public float Amplitude = 0.25f;
    public int Spoken;
    double _acc, _phase, _syl;
    public void Start() { Recording = true; }
    public void Stop() { Recording = false; _acc = 0; }
    public void SetSpeaking(bool on) { }
    public void Poll(List<byte[]> into, double dt)
    {
        if (!Recording) return;
        if (!Speak()) { _acc = 0; return; }
        _acc += dt;
        const int frame = FakeVoiceCodec.Rate / 50;   // 20 ms
        int guard = 0;
        while (_acc >= 0.02 - 1e-9 && guard++ < 10)
        {
            _acc -= 0.02;
            var pcm = new float[frame];
            for (int i = 0; i < frame; i++)
            {
                _phase += 1.0 / FakeVoiceCodec.Rate; _syl += 4.0 / FakeVoiceCodec.Rate;
                float env = 0.8f + 0.2f * (float)Math.Sin(_syl * Math.Tau);
                pcm[i] = Amplitude * env * (0.7f * (float)Math.Sin(_phase * 220 * Math.Tau) + 0.3f * (float)Math.Sin(_phase * 330 * Math.Tau));
            }
            into.Add(FakeVoiceCodec.Encode(pcm));
            Spoken++;
        }
    }
}

// ====================================================================================== real Steam voice (never run against real Steam in development)

/// <summary>Steam's voice codec: SteamUser.DecompressVoice at the rate Steam recommends (usually 24 kHz).</summary>
public sealed class SteamVoiceCodec : IVoiceCodec
{
    public int SampleRate { get; }
    readonly byte[] _tmp;
    public SteamVoiceCodec()
    {
        uint r = 24000;
        try { r = OptimalRate(); } catch { }
        if (r < 11025 || r > 48000) r = 24000;
        SampleRate = (int)r;
        _tmp = new byte[SampleRate * 2];   // one second of 16-bit mono: far more than one chunk decodes to
    }

    [MethodImpl(MethodImplOptions.NoInlining)] static uint OptimalRate() => SteamUser.GetVoiceOptimalSampleRate();

    public float[] Decode(byte[] data, int length)
    {
        if (data == null || length <= 0 || length > data.Length) return null;
        try { return DecodeNative(data, length); }
        catch { return null; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    float[] DecodeNative(byte[] data, int length)
    {
        var res = SteamUser.DecompressVoice(data, (uint)length, _tmp, (uint)_tmp.Length, out uint written, (uint)SampleRate);
        if (res != EVoiceResult.k_EVoiceResultOK || written < 2) return null;
        int n = (int)(written / 2);
        var f = new float[n];
        for (int i = 0; i < n; i++) f[i] = (short)(_tmp[2 * i] | (_tmp[2 * i + 1] << 8)) / 32768f;
        return f;
    }
}

/// <summary>Steam microphone capture: StartVoiceRecording, then GetAvailableVoice / GetVoice(compressed) polled every frame.</summary>
public sealed class SteamVoiceSource : IVoiceSource
{
    public string Name => "steam";
    public bool Available => SteamService.Available;
    public bool Recording { get; private set; }
    readonly byte[] _buf = new byte[16384];
    bool _failedLogged;

    public void Start()
    {
        if (Recording || !Available) return;
        try { StartNative(); Recording = true; }
        catch (Exception e) { Log("start failed: " + e.Message); }
    }

    public void Stop()
    {
        if (!Recording) return;
        Recording = false;
        try { StopNative(); } catch (Exception e) { Log("stop failed: " + e.Message); }
    }

    public void Poll(List<byte[]> into, double dt)
    {
        if (!Recording) return;
        try
        {
            for (int guard = 0; guard < 8; guard++)
            {
                var chunk = PollNative();
                if (chunk == null) break;
                into.Add(chunk);
            }
        }
        catch (Exception e) { Log("capture failed: " + e.Message); }
    }

    public void SetSpeaking(bool on)
    {
        if (!Available) return;
        try { SpeakNative(on); } catch { }
    }

    void Log(string m) { if (_failedLogged) return; _failedLogged = true; Util.Log("VOICE: Steam " + m); }

    [MethodImpl(MethodImplOptions.NoInlining)] static void StartNative() => SteamUser.StartVoiceRecording();
    [MethodImpl(MethodImplOptions.NoInlining)] static void StopNative() => SteamUser.StopVoiceRecording();
    [MethodImpl(MethodImplOptions.NoInlining)] static void SpeakNative(bool on) => SteamFriends.SetInGameVoiceSpeaking(SteamUser.GetSteamID(), on);

    [MethodImpl(MethodImplOptions.NoInlining)]
    byte[] PollNative()
    {
        var r = SteamUser.GetAvailableVoice(out uint avail);
        if (r != EVoiceResult.k_EVoiceResultOK || avail == 0) return null;
        r = SteamUser.GetVoice(true, _buf, (uint)_buf.Length, out uint written);
        if (r != EVoiceResult.k_EVoiceResultOK || written == 0) return null;
        var c = new byte[written];
        Buffer.BlockCopy(_buf, 0, c, 0, (int)written);
        return c;
    }
}
