namespace AppStein;

/// <summary>All voice chat numbers in one place (metres, seconds, 0..1 levels). See docs/ARCHITECTURE.md, "Voice chat".</summary>
public static class VoiceTuning
{
    // ---- who hears whom (server decides, the listening client applies the falloff)
    /// <summary>Living miner: full volume up to this distance, then a falloff to silence at <see cref="NearMaxM"/>.</summary>
    public const float NearFullM = 6f, NearMaxM = 28f;
    /// <summary>Downed miner: quieter and shorter (crawling, whispering).</summary>
    public const float DownedFullM = 2f, DownedMaxM = 10f;
    public const float DownedGain = 0.6f;
    /// <summary>Ghosts hear the living from everywhere (they have no position the server knows); slightly lower so seven voices are not a wall.</summary>
    public const float GhostHearsLivingGain = 0.8f;

    // ---- wire format / abuse limits (server side)
    /// <summary>Largest accepted compressed voice frame in bytes (a Steam voice chunk of ~20-60 ms is 40-300 bytes; 1000 keeps frame, header and RPC overhead inside one unreliable packet (the Steam transport sends larger ones reliably)).</summary>
    public const int MaxFrameBytes = 1000;
    public const int HeaderBytes = 3;                  // seq lo, seq hi, level
    public const int DownHeaderBytes = 4;              // seq lo, seq hi, mode, level
    /// <summary>Sustained frame rate and burst allowed per client; the client sends at most 50 per second.</summary>
    public const double MaxFramesPerSec = 70, FrameBurst = 14;
    /// <summary>Sustained and burst bytes per second per client (about 4x what Steam's codec produces).</summary>
    public const double MaxBytesPerSec = 14000, ByteBurst = 5000;
    /// <summary>The client batches captured audio and sends at most one packet per this interval.</summary>
    public const double SendIntervalSecs = 0.02;
    /// <summary>Unreliable RPC channel used for voice (movement and everything else use channel 0).</summary>
    public const int Channel = 2;

    // ---- level (0..1, from the RMS of the decoded audio: level = rms / LevelFullScaleRms)
    public const float LevelFullScaleRms = 0.25f;
    /// <summary>Open mic: quieter than this level counts as silence (nothing is sent), with a short hang time so word endings are not cut.</summary>
    public const float OpenMicGate = 0.05f;
    public const double OpenMicHangSecs = 0.35;

    // ---- monsters hear voices
    /// <summary>Talking above this level makes noise the monsters can hear. A whisper (level below it) is silent.</summary>
    public const float NoiseLevelThreshold = 0.22f;
    /// <summary>Noise radius in metres: NoiseMinM at the threshold, NoiseMaxM at full level (for comparison: a geode hit 6-12, a sprint 8-20, dynamite 45).</summary>
    public const float NoiseMinM = 6f, NoiseMaxM = 14f;
    /// <summary>At most one noise event per speaker per this many seconds.</summary>
    public const double NoiseIntervalSecs = 0.7;

    // ---- playback
    /// <summary>Jitter buffer: playout starts after this much audio is queued, and a buffer above the max drops the oldest audio.</summary>
    public const int JitterTargetMs = 100, JitterMaxMs = 400;
    /// <summary>Audio kept queued in the engine's generator buffer (smaller = lower delay, larger = fewer underruns).</summary>
    public const int PlayQueueMs = 60;
    /// <summary>A talker counts as speaking this long after their last frame (speaker glyph).</summary>
    public const double SpeakingHoldSecs = 0.3;
    /// <summary>Fade on starting and stopping playback (anti-click), in milliseconds.</summary>
    public const int FadeMs = 3;
}
