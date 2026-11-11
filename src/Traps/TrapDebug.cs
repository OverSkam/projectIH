using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Server-only debug arguments (ignored on clients):
///   --spawntrap=spikes@near,gas@near,darts@camp,lever_gate@near   kinds: spikes gas darts rockfall trapdoor lever_gate lever_collapse lever_trapdoor cuttable
///                                                                   places: near (first walkable nav node outside the camp, default) | camp (every miner spawn)
///   --trapfast        short idle timers (gas) and no random rockfall
///   --trapnoise=SEC   SEC seconds after the round was populated the server makes noise (Game.ServerNoise) on the first non-lever rockfall zone
/// </summary>
public static class TrapDebug
{
    static bool _noiseArmed;

    public static void OnPopulated(Game g, RandomNumberGenerator rng)
    {
        if (!Net.I.IsServer) return;
        if (Util.HasArg("spawntrap") || Util.HasArg("trapnoise")) TrapSpawner.Defer(g, () => Populate(g, rng));
    }

    static void Populate(Game g, RandomNumberGenerator rng)
    {
        string arg = Util.Arg("spawntrap", "");
        if (arg != "")
        {
            bool fast = Util.HasArg("trapfast");
            foreach (var spec in arg.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = spec.Split('@');
                string kind = parts[0], place = parts.Length > 1 ? parts[1] : "near";
                var spots = place == "camp" ? g.World.Cave.MinerSpawns.ToList() : new List<Vector3> { NearPos(g) };
                foreach (var pos in spots) SpawnOne(g, kind, pos, rng, fast);
            }
        }
        if (Util.HasArg("trapnoise") && !_noiseArmed && double.TryParse(Util.Arg("trapnoise"), out var sec))
        {
            _noiseArmed = true;
            g.GetTree().CreateTimer(sec).Timeout += () =>
            {
                _noiseArmed = false;
                if (g.World == null) return;
                var r = g.World.Objects.Values.OfType<RockfallObj>().FirstOrDefault(o => !o.Flag("lv"));
                if (r == null) { Util.Fail("trapnoise", "no rockfall zone"); return; }
                Util.Log($"TRAPNOISE at {r.GlobalPosition}");
                Game.ServerNoise(r.GlobalPosition, 25f, null);
            };
        }
    }

    static void SpawnOne(Game g, string kind, Vector3 pos, RandomNumberGenerator rng, bool fast)
    {
        if (kind.StartsWith("lever_"))
        {
            if (!TrapSpawner.SpawnLever(g, pos, kind.Substring(6), true, fast)) Util.Fail("spawntrap", kind);
            return;
        }
        if (kind == "cuttable")
        {
            var pr = Probe.At(pos);
            var a = new Vector3(pos.X, pr.FloorY + 2.5f, pos.Z - 1.5f); var b = new Vector3(pos.X, pr.FloorY + 2.5f, pos.Z + 1.5f);
            int id = g.ServerSpawnObject("trap_cuttable", a, TrapBase.Mk(("s", "intact"), ("p", $"{a.X:0.##},{a.Y:0.##},{a.Z:0.##},{b.X:0.##},{b.Y:0.##},{b.Z:0.##}")));
            return;
        }
        if (TrapSpawner.Spawn(g, kind, pos, rng, true, fast) == null) Util.Fail("spawntrap", kind);
    }

    /// <summary>First nav node (nearest to the first miner spawn) that is outside the camp, so traps can act there.</summary>
    public static Vector3 NearPos(Game g)
    {
        var cave = g.World.Cave;
        var from = cave.MinerSpawns.Count > 0 ? cave.MinerSpawns[0] : Vector3.Zero;
        var node = cave.NavNodes.Where(n => !cave.CampZone.Grow(3f).HasPoint(n)).OrderBy(n => n.DistanceSquaredTo(from)).Cast<Vector3?>().FirstOrDefault();
        return node ?? (cave.Rooms.Count > 0 ? cave.Rooms[0].Center : from + new Vector3(14, 0, 0));
    }
}
