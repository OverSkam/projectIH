using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Server-side helpers for what monsters can sense: is a miner's lantern lit, and how fast is the miner moving (a still miner in the dark is nearly invisible).</summary>
public static class MonsterSenses
{
    static readonly Dictionary<int, (Vector3 pos, float speed)> _t = new();

    /// <summary>The held lantern is lit: switched on, has oil, not snuffed by a ghost. (Public information: everyone can see the light.)</summary>
    public static bool Lit(ServerPlayer p) =>
        p != null && p.Inv.Held == "lantern" && p.LanternOn && p.Oil > 0f && p.Items.LanternOutUntil <= 0;

    /// <summary>Horizontal speed in m/s, smoothed over ~0.3 s from the reported positions.</summary>
    public static float Speed(ServerPlayer p) => _t.TryGetValue(p.Peer, out var v) ? v.speed : 0f;

    public static void Tick(float dt)
    {
        if (dt <= 0f || Game.I == null) return;
        foreach (var p in Game.I.Players.Values)
        {
            var flat = new Vector3(p.Pos.X, 0, p.Pos.Z);
            if (!_t.TryGetValue(p.Peer, out var v)) { _t[p.Peer] = (flat, 0f); continue; }
            float inst = (flat - v.pos).Length() / dt;
            if (inst > 40f) inst = 0f;   // teleport (round start, throw)
            _t[p.Peer] = (flat, Mathf.Lerp(v.speed, inst, Mathf.Min(1f, dt * 5f)));
        }
    }

    public static void Reset() => _t.Clear();
}
