using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Registers everything of the items workstream: object kinds, marker spawners, round hooks, HUD.</summary>
[ContentModule]
public static class ItemsModule
{
    public static void Register()
    {
        NetObject.Factories["chest"] = () => new ChestObj();
        NetObject.Factories["shrine"] = () => new ShrineObj();
        NetObject.Factories["shrineplate"] = () => new ShrinePlateObj();
        NetObject.Factories["shrinebarrier"] = () => new ShrineBarrierObj();
        NetObject.Factories["dynamite"] = () => new DynamiteObj();
        NetObject.Factories["beartrap"] = () => new BearTrapObj();
        NetObject.Factories["rope"] = () => new RopeObj();
        NetObject.Factories["oil"] = () => new OilDrumObj();
        NetObject.Factories["dbgplatform"] = () => new DbgPlatformObj();
        NetObject.Factories["dbgthinwall"] = () => new DbgThinWallObj();

        Game.MarkerSpawners["chest"] = SpawnChest;
        Game.MarkerSpawners["loot"] = SpawnLoot;
        Game.MarkerSpawners["shrine"] = SpawnShrine;
        Game.MarkerSpawners["oil"] = (g, room, m, rng) => g.ServerSpawnObject("oil", m.Pos, OilDrumObj.MakeData(g.Config.OilDrumRefills));
        Game.RoundPopulated += OnRoundPopulated;

        ItemsDebug.Register();
    }

    static void OnRoundPopulated(Game g, RandomNumberGenerator rng)
    {
        if (g.Round == 1) { InfinityTracker.NewMatch(); g.ForcedWinner = null; }
        foreach (var p in g.Players.Values) ItemsServer.ResetRound(p);
        ItemsDebug.OnRoundPopulated(g);
    }

    static void SpawnChest(Game g, RoomInfo room, SpawnMarker m, RandomNumberGenerator rng)
    {
        int id = g.ServerSpawnObject("chest", m.Pos, "open=0");
        if (g.World.Objects.TryGetValue(id, out var o) && o is ChestObj c) c.Depth = room.Depth;
    }

    static void SpawnLoot(Game g, RoomInfo room, SpawnMarker m, RandomNumberGenerator rng)
    {
        g.ServerSpawnObject("pickup", m.Pos + new Vector3(0, 0.4f, 0), Loot.RollLoose(rng, room.Depth, g.Round));
    }

    static World _shrineWorld;
    static int _shrines;

    static void SpawnShrine(Game g, RoomInfo room, SpawnMarker m, RandomNumberGenerator rng)
    {
        float r = rng.Randf();
        // pacing: at most one stein shrine per ~4 rooms
        if (_shrineWorld != g.World) { _shrineWorld = g.World; _shrines = 0; }
        if (_shrines >= Math.Max(1, g.World.Cave.Rooms.Count / 4)) return;
        _shrines++;
        string type = r < 0.4f ? "plate" : r < 0.65f ? "blood" : "timed";
        // the cave collision is not in the physics space yet: the plate/switch placement needs raycasts, so wait two physics frames
        TrapSpawner.Defer(g, () => SpawnShrineOfType(g, m.Pos, type, rng));
    }

    /// <summary>Also used by the debug spawner. Picks the free direction for the plate/switch, rolls the stein and spawns all parts.</summary>
    public static ShrineObj SpawnShrineOfType(Game g, Vector3 pos, string type, RandomNumberGenerator rng, string forcedStein = null)
    {
        string stein = forcedStein ?? (type == "plate" ? Loot.RollStein(rng, 0, g.Round, Rarity.Common, false)
            : type == "timed" ? Loot.RollStein(rng, 0, g.Round, Rarity.Rare, false)
            : rng.Randf() < 0.35f ? Loot.RollStein(rng, 0, g.Round, Rarity.Legendary) : Loot.RollStein(rng, 0, g.Round, Rarity.Rare, false));
        var space = g.World.GetWorld3D().DirectSpaceState;
        int bestDir = 0; float bestFree = -1;
        for (int d = 0; d < 4; d++)
        {
            var from = pos + new Vector3(0, 0.8f, 0);
            var q = PhysicsRayQueryParameters3D.Create(from, from + ObjUtil.DirVec(d) * 10f, 1);
            var hit = space.IntersectRay(q);
            float free = hit.Count > 0 ? from.DistanceTo(hit["position"].AsVector3()) : 10f;
            if (free > bestFree) { bestFree = free; bestDir = d; }
        }
        var dv = ObjUtil.DirVec(bestDir);
        int sid = g.ServerSpawnObject("shrine", pos, ShrineObj.MakeData(type, stein, "locked", bestDir));
        var shrine = (ShrineObj)g.World.Objects[sid];
        if (type == "plate")
        {
            float dist = Math.Clamp(bestFree - 1f, 3.5f, 6.5f);
            if (bestFree >= 6.6f) dist = Math.Max(dist, 5.8f);
            int pid = g.ServerSpawnObject("shrineplate", pos + dv * dist, "mode=plate;pressed=0");
            shrine.Plate = (ShrinePlateObj)g.World.Objects[pid];
            shrine.Plate.Shrine = shrine;
        }
        else if (type == "timed")
        {
            int bid = g.ServerSpawnObject("shrinebarrier", pos, $"open=0;dir={bestDir}");
            shrine.Barrier = (ShrineBarrierObj)g.World.Objects[bid];
            float dist = Math.Clamp(bestFree - 1f, 3.6f, 5.5f);
            int pid = g.ServerSpawnObject("shrineplate", pos + dv * dist, "mode=start;busy=0");
            shrine.Start = (ShrinePlateObj)g.World.Objects[pid];
            shrine.Start.Shrine = shrine;
        }
        return shrine;
    }
}
