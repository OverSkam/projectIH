using System;

namespace AppStein;

/// <summary>The burden rules (v2.1). Burden B is the total of the sack's stones (x Stats.BurdenMul). It slows you, drains sprint faster and makes your footsteps carry.</summary>
public static class Burden
{
    public const float MinSpeed = 0.35f, HalfAt = 36f;
    /// <summary>Movement speed multiplier: 1 / (1 + B/36), never below 0.35 (about 55% at B = 30).</summary>
    public static float SpeedMul(float b) => Math.Max(MinSpeed, 1f / (1f + Math.Max(0f, b) / HalfAt));
    /// <summary>Sprint stamina drains this many times faster than unburdened.</summary>
    public static float SprintDrainMul(float b) => 1f + Math.Max(0f, b) / 10f;
    /// <summary>Radius (m) of the noise of a walking footstep cycle: silent unburdened, grows with the load.</summary>
    public static float WalkNoise(float b) => b < 3f ? 0f : Math.Min(16f, 2.5f + b * 0.3f);
    /// <summary>Radius of the noise of a sprinting step (was a flat 9 m).</summary>
    public static float SprintNoise(float b) => 9f * (1f + Math.Max(0f, b) / 40f);
}
