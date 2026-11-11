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
}

public static class Steins
{
    public static readonly Dictionary<string, SteinDef> All = new();
    static void Add(SteinDef d) => All[d.Id] = d;
    static Steins()
    {
        Add(new SteinDef
        {
            Id = "regen", Name = "Regen Stein", Description = "Slowly heals you.", Color = new Color(0.2f, 0.9f, 0.3f),
            OnTick = (p, dt) => p.Regen(1.0f * dt),
        });
        Add(new SteinDef
        {
            Id = "swift", Name = "Swift Stein", Description = "+20% speed.", Color = new Color(0.3f, 0.7f, 1f),
            Modify = s => s.SpeedMul *= 1.2f,
        });
        Add(new SteinDef
        {
            Id = "stoneskin", Name = "Stoneskin Stein", Description = "-25% damage taken.", Color = new Color(0.6f, 0.6f, 0.6f),
            Modify = s => s.DamageTakenMul *= 0.75f,
        });
        Add(new SteinDef
        {
            Id = "packmule", Name = "Packmule Stein", Description = "+2 sack slots.", Color = new Color(0.8f, 0.55f, 0.2f),
            Modify = s => s.SackBonus += 2,
        });
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
    /// <summary>True when the behaviour is not implemented yet (placeholder for later content).</summary>
    public bool Stub;
}

public static class Utilities
{
    public static readonly Dictionary<string, UtilityDef> All = new();
    static void Add(UtilityDef d) => All[d.Id] = d;
    static Utilities()
    {
        Add(new UtilityDef { Id = "pickaxe", Name = "Pickaxe", Description = "Mine ore. 20 damage.", Color = new Color(0.55f, 0.4f, 0.25f), IsPickaxe = true, MeleeDamage = 20f });
        Add(new UtilityDef { Id = "lantern", Name = "Lantern", Description = "Light.", Color = new Color(1f, 0.8f, 0.4f), IsLight = true });
        Add(new UtilityDef { Id = "rope", Name = "Rope", Description = "Climb pits.", Color = new Color(0.7f, 0.6f, 0.3f), Stub = true });
        Add(new UtilityDef { Id = "gasmask", Name = "Gas mask", Description = "Immune to gas.", Color = new Color(0.3f, 0.5f, 0.3f), Stub = true });
        Add(new UtilityDef { Id = "map", Name = "Map", Description = "Shows the layout.", Color = new Color(0.9f, 0.85f, 0.6f), Stub = true });
        Add(new UtilityDef { Id = "dynamite", Name = "Dynamite", Description = "Boom.", Color = new Color(0.8f, 0.2f, 0.1f), Stub = true });
        Add(new UtilityDef { Id = "beartrap", Name = "Bear trap", Description = "Placeable trap.", Color = new Color(0.4f, 0.4f, 0.45f), Stub = true });
    }
    public static UtilityDef Get(string id) => string.IsNullOrEmpty(id) ? null : (All.TryGetValue(id, out var d) ? d : null);
}
