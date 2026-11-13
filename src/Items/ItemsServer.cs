using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Server-only per-player state of the items workstream (hangs off ServerPlayer.Items).</summary>
public class ItemState
{
    public double NextUse, AbilityReadyAt, EtherealUntil, LanternOutUntil, NoiseAcc;
    /// <summary>Last lantern state sent to everyone (-1 = none yet).</summary>
    public int LanternSent = -1;
    public int OilSecs = -1;
    public string UtilSig = "", StateSig = "";
    public bool MaskSent, Sprinting;
    public Vector3 LastPos;
    public bool HasLastPos;
}

/// <summary>Monsters (or anything server-driven) implement this to be stunned by bear traps. Monsters are never hurt by traps.</summary>
public interface IRootable
{
    void ServerRoot(float seconds);
}

/// <summary>Monsters implement this to react to dynamite (stun / disperse, never damage).</summary>
public interface IExplodable
{
    void OnExplosion(Vector3 center, float radius, float damage);
}

public static class InfinityTracker
{
    public static int Holder = -1, Count, LastRound;
    /// <summary>True once an Infinity Stein was generated this match (at most one per match).</summary>
    public static bool Spawned;
    public static void Reset() { Holder = -1; Count = 0; LastRound = 0; }
    public static void NewMatch() { Reset(); Spawned = false; }
}

/// <summary>Server-side behaviour of steins, utilities and per-player item state.</summary>
public static class ItemsServer
{
    public const int InfinityRoundsToWin = 3;
    public static double Now => Time.GetTicksMsec() / 1000.0;

    public static bool HasUtil(ServerPlayer p, string id)
    {
        foreach (var u in p.Inv.Utils) if (u == id) return true;
        return false;
    }

    /// <summary>Utility-slot passives (gas mask) - called from ServerPlayer.RecalcStats.</summary>
    public static void ApplyUtilityStats(ServerPlayer p, StatBlock s)
    {
        if (HasUtil(p, "gasmask")) s.GasImmune = true;
    }

    public static string AbilityOf(ServerPlayer p)
    {
        foreach (var id in p.Inv.Steins) { var d = Steins.Get(id); if (d?.Ability != null) return d.Ability; }
        return "";
    }

    public static void OnState(ServerPlayer p, int flags) { p.Items.Sprinting = (flags & 2) != 0; }

    public static void ResetRound(ServerPlayer p)
    {
        var it = p.Items;
        it.MaskSent = false; it.LanternOutUntil = 0; it.EtherealUntil = 0; it.StateSig = ""; it.LanternSent = -1; it.OilSecs = -1;
        it.AbilityReadyAt = 0; it.HasLastPos = false;
    }

    /// <summary>Called every physics tick for alive participants (from ServerPlayer.Tick).</summary>
    public static void Tick(ServerPlayer p, float dt)
    {
        var it = p.Items; double now = Now; var g = Game.I;
        string us = string.Join(",", p.Inv.Utils.Select(u => u ?? ""));
        if (us != it.UtilSig) { it.UtilSig = us; p.RecalcStats(); }
        bool mask = HasUtil(p, "gasmask");
        if (mask != it.MaskSent) { it.MaskSent = mask; g.ServerMask(p.Peer, mask); }
        TickLantern(p, dt, now);
        if (it.EtherealUntil > 0 && now > it.EtherealUntil) { it.EtherealUntil = 0; it.StateSig = ""; }
        string sig = $"{p.Stats.DarkVision}|{AbilityOf(p)}";
        if (sig != it.StateSig) { it.StateSig = sig; g.ServerSendItemState(p); }

        // footsteps make noise so monsters can react: sprinting always, walking once the sack is heavy (burden): the radius grows with the load
        it.NoiseAcc += dt;
        if (it.NoiseAcc >= 0.5)
        {
            var flat = p.Pos - it.LastPos; flat.Y = 0;
            float speed = flat.Length() / (float)it.NoiseAcc;
            float b = p.Burden;
            if (it.HasLastPos && it.Sprinting && speed > 4f) Game.ServerNoise(p.Pos, Burden.SprintNoise(b), p);
            else if (it.HasLastPos && speed > 1.5f && Burden.WalkNoise(b) > 0f) Game.ServerNoise(p.Pos, Burden.WalkNoise(b), p);
            it.LastPos = p.Pos; it.HasLastPos = true; it.NoiseAcc = 0;
        }
    }

    // ---------------------------------------------------------------- lantern oil
    public const float OilLowFraction = 0.25f;

    /// <summary>The lantern burns oil only while it is lit AND held; it is lit when switched on, has oil and no ghost snuffed it. The light state is public, the oil level is private.</summary>
    static void TickLantern(ServerPlayer p, float dt, double now)
    {
        var it = p.Items; var g = Game.I;
        if (it.LanternOutUntil > 0 && now > it.LanternOutUntil) it.LanternOutUntil = 0;
        bool has = HasUtil(p, "lantern");
        bool lit = has && p.LanternOn && p.Oil > 0f && it.LanternOutUntil <= 0;
        if (lit && p.Inv.Held == "lantern")
        {
            p.Oil = Math.Max(0f, p.Oil - dt);
            int secs = (int)Math.Ceiling(p.Oil);
            if (secs != it.OilSecs) { it.OilSecs = secs; p.Dirty = true; }   // private value, resent about once a second
            if (p.Oil <= 0f) lit = false;
        }
        int state = !lit ? 1 : (p.Oil <= g.Config.LanternOilSeconds * OilLowFraction ? 2 : 0);
        if (state != it.LanternSent) { it.LanternSent = state; g.ServerLantern(p.Peer, state); if (!lit && has && p.LanternOn && p.Oil <= 0f) Util.Pass("lantern_out_of_oil", $"name={p.Name}"); p.Dirty = true; }
    }

    /// <summary>Oil drum: refills the lantern completely.</summary>
    public static void Refuel(ServerPlayer p)
    {
        p.Oil = Game.I.Config.LanternOilSeconds; p.Dirty = true; p.Items.OilSecs = -1;
    }

    // ---------------------------------------------------------------- active abilities
    public static void UseAbility(ServerPlayer p)
    {
        var it = p.Items; double now = Now;
        string ab = AbilityOf(p);
        if (ab == "") return;
        if (now < it.AbilityReadyAt) { Game.I.ServerNotice(p, $"Not ready ({Math.Ceiling(it.AbilityReadyAt - now)} s)"); return; }
        var def = p.Inv.Steins.Select(Steins.Get).First(d => d?.Ability == ab);
        it.AbilityReadyAt = now + def.AbilityCooldown;
        if (ab == "ethereal")
        {
            it.EtherealUntil = now + def.AbilityDuration;
            Util.Pass("ethereal_activated", $"name={p.Name} secs={def.AbilityDuration}");
        }
        Game.I.ServerSendItemState(p);
    }

    // ---------------------------------------------------------------- giving / swapping / dropping
    public static Vector3 FrontOf(ServerPlayer p, float dist = 1.2f)
    {
        var fwd = new Vector3(-Mathf.Sin(p.Yaw), 0, -Mathf.Cos(p.Yaw));
        return p.Pos + fwd * dist + new Vector3(0, 0.4f, 0);
    }

    public static void DropItem(Vector3 pos, string item) => Game.I.ServerSpawnObject("pickup", pos, item);

    public static void OnSteinLeft(ServerPlayer p, string id)
    {
        if (id == "infinity") InfinityTracker.Reset();
    }

    /// <summary>Full slots: swap the picked-up item with the highlighted slot (stein) / selected utility. Returns false if not applicable.</summary>
    public static bool TrySwap(ServerPlayer p, string item)
    {
        int c = item.IndexOf(':'); if (c < 0) return false;
        string kind = item.Substring(0, c), id = item.Substring(c + 1);
        if (kind == "stein" && p.Inv.Steins.Count > 0)
        {
            int idx = Math.Clamp(p.Inv.SteinSel, 0, p.Inv.Steins.Count - 1);
            string old = p.Inv.Steins[idx];
            p.Inv.Steins[idx] = id;
            OnSteinLeft(p, old);
            DropItem(FrontOf(p), "stein:" + old);
            p.RecalcStats();
            return true;
        }
        if (kind == "util")
        {
            int slot = p.Inv.Selected;
            string old = p.Inv.Utils[slot];
            p.Inv.Utils[slot] = id;
            if (!string.IsNullOrEmpty(old)) DropItem(FrontOf(p), "util:" + old);
            p.Dirty = true;
            Game.I.ServerSyncHeld(p);
            return true;
        }
        return false;
    }

    public static void GiveStein(ServerPlayer p, string id)
    {
        if (!p.TryAdd("stein:" + id)) TrySwap(p, "stein:" + id);
    }

    public static void DropStein(ServerPlayer p, int idx)
    {
        if (idx < 0 || idx >= p.Inv.Steins.Count) return;
        string id = p.Inv.Steins[idx];
        p.Inv.Steins.RemoveAt(idx);
        if (p.Inv.SteinSel >= p.Inv.Steins.Count) p.Inv.SteinSel = 0;
        OnSteinLeft(p, id);
        DropItem(FrontOf(p), "stein:" + id);
        p.RecalcStats();
        Util.Pass("stein_dropped", $"name={p.Name} stein={id}");
    }

    // ---------------------------------------------------------------- utility use (RMB)
    static Vector3 FloorBelow(Vector3 from, float maxDown, out bool hit)
    {
        var q = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * maxDown, 1);
        var r = Game.I.World.GetWorld3D().DirectSpaceState.IntersectRay(q);
        hit = r.Count > 0;
        return hit ? r["position"].AsVector3() : from + Vector3.Down * maxDown;
    }

    static Vector3 Forward(ServerPlayer p) => new Vector3(-Mathf.Sin(p.Yaw), 0, -Mathf.Cos(p.Yaw));

    public static void UseHeld(ServerPlayer p)
    {
        var g = Game.I;
        string held = p.Inv.Held;
        if (string.IsNullOrEmpty(held)) return;
        var fwd = Forward(p);
        var eye = p.Pos + new Vector3(0, 1.4f, 0);
        switch (held)
        {
            case "dynamite":
            {
                float dist = 3f;
                var q = PhysicsRayQueryParameters3D.Create(eye, eye + fwd * 3.4f, 1);
                var r = g.World.GetWorld3D().DirectSpaceState.IntersectRay(q);
                if (r.Count > 0) dist = Math.Max(0.6f, eye.DistanceTo(r["position"].AsVector3()) - 0.4f);
                var at = FloorBelow(eye + fwd * dist, 4f, out _) + new Vector3(0, 0.12f, 0);
                int id = g.ServerSpawnObject("dynamite", at, "fuse=3");
                if (g.World.Objects.TryGetValue(id, out var o) && o is DynamiteObj d) d.Thrower = p;
                Consume(p);
                Util.Pass("dynamite_placed", $"by={p.Name}");
                break;
            }
            case "beartrap":
            {
                var at = FloorBelow(p.Pos + fwd * 1.5f + new Vector3(0, 1f, 0), 3f, out _) + new Vector3(0, 0.03f, 0);
                g.ServerSpawnObject("beartrap", at, "armed=1");
                Consume(p);
                Util.Pass("beartrap_placed", $"by={p.Name}");
                break;
            }
            case "rope":
            {
                var ahead = p.Pos + fwd * 1.0f;
                var floor = FloorBelow(new Vector3(ahead.X, p.Pos.Y + 0.6f, ahead.Z), 16f, out bool hit);
                float drop = p.Pos.Y - floor.Y;
                if (!hit || drop < 2.0f || drop > 15f) { g.ServerNotice(p, hit ? "No ledge here to hang a rope from." : "You cannot see the bottom."); return; }
                var anchor = new Vector3(ahead.X, p.Pos.Y + 0.05f, ahead.Z);
                g.ServerSpawnObject("rope", anchor, string.Format(System.Globalization.CultureInfo.InvariantCulture, "len={0:0.##}", drop));
                Consume(p);
                Util.Pass("rope_placed", $"by={p.Name} len={drop:0.0}");
                break;
            }
            default: break; // pickaxe/lantern/map/gasmask have no RMB action
        }
    }

    static void Consume(ServerPlayer p)
    {
        p.Inv.Utils[p.Inv.Selected] = null;
        p.Dirty = true;
        Game.I.ServerSyncHeld(p);
    }

    // ---------------------------------------------------------------- stein events
    public static void BloodEvent(ServerPlayer p, SteinEvent e)
    {
        if (e.Kind != SteinEventKind.RoundEnd) return;
        if (p.KillsThisRound > 0) { Util.Pass("blood_kept_with_kill", $"name={p.Name} tribute={p.TributeWeight}"); return; }
        int before = p.TributeWeight;
        p.Inv.Steins.RemoveAll(s => s == "blood");
        if (p.Inv.SteinSel >= p.Inv.Steins.Count) p.Inv.SteinSel = 0;
        p.TributeWeight -= p.TributeWeight / 2;
        p.RecalcStats();
        Game.I.ServerNotice(p, "The Blood Stein crumbles to dust. Half of this round's tribute is gone.");
        Util.Pass("blood_penalty_applied", $"name={p.Name} before={before} after={p.TributeWeight}");
    }

    public static void InfinityEvent(ServerPlayer p, SteinEvent e)
    {
                if (e.Kind == SteinEventKind.Death) { if (InfinityTracker.Holder == p.Peer) InfinityTracker.Reset(); return; }
        if (e.Kind != SteinEventKind.RoundEnd) return;
        int round = Game.I.Round;
        if (InfinityTracker.Holder == p.Peer && InfinityTracker.LastRound == round - 1) InfinityTracker.Count++; else { InfinityTracker.Holder = p.Peer; InfinityTracker.Count = 1; }
        InfinityTracker.LastRound = round;
        Game.I.ServerAnnounce($"The Infinity Stein has been carried for {InfinityTracker.Count} round{(InfinityTracker.Count == 1 ? "" : "s")}.");
        Util.Pass("infinity_count", $"n={InfinityTracker.Count} holder={p.Name}");
        if (InfinityTracker.Count >= InfinityRoundsToWin)
        {
            Game.I.ForcedWinner = p.Name;
            Game.I.ServerAnnounce("The Infinity Stein has found its master. The mine is theirs.");
            Util.Pass("infinity_win", $"winner={p.Name} round={round}");
        }
    }
}
