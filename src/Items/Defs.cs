using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

// ---------------------------------------------------------------- Ore
public class OreDef
{
    public string Id, Name;
    public int Value;
    public Color Color;
    public float Glow;
    /// <summary>Minimum room depth at which veins of this ore spawn (richer deeper).</summary>
    public int MinDepth;
    public float Weight = 1f;
}

public static class Ores
{
    public static readonly Dictionary<string, OreDef> All = new();
    static void Add(OreDef d) => All[d.Id] = d;
    static Ores()
    {
        Add(new OreDef { Id = "coal", Name = "Coal", Value = 10, Color = new Color(0.12f, 0.12f, 0.14f), MinDepth = 1, Weight = 5 });
        Add(new OreDef { Id = "iron", Name = "Iron", Value = 25, Color = new Color(0.6f, 0.45f, 0.4f), MinDepth = 1, Weight = 4 });
        Add(new OreDef { Id = "silver", Name = "Silver", Value = 50, Color = new Color(0.8f, 0.85f, 0.9f), Glow = 0.3f, MinDepth = 1, Weight = 3 });
        Add(new OreDef { Id = "gold", Name = "Gold", Value = 100, Color = new Color(1f, 0.8f, 0.1f), Glow = 0.6f, MinDepth = 2, Weight = 2 });
        Add(new OreDef { Id = "ruby", Name = "Ruby", Value = 175, Color = new Color(0.9f, 0.05f, 0.2f), Glow = 1.0f, MinDepth = 2, Weight = 1 });
        Add(new OreDef { Id = "diamond", Name = "Diamond", Value = 250, Color = new Color(0.6f, 0.95f, 1f), Glow = 1.5f, MinDepth = 3, Weight = 0.6f });
    }
    public static OreDef Get(string id) => All.TryGetValue(id, out var d) ? d : null;
    public static int ValueOf(string id) => Get(id)?.Value ?? 0;

    /// <summary>Weighted pick; deeper rooms and later rounds unlock richer ore.</summary>
    public static string Pick(RandomNumberGenerator rng, int depth, int round)
    {
        int eff = depth + (round - 1) / 2;
        float total = 0;
        foreach (var o in All.Values) if (o.MinDepth <= eff) total += o.Weight;
        float r = rng.Randf() * total;
        foreach (var o in All.Values)
        {
            if (o.MinDepth > eff) continue;
            r -= o.Weight;
            if (r <= 0) return o.Id;
        }
        return "coal";
    }
}

// ---------------------------------------------------------------- Steins
public enum Rarity { Common, Rare, Legendary }

/// <summary>Stat modifiers a stein can change. Recomputed by the server whenever the inventory changes.</summary>
public sealed class StatBlock
{
    public float SpeedMul = 1f, JumpMul = 1f, DamageTakenMul = 1f, MeleeDamageMul = 1f, MiningSpeedMul = 1f, RegenPerSec = 0f;
    public int SackBonus = 0;
    public bool NoFallDamage = false;
    /// <summary>Immune to damage with cause "gas".</summary>
    public bool GasImmune = false;
    public bool DarkVision = false;
    /// <summary>0 = normal. Positive improves drop rarity rolls.</summary>
    public float Luck = 0f;
    /// <summary>Multiplier on noise this miner makes (Game.ServerNoise).</summary>
    public float NoiseMul = 1f;
}

public enum SteinEventKind { RoundStart, RoundEnd, DamageTaken, DamageDealt, Kill, Death, Deposit, Mined }

public struct SteinEvent
{
    public SteinEventKind Kind;
    public float Amount;
    public ServerPlayer Other;
}

public class SteinDef
{
    public string Id, Name, Description;
    public Rarity Rarity = Rarity.Common;
    public Color Color = Colors.White;
    /// <summary>Passive stat modifiers.</summary>
    public Action<StatBlock> Modify;
    /// <summary>Server-side, called every physics tick for alive carriers.</summary>
    public Action<ServerPlayer, float> OnTick;
    /// <summary>Server-side event hook (damage, kills, deposit, round start/end...).</summary>
    public Action<ServerPlayer, SteinEvent> OnEvent;
    /// <summary>Passive modifiers that depend on the carrier (e.g. money). Runs inside ServerPlayer.RecalcStats().</summary>
    public Action<ServerPlayer, StatBlock> ModifyFor;
    /// <summary>Id of an active ability triggered with the stein key (Q), or null.</summary>
    public string Ability;
    public float AbilityCooldown, AbilityDuration;
}

public static class Steins
{
    public static readonly Dictionary<string, SteinDef> All = new();
    static void Add(SteinDef d) => All[d.Id] = d;
    static Steins()
    {
        Add(new SteinDef
        {
            Id = "regen", Name = "Regen Stein", Description = "Heals you by 1 health every second.", Color = new Color(0.2f, 0.9f, 0.3f),
            OnTick = (p, dt) => p.Regen(1.0f * dt),
        });
        Add(new SteinDef
        {
            Id = "swift", Name = "Swift Stein", Description = "You move 20% faster.", Color = new Color(0.3f, 0.7f, 1f),
            Modify = s => s.SpeedMul *= 1.2f,
        });
        Add(new SteinDef
        {
            Id = "stoneskin", Name = "Stoneskin Stein", Description = "You take 25% less damage from everything.", Color = new Color(0.6f, 0.6f, 0.6f),
            Modify = s => s.DamageTakenMul *= 0.75f,
        });
        Add(new SteinDef
        {
            Id = "packmule", Name = "Packmule Stein", Description = "Your sack holds 6 ore instead of 4.", Color = new Color(0.8f, 0.55f, 0.2f),
            Modify = s => s.SackBonus += 2,
        });
        // ---- Common
        Add(new SteinDef { Id = "strongarm", Name = "Strongarm Stein", Description = "You mine 50% faster and your melee hits do 25% more damage.", Color = new Color(0.85f, 0.3f, 0.2f),
            Modify = s => { s.MiningSpeedMul *= 1.5f; s.MeleeDamageMul *= 1.25f; } });
        Add(new SteinDef { Id = "owl", Name = "Owl Stein", Description = "Night vision: a faint cool glow lights up the dark around you. Only you see it; it fades inside the camp.", Color = new Color(1f, 0.85f, 0.2f),
            Modify = s => s.DarkVision = true });
        Add(new SteinDef { Id = "feather", Name = "Feather Stein", Description = "No fall damage, and you jump 25% higher.", Color = new Color(0.92f, 0.95f, 1f),
            Modify = s => { s.NoFallDamage = true; s.JumpMul *= 1.25f; } });
        Add(new SteinDef { Id = "hush", Name = "Hush Stein", Description = "Monsters hear your noise half as far.", Color = new Color(0.7f, 0.55f, 0.95f),
            Modify = s => s.NoiseMul *= 0.5f });
        // ---- Rare
        Add(new SteinDef { Id = "luck", Name = "Luck Stein", Rarity = Rarity.Rare, Description = "Chests, shrines and veins give rarer rewards.", Color = new Color(0.3f, 1f, 0.5f),
            Modify = s => s.Luck += 1f });
        Add(new SteinDef { Id = "midas", Name = "Midas Stein", Rarity = Rarity.Rare, Description = "Melee damage grows with your banked money: +1% per $25, up to +60% at $1,500.", Color = new Color(1f, 0.78f, 0.1f),
            ModifyFor = (p, s) => s.MeleeDamageMul *= 1f + Math.Min(0.6f, p.Money / 1500f),
            OnEvent = (p, e) => { if (e.Kind == SteinEventKind.Deposit || e.Kind == SteinEventKind.RoundStart || e.Kind == SteinEventKind.RoundEnd) p.RecalcStats(); } });
        Add(new SteinDef { Id = "ethereal", Name = "Ethereal Stein", Rarity = Rarity.Rare, Description = "Press Q to walk through cracked thin walls for 5 seconds (normal rock still blocks you). 25 second cooldown.", Color = new Color(0.5f, 0.95f, 1f),
            Ability = "ethereal", AbilityCooldown = 25f, AbilityDuration = 5f });
        // ---- Legendary
        Add(new SteinDef { Id = "blood", Name = "Blood Stein", Rarity = Rarity.Legendary, Description = "Damage taken -50%, speed +30%, melee damage +80%, regen 1 health/s. You must kill a miner each round: if you have not by round end the stein turns to dust and you lose half your money.", Color = new Color(0.75f, 0.02f, 0.05f),
            Modify = s => { s.DamageTakenMul *= 0.5f; s.SpeedMul *= 1.3f; s.MeleeDamageMul *= 1.8f; s.RegenPerSec += 1f; },
            OnEvent = ItemsServer.BloodEvent });
        Add(new SteinDef { Id = "infinity", Name = "Infinity Stein", Rarity = Rarity.Legendary, Description = "Gives no power. Hold it when 3 rounds in a row end and you win the match at once. The Goblin announces the count (not the holder). Dies with you: it drops in your bag.", Color = new Color(1f, 1f, 1f),
            OnEvent = ItemsServer.InfinityEvent });
    }
    public static SteinDef Get(string id) => All.TryGetValue(id, out var d) ? d : null;
    public static string RandomCommon(RandomNumberGenerator rng)
    {
        var list = new List<SteinDef>();
        foreach (var d in All.Values) if (d.Rarity == Rarity.Common) list.Add(d);
        return list[rng.RandiRange(0, list.Count - 1)].Id;
    }
}

// ---------------------------------------------------------------- Utilities
public class UtilityDef
{
    public string Id, Name, Description;
    public Color Color = Colors.Gray;
    public bool IsLight;
    public bool IsPickaxe;
    public float MeleeDamage = 5f;
}

public static class Utilities
{
    public static readonly Dictionary<string, UtilityDef> All = new();
    static void Add(UtilityDef d) => All[d.Id] = d;
    static Utilities()
    {
        Add(new UtilityDef { Id = "pickaxe", Name = "Pickaxe", Description = "Hold left mouse to mine veins. Also your weapon: 20 damage.", Color = new Color(0.55f, 0.4f, 0.25f), IsPickaxe = true, MeleeDamage = 20f });
        Add(new UtilityDef { Id = "lantern", Name = "Lantern", Description = "Your light. Without it the cave is almost black, but monsters spot a lit lantern from 24 m instead of 9 m.", Color = new Color(1f, 0.8f, 0.4f), IsLight = true });
        Add(new UtilityDef { Id = "rope", Name = "Rope", Description = "Right mouse at a ledge with a 2 to 15 m drop hangs a rope: W climbs, S/Ctrl descends, Space lets go. A pickaxe cuts it. Used up.", Color = new Color(0.7f, 0.6f, 0.3f) });
        Add(new UtilityDef { Id = "gasmask", Name = "Gas mask", Description = "Carry it in a utility slot: you are immune to gas (vents and gas wisps).", Color = new Color(0.3f, 0.5f, 0.3f) });
        Add(new UtilityDef { Id = "map", Name = "Map", Description = "Hold right mouse to see the room layout and your own position.", Color = new Color(0.9f, 0.85f, 0.6f) });
        Add(new UtilityDef { Id = "dynamite", Name = "Dynamite", Description = "Right mouse places it: 3 s fuse, 6.5 m blast up to 70 damage, breaks nearby veins, very loud. Harmless in the camp. Used up.", Color = new Color(0.8f, 0.2f, 0.1f) });
        Add(new UtilityDef { Id = "beartrap", Name = "Bear trap", Description = "Right mouse places it: roots whoever steps on it for 3 s and bites for 15 damage. Harmless in the camp. Used up.", Color = new Color(0.4f, 0.4f, 0.45f) });
    }
    public static UtilityDef Get(string id) => string.IsNullOrEmpty(id) ? null : (All.TryGetValue(id, out var d) ? d : null);
}
