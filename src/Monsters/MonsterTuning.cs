namespace AppStein;

/// <summary>All monster tuning in one place. Numbers are first guesses (metres, seconds, HP).</summary>
public static class MonsterTuning
{
    // ---- general
    public const float SyncInterval = 0.1f;        // position replication period (10 Hz)
    public const float SightDark = 9f;             // (legacy) sight range vs a miner without a lit lantern
    public const float SightLantern = 28f;         // sight range (line of sight) vs a miner holding a lit lantern
    public const float DetectStill = 2f;           // miner without a lit lantern, standing still: noticed only this close
    public const float DetectWalk = 5.5f;          // ... walking
    public const float DetectSprint = 9f;          // ... sprinting
    public const float StillSpeed = 0.6f, SprintSpeedThreshold = 6.5f;   // m/s measured on the server from reported positions
    public const float LoseSightSeconds = 5f;      // chase: seconds without sense contact before it gives up and searches the last known spot
    public const float SearchSeconds = 6f;         // looks around at the last known spot, then resumes patrol
    public const float HallBuffer = 0.5f;          // monsters keep this far outside the Hall zone
    public const float PatrolMinPath = 18f;        // a patrol goal is at least this far (path metres) from where the monster stands

    // ---- density director
    public const int BaseRoamers = 2;              // roaming monsters at round start = BaseRoamers + miners/2 + round (cap RoamCap)
    public const int RoamCap = 12;
    public const float SpawnHallPath = 40f;        // spawn at least this many path metres from the Hall
    public const float SpawnSpacing = 14f;         // and this far (straight) from each other
    public const float DirectorCheck = 40f;        // seconds between director checks
    public const float DirectorQuiet = 60f;        // no miner had a monster within EncounterRadius for this long -> send one
    public const float EncounterRadius = 15f;
    public const float DirectorScatter = 10f;      // the sent monster aims this far (random) from the miner, not at the exact spot
    /// <summary>Roaming monsters at round start for this miner count and round (the director never exceeds it).</summary>
    public static int RoamTarget(int miners, int round) => System.Math.Min(RoamCap, BaseRoamers + miners / 2 + round);
    /// <summary>Kinds guaranteed per round (before the rest is filled with weighted picks). Round 1: golems, a Lamplighter and Wicks.</summary>
    public static (string kind, int minRound, int perRound)[] Guaranteed = {
        ("lamplighter", 1, 1), ("wicks", 1, 1), ("plug", 2, 1), ("assessor", 2, 1), ("wicks", 3, 1), ("lamplighter", 4, 1), ("plug", 4, 1),
    };
    /// <summary>Weights for the remaining slots, by round (index = round-1, clamped): golem, wicks, lamplighter, plug.</summary>
    public static (string kind, float[] w)[] FillWeights = {
        ("golem", new[] { 6f, 5f, 5f, 4f, 4f }), ("wicks", new[] { 2f, 2f, 3f, 3f, 3f }),
        ("lamplighter", new[] { 1f, 1.5f, 2f, 2f, 2f }), ("plug", new[] { 0f, 1f, 1.5f, 2f, 2f }),
    };
    public const int MaxAssessors = 2;

    /// <summary>Upper bound of roaming monsters in any round (Soak asserts against it).</summary>
    public static int MaxTotal(int round) => RoamCap;

    // ---- stone golem (miner walk 5, sprint 8)
    public const float GolemWalk = 4.4f, GolemInvestigate = 5.0f, GolemChase = 7.0f;
    public const float GolemTurn = 2.2f;           // rad/s while moving; the slam wind-up turns at a third of this, so one miner distracts and another circles round
    public const float GolemSlamDamage = 35f, GolemSlamReach = 2.9f, GolemSlamArc = 0.95f; // half-angle rad
    public const float GolemWindup = 0.85f, GolemSlamTime = 0.25f, GolemRecover = 1.1f;
    public const float GolemKnockback = 10f;
    public const float GolemStunSeconds = 6f, GolemRearDot = -0.35f;   // a pickaxe hit with dot(forward, toAttacker) below RearDot hits the back crystal: stun
    public const float GolemSightCos = -0.2f;      // wide cone (except within 4 m)
    public const float GolemHeadroom = 2.05f;      // hunched golem: fits ordinary tunnels (ceiling 2.1+), not crawls or squeezes

    // ---- the Plug
    public const float PlugSpeed = 1.0f, PlugPushSpeed = 4f, PlugCrushDps = 20f, PlugReach = 1.75f, PlugPushback = 4.5f, PlugMaxStun = 0.8f;
    public const float PlugHearMul = 3f;           // hears noise out to loudness * this (it is slow, so it hears far)

    // ---- Wicks
    public const float WicksSpeed = 5.2f, WicksContact = 1.3f, WicksOilPerSec = 22f, WicksDps = 4f;
    public const float WicksSense = 22f, WicksPath = 30f;   // attracted only to a LIT lantern within 22 m (straight) AND at most 30 m of tunnel path
    public const float WicksFlash = 1.0f;                   // the lantern must have been lit this long (a shorter flash attracts nothing)
    public const float WicksCircleSeconds = 2.5f;           // after the light goes out: circle the last lit spot this long, then drift off
    public const int WicksCount = 10;

    // ---- the Lamplighter
    public const float LampWalk = 1.1f, LampTrigger = 3f, LampSnuff = 0.5f, LampHold = 2f, LampDamage = 40f, LampWalkAway = 14f, LampStand = 6f, LampCooldown = 40f;
    public const float LampHeadroom = 2.05f;

    // ---- the Assessor
    public const float AssessorWalk = 5f;          // = miner walk speed, never runs
    public const float AssessorReevaluate = 20f, AssessorPin = 3f, AssessorRetreat = 45f, AssessorCatch = 1.4f;
    public const int AssessorFromRound = 2;

    // ---- ember elemental
    public const float EmberWalk = 2.2f, EmberChase = 6.5f, EmberTurn = 10f;
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
