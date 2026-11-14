using Godot;

namespace AppStein;

/// <summary>All numbers of the v2.3 "other miners" step in one place (shove, spill, pickpocket, downed options).</summary>
public static class SocialTuning
{
    // ---- shove (R)
    public const float ShoveRange = 1.6f;         // metres
    public const float ShoveSlack = 0.35f;        // extra reach the server grants for latency (its picture of the target is ~50-100 ms old)
    public const float ShoveConeCos = 0.55f;      // cos of the half angle of the cone in front of the shover (about 57 degrees)
    public const float ShoveCooldown = 1.2f;
    public const float ShoveStagger = 0.4f;       // the shoved miner cannot act for this long
    public const float ShoveSpeed = 11f;          // initial horizontal speed; the victim decelerates at StaggerDecel, so about 3.6 m of travel
    public const float ShoveSprintSpeed = 14f;    // when the shover was sprinting: about 5.4 m
    public const float StaggerDecel = 14f;        // m/s^2 while staggered (normal floor friction is 40)
    public const float ShoveStamina = 0.08f;      // fraction of the run bar a shove costs (client side)
    public const float ShoveNoise = 7f;           // metres monsters can hear a scuffle
    /// <summary>The shover stands in this arc behind the victim's facing: the angle between the victim's forward and the direction victim->shover is more than 120 degrees.</summary>
    public const float BackCos = -0.5f;

    // ---- pickpocket (crouch + hold E behind a miner)
    public const float PickRange = 1.3f, PickRangeSlack = 0.55f;
    public const float PickSeconds = 1.6f;
    public const float PickFailCos = -0.3f;       // the attempt fails once the thief is no longer this far behind the victim (hysteresis against the start arc)
    public const float PickLoudAfter = 0.5f;      // a failure after more than this fraction of the hold clatters loudly
    public const float PickFailCooldown = 6f;
    public const float PickVictimDelay = 1.5f;    // the victim hears about the loss this much later
    public const float PickLoudNoise = 22f;       // monsters hear a fumbled pickpocket
    public const float RustleHear = 4.5f;         // the cloth rustle can be heard this far

    // ---- downed miners
    public const float ReviveSeconds = 3f, ReviveRange = 2.4f, ReviveHealth = 35f;
    public const float DragRange = 2.4f, DragMaxRange = 4.5f, DragSpeedMul = 0.5f, DragTether = 1.25f;

    /// <summary>Horizontal forward vector of a miner with the given yaw (Miner nodes face -Z).</summary>
    public static Vector3 Fwd(float yaw) => new Vector3(-Mathf.Sin(yaw), 0, -Mathf.Cos(yaw));

    /// <summary>True when 'who' stands in the 120 degree arc behind a miner at 'victimPos' facing 'victimYaw'.</summary>
    public static bool IsBehind(Vector3 victimPos, float victimYaw, Vector3 who, float cosLimit = BackCos)
    {
        var d = new Vector3(who.X - victimPos.X, 0, who.Z - victimPos.Z);
        if (d.LengthSquared() < 0.0001f) return true;
        return Fwd(victimYaw).Dot(d.Normalized()) < cosLimit;
    }
}
