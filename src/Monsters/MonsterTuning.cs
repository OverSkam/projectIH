namespace AppStein;

/// <summary>All monster tuning in one place. Numbers are first guesses (metres, seconds, HP).</summary>
public static class MonsterTuning
{
    // ---- general
    public const float SyncInterval = 0.1f;        // position replication period (10 Hz)
    public const float SightDark = 9f;             // sight range vs a miner without a lit lantern
    public const float SightLantern = 24f;         // sight range vs a miner holding a lit lantern
    public const float LoseSightSeconds = 4f;
    public const float LeashDistance = 45f;        // chase gives up this far from home
    public const float CampBuffer = 1.5f;          // monsters keep this far outside the camp zone
    public const float DeadLinger = 2.2f;          // corpse stays this long before despawn

    // ---- spawning (monster markers)
    public const float SpawnChanceBase = 0.40f, SpawnChancePerRound = 0.10f, SpawnChancePerDepth = 0.08f;
    public const float GolemWeight = 5f;           // always eligible
    public const float EmberWeightLate = 2.5f;     // round >= 2 or depth >= 3
    public const float WispWeightLate = 2f;        // round >= 2
    public const float EarlyRareWeight = 0.4f;     // ember/wisp in round 1 at depth >= 2

    // ---- stone golem
    public const float GolemHp = 120f, GolemWalk = 1.5f, GolemInvestigate = 2.2f, GolemChase = 2.6f;
    public const float GolemTurn = 1.0f;           // rad/s: slow, so one miner can distract and another circle round
    public const float GolemSlamDamage = 45f, GolemSlamReach = 3.6f, GolemSlamArc = 0.95f; // half-angle rad
    public const float GolemWindup = 1.3f, GolemSlamTime = 0.25f, GolemRecover = 1.6f;
    public const float GolemFrontMul = 0.05f, GolemBackMul = 1.75f, GolemRearDot = -0.35f; // dot(forward, toAttacker) below this = back
    public const float GolemSightCos = 0.25f;      // ~75 degree half cone (except within 4 m)

    // ---- ember elemental
    public const float EmberHp = 20f, EmberWalk = 2.2f, EmberChase = 6.5f, EmberTurn = 10f;
    public const float EmberWindup = 0.4f, EmberLunge = 0.3f, EmberLungeSpeed = 10f, EmberRecover = 0.9f;
    public const float EmberLungeDamage = 10f, EmberAuraDamage = 3f, EmberAuraRadius = 1.2f, EmberAuraInterval = 0.8f;
    public const float EmberBurnSeconds = 3.5f;
    public const float EmberSightDark = 12f, EmberSightLantern = 22f; // it glows, so it sees well

    // ---- gas wisp
    public const float WispSpeed = 1.1f, WispInvestigate = 1.7f, WispRadius = 3.4f, WispHover = 1.3f;
    public const float WispGasDps = 7f, WispGasInterval = 0.5f;
    public const float WispSightDark = 7f, WispSightLantern = 13f, WispHearMul = 1.5f;

    // ---- dragon
    public const float DragonNoiseGain = 1.0f;     // meter += (loudness - 0.7*distance) * gain
    public const float DragonMeterMax = 100f, DragonStir = 25f, DragonEye = 55f, DragonDecay = 3f;
    public const float DragonAwakeSeconds = 18f;   // settles back after this long without breathing
    public const float DragonWindup = 1.8f, DragonBreathTime = 1.5f, DragonRecover = 1.4f;
    public const float DragonBreathRange = 18f, DragonBreathHalfAngle = 0.5f, DragonBreathDps = 55f;
    public const float DragonBreathBurn = 2.5f, DragonTunnelReach = 10f, DragonTurn = 1.6f, DragonWalk = 3f;
    public const int DragonHoardCount = 8;

    // ---- burning status (ember touch, dragon breath)
    public const float BurnDps = 4f;
}
