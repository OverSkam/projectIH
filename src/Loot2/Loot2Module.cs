using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Step 2 of the build order: the dopamine loop. Veins break off sealed geodes that are cracked in place; bright chests glow in the dark; some are mimics.
/// Registers object kinds, the "bchest" marker spawner, the round hook that picks the mimics, the screen-feedback layer and the test / staging code.</summary>
[ContentModule]
public static class Loot2Module
{
    public static void Register()
    {
        NetObject.Factories["geode"] = () => new GeodeObj();
        NetObject.Factories["bchest"] = () => new GlowChestObj();
        NetObject.Factories["gaspuff"] = () => new GasPuffObj();
        Game.MarkerSpawners["bchest"] = SpawnChest;
        Game.RoundPopulated += OnRoundPopulated;
        Game.I.PhaseChanged += () => { if (Game.I.Phase == GamePhase.Round) Loot2Ui.Ensure(); };
        Loot2Debug.Register();
    }

    /// <summary>A mined vein: 1-3 geodes by the richness of its ore (small / medium / large), dropped on the floor in front of the miner.</summary>
    public static void OnVeinBroken(Vector3 pos, string ore, ServerPlayer p)
    {
        var g = Game.I;
        var rng = new RandomNumberGenerator(); rng.Randomize();
        var sizes = LootRolls.VeinGeodes(rng, ore);
        Vector3? pref = p != null ? (p.Pos - pos) with { Y = 0 } : null;
        var land = LootPlacement.Landings(g.World, pos, pref, sizes.Count, 1.35f, 2.3f, rng);
        int depth = g.World.Cave.RoomAt(pos)?.Depth ?? 1;
        for (int i = 0; i < sizes.Count; i++)
        {
            int id = g.ServerSpawnObject("geode", land[i] - new Vector3(0, 0.4f, 0), GeodeObj.MakeData(sizes[i], 0));
            if (g.World.Objects.TryGetValue(id, out var o) && o is GeodeObj ge) ge.Depth = depth;
        }
        Util.Pass("vein_geodes", $"ore={ore} n={sizes.Count} sizes={string.Join(",", sizes)} by={p?.Name}");
    }

    /// <summary>Chest tier by depth: wooden everywhere, iron from depth 2, gilded (rare, brightest) only deep.</summary>
    public static int RollTier(RandomNumberGenerator rng, int depth)
    {
        float wood = 6f, iron = depth >= 2 ? 2.5f + depth * 0.5f : 0f, gilded = depth >= 4 ? 1.5f + (depth - 4) : 0f;
        float r = rng.Randf() * (wood + iron + gilded);
        return r < wood ? 0 : r < wood + iron ? 1 : 2;
    }

    static void SpawnChest(Game g, RoomInfo room, SpawnMarker m, RandomNumberGenerator rng)
    {
        int tier = RollTier(rng, room.Depth);
        int id = g.ServerSpawnObject("bchest", m.Pos, GlowChestObj.MakeData(tier, "closed", false));
        if (g.World.Objects.TryGetValue(id, out var o) && o is GlowChestObj c) c.Depth = room.Depth;
    }

    /// <summary>After all markers are spawned: ~20% of the chests are mimics, never the chest nearest the Hall (and none at all if there is only one chest).</summary>
    static void OnRoundPopulated(Game g, RandomNumberGenerator rng)
    {
        var chests = g.World.Objects.Values.OfType<GlowChestObj>().OrderBy(c => c.GlobalPosition.DistanceTo(g.World.Cave.GoblinPos)).ToList();
        int mimics = 0;
        for (int i = 1; i < chests.Count; i++)
            if (!Util.HasArg("l2nomimic") && rng.Randf() < LootTuning.MimicChance) { chests[i].ServerMakeMimic(true); mimics++; }
        if (chests.Count > 0) Util.Log($"LOOT2: round {g.Round} chests={chests.Count} mimics={mimics} tiers={string.Join(",", chests.Select(c => c.Tier))}");
    }
}
