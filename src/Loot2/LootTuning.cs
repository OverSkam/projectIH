using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// ALL numbers of step 2 (geodes, bright chests, mimics) live here. Change feel and odds in this one class.
/// Reveal tier: 0 dull (coal / iron), 1 bright (silver / gold), 2 gem (ruby / diamond), 3 power stein.
/// </summary>
public static class LootTuning
{
    // ------------------------------------------------------------------ geodes
    public const int HitsToCrack = 3;
    /// <summary>Noise radius (m) of hit 1, 2, 3 (each crack is louder).</summary>
    public static readonly float[] HitNoise = { 6f, 9f, 13f };
    /// <summary>Noise radius of the reveal by tier. The stein reveal is heard (and answered by monsters) up to 45 m away.</summary>
    public static readonly float[] RevealNoise = { 12f, 20f, 30f, 45f };
    public const float DudNoise = 34f;
    /// <summary>Largest light radius (m) of the reveal by tier: dull 2 m, bright 6 m, gem 14 m, stein pillar fills the tunnel.</summary>
    public static readonly float[] RevealLight = { 2f, 6f, 14f, 22f };
    /// <summary>Seconds the reveal light lingers by tier (stein pillar ~4 s).</summary>
    public static readonly float[] RevealSeconds = { 0.6f, 1.4f, 5f, 4.2f };
    /// <summary>How far (m) a sound is heard / the screen reacts, by tier.</summary>
    public static readonly float[] ScreenRange = { 0f, 9f, 20f, 45f };

    public const float DudChance = 0.10f;
    /// <summary>Of the duds: this share is gas, the rest bats.</summary>
    public const float DudGasShare = 0.5f;
    public const float GasSeconds = 4.5f, GasRadius = 2.4f, GasTickSeconds = 0.5f, GasTickDamage = 3.5f;

    /// <summary>Base weights of a stone in a geode (silver and up are scaled by size, depth, round and luck).</summary>
    public static readonly (string ore, float w)[] OreWeights =
    {
        ("coal", 48f), ("iron", 32f), ("silver", 12f), ("gold", 4.2f), ("ruby", 1.5f), ("diamond", 0.55f),
    };
    /// <summary>Multiplier on the rarer stones for small / medium / large geodes.</summary>
    public static readonly float[] SizeRareMul = { 1f, 1.7f, 2.8f };
    /// <summary>Per level of effective depth (room depth + half the rounds beyond the first): the rare weights grow by this fraction.</summary>
    public const float DepthRareGain = 0.26f;
    /// <summary>Luck stein: the rare weights grow by this fraction per point of Stats.Luck; it also counts as one level of depth.</summary>
    public const float LuckRareGain = 0.7f;
    /// <summary>Large geodes only: chance of a power stein. Legendary only from effective depth 3.</summary>
    public const float SteinCommon = 0.04f, SteinRare = 0.01f, SteinLegendary = 0.003f;
    public const int LegendaryMinDepth = 3;

    // ------------------------------------------------------------------ chests
    public const float HoldSeconds = 2.5f, HoldReach = 3.6f;
    public static readonly float[] ChestLight = { 1.2f, 2.2f, 3.6f };
    public static readonly float[] ChestRange = { 5f, 6.5f, 8.5f };
    public static readonly int[] ChestMinStones = { 3, 4, 5 };
    public static readonly int[] ChestMaxStones = { 4, 5, 6 };
    public static readonly float[] ChestSteinChance = { 0.08f, 0.18f, 0.40f };
    public static readonly float[] ChestOpenNoise = { 14f, 20f, 26f };
    public const float PopInterval = 0.34f;
    public const float MimicChance = 0.20f;

    // ------------------------------------------------------------------ mimic
    public const float BiteSeconds = 4f, BiteDamage = 30f, BiteTick = 0.5f, MimicStun = 3f, MimicSpeed = 4.2f;
    public const float ShriekNoise = 70f;
    public const float BreathRange = 2f;

    // ------------------------------------------------------------------ helpers
    public static int TierOfStone(string ore) => Ores.WeightOf(ore) switch { >= 10 => 2, >= 4 => 1, _ => 0 };
    public static int TierOfItem(string item) => item.StartsWith("stein:") ? 3 : TierOfStone(item.Substring(item.IndexOf(':') + 1));

    public static Color ColorOfItem(string item)
    {
        int c = item.IndexOf(':'); string kind = item.Substring(0, c), id = item.Substring(c + 1);
        if (kind == "stein")
        {
            var d = Steins.Get(id);
            return d?.Rarity switch { Rarity.Legendary => new Color(1f, 0.6f, 0.12f), Rarity.Rare => new Color(0.35f, 0.7f, 1f), _ => new Color(0.55f, 1f, 0.6f) };
        }
        return id switch
        {
            "coal" => new Color(0.62f, 0.62f, 0.66f), "iron" => new Color(0.8f, 0.66f, 0.58f), "silver" => new Color(0.88f, 0.94f, 1f),
            "gold" => new Color(1f, 0.82f, 0.2f), "ruby" => new Color(1f, 0.12f, 0.25f), "diamond" => new Color(0.55f, 0.95f, 1f),
            _ => Ores.Get(id)?.Color ?? Colors.White,
        };
    }

    public static string LabelOf(string item)
    {
        int c = item.IndexOf(':'); string kind = item.Substring(0, c), id = item.Substring(c + 1);
        if (kind == "stein") return (Steins.Get(id)?.Name ?? id).ToUpperInvariant();
        var d = Ores.Get(id);
        return $"+{d?.TributeWeight ?? 0} {(d?.Name ?? id).ToUpperInvariant()}";
    }
}

public enum GeodeDud { None, Gas, Bats }

public class GeodeResult
{
    public List<string> Items = new();      // "ore:x" / "stein:x"
    public GeodeDud Dud;
    /// <summary>0..3 (see LootTuning); -1 = dud.</summary>
    public int Tier => Dud != GeodeDud.None ? -1 : Items.Count == 0 ? 0 : Items.Max(LootTuning.TierOfItem);
    /// <summary>The item that sets the colour of the reveal.</summary>
    public string Best => Items.Count == 0 ? null : Items.OrderByDescending(LootTuning.TierOfItem).ThenByDescending(i => i.StartsWith("ore:") ? Ores.WeightOf(i.Substring(4)) : 99).First();
    public int Tribute => Items.Where(i => i.StartsWith("ore:")).Sum(i => Ores.WeightOf(i.Substring(4)));
}

/// <summary>The roll tables for geodes, chests and mimic bait. Pure functions of (rng, parameters) so tests can seed them.</summary>
public static class LootRolls
{
    /// <summary>size 0 small, 1 medium, 2 large; depth = room depth (distance from the Hall); luck = Stats.Luck of the miner who cracks it.</summary>
    public static GeodeResult RollGeode(RandomNumberGenerator rng, int size, int depth, int round, float luck)
    {
        var res = new GeodeResult();
        if (rng.Randf() < LootTuning.DudChance) { res.Dud = rng.Randf() < LootTuning.DudGasShare ? GeodeDud.Gas : GeodeDud.Bats; return res; }
        int eff = depth + (round - 1) / 2 + (luck > 0.5f ? 1 : 0);
        if (size == 2)
        {
            float lm = 1f + LootTuning.LuckRareGain * 0.4f * luck;
            float r = rng.Randf();
            if (eff >= LootTuning.LegendaryMinDepth && r < LootTuning.SteinLegendary * lm) { res.Items.Add("stein:" + Loot.RollStein(rng, luck, round, Rarity.Legendary, true)); return res; }
            r = rng.Randf();
            if (r < LootTuning.SteinRare * lm) { res.Items.Add("stein:" + PickStein(rng, Rarity.Rare)); return res; }
            r = rng.Randf();
            if (r < LootTuning.SteinCommon * lm) { res.Items.Add("stein:" + PickStein(rng, Rarity.Common)); return res; }
        }
        int n = size == 0 ? 1 : size == 1 ? (rng.Randf() < 0.5f ? 2 : 1) : (rng.Randf() < 0.4f ? 3 : 2);
        for (int i = 0; i < n; i++) res.Items.Add("ore:" + RollOre(rng, size, eff, luck));
        return res;
    }

    public static string PickStein(RandomNumberGenerator rng, Rarity r)
    {
        var pool = Steins.All.Values.Where(d => d.Rarity == r).ToList();
        return pool[rng.RandiRange(0, pool.Count - 1)].Id;
    }

    /// <summary>One stone of a geode: junk mostly; depth, later rounds, big geodes and Luck shift the odds up.</summary>
    public static string RollOre(RandomNumberGenerator rng, int size, int effDepth, float luck)
    {
        float rare = LootTuning.SizeRareMul[Math.Clamp(size, 0, 2)] * (1f + LootTuning.DepthRareGain * effDepth) * (1f + LootTuning.LuckRareGain * luck);
        float total = 0;
        var w = new float[LootTuning.OreWeights.Length];
        for (int i = 0; i < w.Length; i++)
        {
            var (ore, bw) = LootTuning.OreWeights[i];
            var def = Ores.Get(ore);
            float v = Ores.WeightOf(ore) >= 4 ? bw * rare : bw;
            if (def != null && def.MinDepth > effDepth + 1) v = 0f;   // gold / ruby need some depth, diamond more
            w[i] = v; total += v;
        }
        float r = rng.Randf() * total;
        for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0) return LootTuning.OreWeights[i].ore; }
        return "coal";
    }

    /// <summary>Bright chests: 3-6 stones (better than geodes) and a chance of a stein at the end.</summary>
    public static List<string> RollChest(RandomNumberGenerator rng, int tier, int depth, int round, float luck)
    {
        var l = new List<string>();
        int n = rng.RandiRange(LootTuning.ChestMinStones[tier], LootTuning.ChestMaxStones[tier]);
        int eff = depth + (round - 1) / 2 + tier + (luck > 0.5f ? 1 : 0);
        for (int i = 0; i < n; i++) l.Add("ore:" + RollOre(rng, 1 + (tier >= 1 ? 1 : 0), eff, luck));
        float sc = LootTuning.ChestSteinChance[tier] * (1f + 0.5f * luck);
        if (rng.Randf() < sc)
        {
            string st;
            if (tier == 2) st = rng.Randf() < 0.08f ? Loot.RollStein(rng, luck, round, Rarity.Legendary, true) : Loot.RollStein(rng, luck, round, Rarity.Rare, false);
            else st = rng.Randf() < (tier == 1 ? 0.18f : 0.06f) ? PickStein(rng, Rarity.Rare) : PickStein(rng, Rarity.Common);
            l.Add("stein:" + st);
        }
        return l;
    }

    /// <summary>What a mimic spits out as bait: one or two junk stones.</summary>
    public static List<string> RollBait(RandomNumberGenerator rng)
    {
        var l = new List<string>();
        int n = rng.RandiRange(1, 2);
        for (int i = 0; i < n; i++) l.Add("ore:" + (rng.Randf() < 0.65f ? "coal" : rng.Randf() < 0.6f ? "iron" : "silver"));
        return l;
    }

    /// <summary>How many geodes of which size a vein breaks into, by the richness of its ore (1..3 geodes).</summary>
    public static List<int> VeinGeodes(RandomNumberGenerator rng, string ore)
    {
        int rich = Ores.WeightOf(ore) switch { >= 10 => 3, >= 7 => 2, >= 4 => 1, _ => 0 };
        var sizes = new List<int>();
        int count = rich switch { 0 => rng.Randf() < 0.4f ? 2 : 1, 1 => rng.Randf() < 0.55f ? 2 : 1, 2 => rng.Randf() < 0.5f ? 3 : 2, _ => rng.Randf() < 0.6f ? 3 : 2 };
        for (int i = 0; i < count; i++)
        {
            float r = rng.Randf();
            sizes.Add(rich switch
            {
                0 => r < 0.75f ? 0 : 1,
                1 => r < 0.25f ? 0 : r < 0.85f ? 1 : 2,
                2 => r < 0.5f ? 1 : 2,
                _ => 2,
            });
        }
        return sizes;
    }
}
