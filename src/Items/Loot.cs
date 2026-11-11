using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Rarity rolls for chests, shrines, loose loot and veins. Luck (Stats.Luck) shifts every roll towards rarer results.</summary>
public static class Loot
{
    static readonly (string id, float w)[] UtilPool =
    {
        ("rope", 3), ("gasmask", 2), ("map", 2), ("dynamite", 3), ("beartrap", 2), ("lantern", 0.7f), ("pickaxe", 0.7f),
    };

    public static string RollUtility(RandomNumberGenerator rng)
    {
        float total = UtilPool.Sum(u => u.w), r = rng.Randf() * total;
        foreach (var u in UtilPool) { r -= u.w; if (r <= 0) return u.id; }
        return "rope";
    }

    /// <summary>Picks a stein by rarity. Legendary = Blood (rare) or the Infinity Stein (round 2+, at most one per match).</summary>
    public static string RollStein(RandomNumberGenerator rng, float luck, int round, Rarity min = Rarity.Common, bool allowLegendary = true)
    {
        float wc = Math.Max(15f, 70f - 12f * luck), wr = 26f + 8f * luck, wl = allowLegendary ? 4f + 3f * luck : 0f;
        if (min >= Rarity.Rare) wc = 0;
        if (min >= Rarity.Legendary) { wc = 0; wr = 0; wl = 1; }
        float r = rng.Randf() * (wc + wr + wl);
        Rarity rar = r < wc ? Rarity.Common : r < wc + wr ? Rarity.Rare : Rarity.Legendary;
        if (rar == Rarity.Legendary)
        {
            bool inf = round >= 2 && !InfinityTracker.Spawned && rng.Randf() < 0.45f;
            if (inf) { InfinityTracker.Spawned = true; return "infinity"; }
            return "blood";
        }
        var pool = Steins.All.Values.Where(d => d.Rarity == rar).ToList();
        return pool[rng.RandiRange(0, pool.Count - 1)].Id;
    }

    /// <summary>A shrine's stein may be upgraded when the taker has Luck.</summary>
    public static string LuckUpgrade(string stein, float luck, RandomNumberGenerator rng, int round)
    {
        var d = Steins.Get(stein);
        if (d == null || luck <= 0 || d.Rarity != Rarity.Common || rng.Randf() > luck * 0.3f) return stein;
        return RollStein(rng, luck, round, Rarity.Rare, false);
    }

    /// <summary>Chest contents: mostly ore and utilities, sometimes a stein.</summary>
    public static List<string> RollChest(RandomNumberGenerator rng, float luck, int depth, int round)
    {
        var l = new List<string>();
        int n = 1 + (rng.Randf() < 0.4f + 0.1f * luck ? 1 : 0);
        for (int i = 0; i < n; i++)
        {
            float r = rng.Randf();
            float steinChance = 0.07f + 0.07f * luck;
            if (r < steinChance) l.Add("stein:" + RollStein(rng, luck, round));
            else if (r < steinChance + 0.33f) l.Add("util:" + RollUtility(rng));
            else l.Add("ore:" + Ores.Pick(rng, depth + (luck > 0.5f ? 1 : 0), round));
        }
        return l;
    }

    /// <summary>Small loose pickup from a 'loot' marker: ore or a utility, never a stein.</summary>
    public static string RollLoose(RandomNumberGenerator rng, int depth, int round) =>
        rng.Randf() < 0.7f ? "ore:" + Ores.Pick(rng, depth, round) : "util:" + RollUtility(rng);

    /// <summary>Luck can upgrade the ore a vein yields to the next richer one.</summary>
    public static string LuckyOre(string ore, ServerPlayer p)
    {
        if (p == null || p.Stats.Luck <= 0f) return ore;
        var rng = new RandomNumberGenerator(); rng.Randomize();
        if (rng.Randf() > p.Stats.Luck * 0.2f) return ore;
        int v = Ores.ValueOf(ore);
        var next = Ores.All.Values.Where(o => o.Value > v).OrderBy(o => o.Value).FirstOrDefault();
        return next?.Id ?? ore;
    }
}
