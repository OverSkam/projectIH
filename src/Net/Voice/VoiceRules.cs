using Godot;

namespace AppStein;

/// <summary>What a player is, as far as hearing goes.</summary>
public enum VoiceRole { Lobby, Living, Downed, Ghost }

/// <summary>How a delivered voice frame is played at the listener (also the mode byte on the wire).</summary>
public enum VoiceMode : byte { Global = 0, Near = 1, DownedNear = 2 }

/// <summary>Pure rules: who hears whom and how loud. No engine objects, so `--voicecheck` can test them and the server and the clients share them.</summary>
public static class VoiceRules
{
    /// <summary>Volume 0..1 at distance d: full up to <paramref name="full"/>, silent from <paramref name="max"/>, a squared fade in between.</summary>
    public static float Falloff(float d, float full, float max)
    {
        if (d <= full) return 1f;
        if (d >= max) return 0f;
        float t = (d - full) / (max - full);
        return (1f - t) * (1f - t);
    }

    public static float Range(VoiceMode m) => m == VoiceMode.DownedNear ? VoiceTuning.DownedMaxM : VoiceTuning.NearMaxM;

    /// <summary>Gain the listening client applies to a frame that came with this mode, for the given distance between speaker head and listener.</summary>
    public static float Gain(VoiceMode mode, float distance, bool listenerIsGhost)
    {
        switch (mode)
        {
            case VoiceMode.Near: return Falloff(distance, VoiceTuning.NearFullM, VoiceTuning.NearMaxM);
            case VoiceMode.DownedNear: return VoiceTuning.DownedGain * Falloff(distance, VoiceTuning.DownedFullM, VoiceTuning.DownedMaxM);
            default: return listenerIsGhost ? VoiceTuning.GhostHearsLivingGain : 1f;
        }
    }

    /// <summary>The server's decision for one (speaker, listener) pair. deliver = false means the listener gets nothing at all.
    /// lobbyPhase: lobby and match-end screen (everyone hears everyone). ceremony: the weighing in the Hall (the living hear all the living, full volume).</summary>
    public static bool Decide(bool lobbyPhase, bool ceremony, VoiceRole speaker, Vector3 speakerPos, VoiceRole listener, Vector3 listenerPos, out VoiceMode mode)
    {
        mode = VoiceMode.Global;
        if (lobbyPhase) return true;                                   // lobby: everybody hears everybody, not positional
        bool sLive = speaker == VoiceRole.Living || speaker == VoiceRole.Downed;
        bool lLive = listener == VoiceRole.Living || listener == VoiceRole.Downed;
        if (!sLive) return !lLive;                                      // ghosts talk to ghosts, never to the living
        if (!lLive) return true;                                        // ghosts hear the living (global: the server does not know where a ghost floats)
        if (ceremony) return true;                                      // the accusation scene: everyone alive hears everyone alive
        float range = speaker == VoiceRole.Downed ? VoiceTuning.DownedMaxM : VoiceTuning.NearMaxM;
        if ((speakerPos - listenerPos).Length() > range) return false;  // too far: not sent (and not heard)
        mode = speaker == VoiceRole.Downed ? VoiceMode.DownedNear : VoiceMode.Near;
        return true;
    }

    /// <summary>Monsters hear loud talking: radius in metres for a level 0..1, or 0 when it is only a whisper.</summary>
    public static float NoiseRadius(float level)
    {
        if (level < VoiceTuning.NoiseLevelThreshold) return 0f;
        float t = Mathf.Clamp((level - VoiceTuning.NoiseLevelThreshold) / (1f - VoiceTuning.NoiseLevelThreshold), 0f, 1f);
        return Mathf.Lerp(VoiceTuning.NoiseMinM, VoiceTuning.NoiseMaxM, t);
    }

    /// <summary>Level 0..1 of a block of audio (RMS scaled so normal speech lands around 0.4-0.8).</summary>
    public static float LevelOf(float[] pcm, int count)
    {
        if (count <= 0) return 0f;
        double sum = 0;
        for (int i = 0; i < count; i++) sum += pcm[i] * pcm[i];
        return Mathf.Clamp((float)System.Math.Sqrt(sum / count) / VoiceTuning.LevelFullScaleRms, 0f, 1f);
    }
}

/// <summary>Token bucket (frames or bytes per second with a burst) for the per-client voice limits.</summary>
public sealed class VoiceBucket
{
    readonly double _rate, _cap;
    double _tokens, _last = -1;
    public VoiceBucket(double ratePerSec, double burst) { _rate = ratePerSec; _cap = burst; _tokens = burst; }
    public bool Take(double cost, double now)
    {
        if (_last < 0) _last = now;
        _tokens = System.Math.Min(_cap, _tokens + (now - _last) * _rate);
        _last = now;
        if (_tokens < cost) return false;
        _tokens -= cost;
        return true;
    }
}
