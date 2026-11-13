using System;

namespace AppStein;

/// <summary>Match/round tuning. Add new presets here; Game uses Config.</summary>
public class MatchConfig
{
    public string Name = "default";
    public float BaseRoundSeconds = 360f;     // round 1: 6 min
    public float RoundSecondsGrowth = 60f;    // +1 min per round
    public float MaxRoundSeconds = 600f;      // ... up to 10 min
    // weighing ceremony timings (seconds)
    public float WeighIntroSeconds = 5f;      // lift leaves, miners are placed on the Hall floor
    public float RevealSeconds = 5f;          // per sack: reach (25%), lift (to 45%), then the count-up (RevealCountSeconds), then a beat
    public float RevealCountSeconds = 1.8f;
    public float VerdictSeconds = 4.5f;
    public float ThrowSeconds = 6f;           // lift + fall of the thrown miner(s)
    public float RemainSeconds = 5f;          // "N miners remain" card
    public float MatchEndSeconds = 15f;
    public float DownedSeconds = 30f;         // time a downed miner has before dying
    public float LanternOilSeconds = 240f;    // a full lantern burns about 4 minutes
    public int OilDrumRefills = 3;
    public bool AllowSolo = false;

    public float RoundSeconds(int round) => Math.Min(MaxRoundSeconds, BaseRoundSeconds + RoundSecondsGrowth * (round - 1));
    /// <summary>With N miners there are N-1 rounds.</summary>
    public static int RoundsFor(int miners) => Math.Max(1, miners - 1);

    public static MatchConfig Default => new MatchConfig();

    /// <summary>Short debug preset for tests: 60 s rounds, quick ceremony.</summary>
    public static MatchConfig Debug => new MatchConfig
    {
        Name = "debug",
        BaseRoundSeconds = 60f,
        RoundSecondsGrowth = 0f,
        WeighIntroSeconds = 2.5f, RevealSeconds = 2.2f, RevealCountSeconds = 0.8f, VerdictSeconds = 2f, ThrowSeconds = 3.6f, RemainSeconds = 2f,
        MatchEndSeconds = 6f,
        DownedSeconds = 10f,
        AllowSolo = true,
    };

    /// <summary>Full-game soak test: normal rules, short rounds.</summary>
    public static MatchConfig Soak => new MatchConfig
    {
        Name = "soak",
        BaseRoundSeconds = 105f,
        RoundSecondsGrowth = 0f,
        WeighIntroSeconds = 3f, RevealSeconds = 3f, RevealCountSeconds = 1f, VerdictSeconds = 2.5f, ThrowSeconds = 4f, RemainSeconds = 2.5f,
        MatchEndSeconds = 6f,
        AllowSolo = true,
    };

    public static MatchConfig FromName(string n) => n == "debug" ? Debug : n == "soak" ? Soak : Default;
}
