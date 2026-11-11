using System;

namespace AppStein;

/// <summary>Match/round tuning. Add new presets here; Game uses Config.Current.</summary>
public class MatchConfig
{
    public string Name = "default";
    public int Rounds = 5;
    public float BaseRoundSeconds = 360f;     // round 1: 6 min
    public float RoundSecondsGrowth = 90f;    // round 5: 12 min
    public int QuotaPerPlayer = 100;          // round-1 quota = this * participants
    public float QuotaGrowth = 0.5f;          // +50% per later round
    public int QuotaFlat = 0;
    public float QuotaMissMoneyLoss = 0.20f;
    public int SurvivorBonus = 150;
    public float CurseHealthFraction = 0.6f;
    public float SummarySeconds = 12f;
    public float MatchEndSeconds = 15f;
    public bool AllowSolo = false;

    public float RoundSeconds(int round) => BaseRoundSeconds + RoundSecondsGrowth * (round - 1);
    public int Quota(int round, int participants) =>
        (int)Math.Round((QuotaFlat + QuotaPerPlayer * participants) * (1f + QuotaGrowth * (round - 1)));

    public static MatchConfig Default => new MatchConfig();

    public static MatchConfig Debug => new MatchConfig
    {
        Name = "debug",
        Rounds = 2,
        BaseRoundSeconds = 60f,
        RoundSecondsGrowth = 0f,
        QuotaPerPlayer = 0,
        QuotaFlat = 15,
        QuotaGrowth = 0f,
        SurvivorBonus = 100,
        SummarySeconds = 4f,
        MatchEndSeconds = 6f,
        AllowSolo = true,
    };

    public static MatchConfig FromName(string n) => n == "debug" ? Debug : Default;
}
