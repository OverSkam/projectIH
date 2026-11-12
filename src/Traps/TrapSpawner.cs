using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Registration of everything the Traps workstream adds.</summary>
[ContentModule]
public static class TrapsModule
{
    public static void Register()
    {
        NetObject.Factories["trap_spikes"] = () => new SpikePlateObj();
        NetObject.Factories["trap_gas"] = () => new GasVentObj();
        NetObject.Factories["trap_darts"] = () => new DartWireObj();
        NetObject.Factories["trap_rockfall"] = () => new RockfallObj();
        NetObject.Factories["trap_trapdoor"] = () => new TrapdoorObj();
        NetObject.Factories["trap_gate"] = () => new GateObj();
        NetObject.Factories["trap_lever"] = () => new LeverObj();
        NetObject.Factories["trap_cuttable"] = () => new CuttableObj();

        if (!Util.HasArg("notraps"))
        {
            Game.MarkerSpawners["trap"] = TrapSpawner.FromTrapMarker;
            Game.MarkerSpawners["lever"] = TrapSpawner.FromLeverMarker;
        }
        Game.NoiseMade += RockfallObj.OnNoise;
        Game.RoundPopulated += TrapDebug.OnPopulated;
    }
}

/// <summary>Server-side placement of traps from cave markers (geometry is read with raycasts, never assumed).</summary>
public static class TrapSpawner
{
    static World _placedFor;
    static readonly List<Vector3> Placed = new();
    static int Features;   // traps + levers placed this round (a lever's mechanism does not count twice)

    static bool Spaced(Game g, Vector3 pos, float min = -1f)
    {
        if (_placedFor != g.World) { _placedFor = g.World; Placed.Clear(); Features = 0; }
        if (min < 0f) min = TrapTuning.MinTrapSpacing;
        foreach (var q in Placed) if (q.DistanceTo(pos) < min) return false;
        return true;
    }

    static bool NearHall(Game g, Vector3 pos)
    {
        if (_placedFor != g.World) { _placedFor = g.World; Placed.Clear(); Features = 0; }
        return g.World.Cave.HallZone.Grow(TrapTuning.HallMargin).HasPoint(pos);
    }

    /// <summary>Runs a on the server after the freshly built cave is in the physics space (raycasts miss before that).</summary>
    public static async void Defer(Game g, Action a, int extraFrames = 0)
    {
        var w = g.World;
        await g.ToSignal(g.GetTree(), SceneTree.SignalName.PhysicsFrame);
        await g.ToSignal(g.GetTree(), SceneTree.SignalName.PhysicsFrame);
        for (int i = 0; i < extraFrames; i++) await g.ToSignal(g.GetTree(), SceneTree.SignalName.PhysicsFrame);
        if (g.World != w || !GodotObject.IsInstanceValid(w) || g.Phase != GamePhase.Round) return;
        a();
    }

    // ---------------------------------------------------------------- trap markers
    public static void FromTrapMarker(Game g, RoomInfo room, SpawnMarker m, RandomNumberGenerator rng)
        => Defer(g, () => PlaceTrap(g, room, m, rng), 2);   // levers (+ their mechanisms) are placed first

    static void PlaceTrap(Game g, RoomInfo room, SpawnMarker m, RandomNumberGenerator rng)
    {
        if (!Net.I.IsServer || NearHall(g, m.Pos) || !Spaced(g, m.Pos) || Features >= TrapTuning.MaxTraps(g.Round)) return;
        int round = g.Round, depth = room.Depth;
        bool safe = depth <= TrapTuning.SafeDepth;
        var table = new List<(string kind, float w)>
        {
            ("spikes", 3f), ("rockfall", 2f), ("gas", 1.5f + 0.5f * Math.Min(round, 3)),
            ("darts", 1f + 0.5f * Math.Min(round, 3)), ("trapdoor", 1f + 0.5f * Math.Min(round, 3) + (depth >= 2 ? 1f : 0f)),
        };
        if (safe) table.RemoveAll(t => t.kind != "darts");   // darts only sting; everything else is lethal
        var order = new List<string>();
        while (table.Count > 0)
        {
            float tot = table.Sum(t => t.w), r = rng.Randf() * tot;
            int i = 0;
            for (; i < table.Count - 1; i++) { r -= table[i].w; if (r <= 0) break; }
            order.Add(table[i].kind); table.RemoveAt(i);
        }
        foreach (var k in order)
            if (Spawn(g, k, m.Pos, rng, false, false) != null) { Placed.Add(m.Pos); Features++; return; }
        if (Util.HasArg("soak") || Util.HasArg("trapdebug")) { var pr = Probe.At(m.Pos); Util.Log($"TRAPREJECT trap at {m.Pos} room={room.Name} valid={pr.Valid} floor={pr.FloorY:0.0} ceil={pr.CeilH:0.0} free={pr.FreeFwd:0.#}/{pr.FreeBack:0.#} side={pr.SideP:0.#}/{pr.SideN:0.#} width={pr.Width:0.#}"); }
    }

    /// <summary>Spawns one trap of the given kind at pos. Returns the NetObject or null when the surroundings do not fit (unless force).</summary>
    public static NetObject Spawn(Game g, string kind, Vector3 pos, RandomNumberGenerator rng, bool force, bool fast, bool lever = false)
    {
        var pr = Probe.At(pos);
        if (!force && !pr.Valid) return null;
        var fp = new Vector3(pos.X, pr.FloorY, pos.Z);
        string data; string objKind;
        switch (kind)
        {
            case "spikes": objKind = "trap_spikes"; data = TrapBase.Mk(("s", "armed")); break;
            case "gas": objKind = "trap_gas"; data = TrapBase.Mk(("s", "idle"), ("r", TrapTuning.GasRadius), ("fast", fast)); break;
            case "rockfall": objKind = "trap_rockfall"; data = TrapBase.Mk(("s", "armed"), ("r", TrapTuning.RockRadius), ("ch", pr.CeilH), ("fast", fast), ("lv", lever)); break;
            case "darts":
            {
                // launcher on the roomier side, darts fly along the passage over the wire
                bool fwdFree = pr.FreeFwd >= pr.FreeBack;
                float launcherFree = fwdFree ? pr.FreeFwd : pr.FreeBack, otherFree = fwdFree ? pr.FreeBack : pr.FreeFwd;
                float a = pr.CorrAng + (fwdFree ? Mathf.Pi : 0f);
                float dl = Mathf.Min(launcherFree - 0.8f, 6.5f);
                float sideA = fwdFree ? pr.SideN : pr.SideP, sideB = fwdFree ? pr.SideP : pr.SideN; // local +Z, -Z
                float wp = Mathf.Min(sideA, 6f) - 0.05f, wn = Mathf.Min(sideB, 6f) - 0.05f;
                if (force && (dl < 2.5f || wp + wn < 1.8f)) { a = 0; dl = 5f; wp = wn = 2.5f; otherFree = 8f; }
                if (dl < 2.5f || wp + wn < 1.8f || wp + wn > 12f) return null;
                float rg = dl + Mathf.Min(otherFree - 0.3f, 8f);
                objKind = "trap_darts"; data = TrapBase.Mk(("s", "armed"), ("a", a), ("wp", wp), ("wn", wn), ("dl", dl), ("rg", rg));
                break;
            }
            case "trapdoor":
            {
                float hw = Mathf.Min(TrapTuning.PitWidth, pr.Width - 1.6f), hl = TrapTuning.PitLength;
                float a = pr.CorrAng;
                if (force && (hw < 1.4f || pr.FreeFwd < hl / 2f + 0.6f || pr.FreeBack < hl / 2f + 0.6f)) { hw = TrapTuning.PitWidth; a = 0; }
                else if (hw < 1.4f || pr.FreeFwd < hl / 2f + 0.6f || pr.FreeBack < hl / 2f + 0.6f) return null;
                objKind = "trap_trapdoor"; data = TrapBase.Mk(("s", "closed"), ("a", a), ("hl", hl), ("hw", hw), ("dp", TrapTuning.PitDepth), ("lv", lever));
                break;
            }
            default: return null;
        }
        int id = g.ServerSpawnObject(objKind, fp, data);
        return g.World.Objects.GetValueOrDefault(id);
    }

    // ---------------------------------------------------------------- lever markers
    public static void FromLeverMarker(Game g, RoomInfo room, SpawnMarker m, RandomNumberGenerator rng)
        => Defer(g, () => PlaceLever(g, room, m, rng));

    static void PlaceLever(Game g, RoomInfo room, SpawnMarker m, RandomNumberGenerator rng)
    {
        if (!Net.I.IsServer || NearHall(g, m.Pos) || !Spaced(g, m.Pos, TrapTuning.MinLeverSpacing) || Features >= TrapTuning.MaxTraps(g.Round)) return;
        var kinds = new List<string> { "gate", "collapse", "trapdoor" };
        float r = rng.Randf();
        string first = r < 0.4f ? "gate" : r < 0.75f ? "collapse" : "trapdoor";
        kinds.Remove(first); kinds.Insert(0, first);
        if (room.Depth <= TrapTuning.SafeDepth) kinds.RemoveAll(k => k != "gate");   // collapse / trapdoor levers are lethal
        // a lever marker often sits in a niche beside the passage, where the probe cannot read the passage axis: also try nearby open cells
        var mc = ModularCave.Last;
        foreach (var (dx, dz) in new[] { (0f, 0f), (2f, 0f), (-2f, 0f), (0f, 2f), (0f, -2f), (4f, 0f), (-4f, 0f), (0f, 4f), (0f, -4f) })
        {
            var at = m.Pos + new Vector3(dx, 0, dz);
            if (mc != null && !mc.OpenAt((int)Math.Round(at.X / 2f), (int)Math.Round(at.Z / 2f))) continue;
            foreach (var k in kinds)
                if (SpawnLever(g, at, k, false, false)) { Placed.Add(m.Pos); Features++; return; }
        }
        if (Util.HasArg("soak") || Util.HasArg("trapdebug")) { var pr = Probe.At(m.Pos); Util.Log($"TRAPREJECT lever at {m.Pos} room={room.Name} valid={pr.Valid} floor={pr.FloorY:0.0} ceil={pr.CeilH:0.0} free={pr.FreeFwd:0.#}/{pr.FreeBack:0.#} side={pr.SideP:0.#}/{pr.SideN:0.#} width={pr.Width:0.#}"); }
    }

    /// <summary>Lever + mechanism ("gate", "collapse", "trapdoor") some metres down the nearest passage, linked by a chain.</summary>
    static bool Rej(string why) { if (Util.HasArg("soak") || Util.HasArg("trapdebug")) Util.Log("TRAPDBG lever rejected: " + why); return false; }

    public static bool SpawnLever(Game g, Vector3 pos, string mech, bool force, bool fast)
    {
        var pr = Probe.At(pos);
        if (!pr.Valid && !force) return Rej("probe invalid");
        if (!force && !Spaced(g, pos, TrapTuning.MinLeverSpacing)) return Rej("lever spacing");
        // pedestal sits beside the passage axis, never in the way
        float sideSgn = pr.SideP >= pr.SideN ? 1f : -1f;
        float off = Mathf.Clamp(Mathf.Max(pr.SideP, pr.SideN) - 1.0f, 0f, 1.2f);
        var lp = new Vector3(pos.X, pr.FloorY, pos.Z) + TrapUtil.Dir(pr.CorrAng + Mathf.Pi / 2f) * sideSgn * off;
        // direction to the mechanism: freer side of the passage axis
        bool fwd = pr.FreeFwd >= pr.FreeBack;
        float a = pr.CorrAng + (fwd ? 0f : Mathf.Pi);
        float free = fwd ? pr.FreeFwd : pr.FreeBack;
        var dir = TrapUtil.Dir(a);
        float dist = mech switch { "gate" => Mathf.Clamp(free - 1.5f, 3.5f, 5f), "collapse" => 5.5f, _ => 7.5f };
        if (!force && free < dist + 1f) return Rej($"free {free:0.#} < dist {dist:0.#}+1 ({mech})");
        var mp = lp + dir * dist;
        var mpr = Probe.At(mp);
        if (!force && !mpr.Valid) return Rej("mech probe invalid");
        var mfp = new Vector3(mp.X, mpr.FloorY, mp.Z);
        if (!force && !Spaced(g, mfp, TrapTuning.MinMechSpacing)) return Rej($"mech spacing {mech}");   // never stack a mechanism on another trap
        if (Util.HasArg("trapdebug")) Util.Log($"TRAPDBG lever={lp} marker={pos} corr={pr.CorrAng:0.00} free={pr.FreeFwd:0.#}/{pr.FreeBack:0.#} side={pr.SideP:0.#}/{pr.SideN:0.#} mech={mfp} mw={mpr.Width:0.#} mcorr={mpr.CorrAng:0.00}");

        string data; string objKind; Vector3 anchor; float cd;
        switch (mech)
        {
            case "gate":
            {
                float w = mpr.Width + 0.4f;
                if (!force && (w < 2.4f || w > 9f)) return Rej($"gate width {w:0.#}");
                if (force && (w < 2.4f || w > 9f)) w = 4.4f;
                float gh = Mathf.Min(mpr.CeilH, 4.2f);
                float ga = mpr.CorrAng;
                if (force && w == 4.4f) ga = a;
                objKind = "trap_gate"; data = TrapBase.Mk(("s", "up"), ("a", ga), ("w", w), ("h", gh));
                anchor = mfp + TrapUtil.Dir(ga + Mathf.Pi / 2f) * (w / 2f - 0.1f) + new Vector3(0, gh - 0.2f, 0);
                cd = TrapTuning.GateWarn + TrapTuning.GateDown + TrapTuning.GateRise + TrapTuning.LeverCooldown;
                break;
            }
            case "collapse":
                objKind = "trap_rockfall"; data = TrapBase.Mk(("s", "armed"), ("r", TrapTuning.RockRadius), ("ch", mpr.CeilH), ("lv", true), ("fast", fast));
                anchor = mfp + new Vector3(0, mpr.CeilH - 0.25f, 0);
                cd = TrapTuning.CollapseLeverCooldown;
                break;
            default:
            {
                float hw = Mathf.Min(TrapTuning.PitWidth, mpr.Width - 1.6f), hl = TrapTuning.PitLength;
                float ta = mpr.CorrAng;
                if (force && (hw < 1.4f || mpr.FreeFwd < hl / 2f + 0.6f || mpr.FreeBack < hl / 2f + 0.6f)) { hw = TrapTuning.PitWidth; ta = a; }
                else if (hw < 1.4f || mpr.FreeFwd < hl / 2f + 0.6f || mpr.FreeBack < hl / 2f + 0.6f) return Rej($"trapdoor fit hw={hw:0.#} fwd={mpr.FreeFwd:0.#} back={mpr.FreeBack:0.#}");
                objKind = "trap_trapdoor"; data = TrapBase.Mk(("s", "closed"), ("a", ta), ("hl", hl), ("hw", hw), ("dp", TrapTuning.PitDepth), ("lv", true));
                anchor = mfp + new Vector3(0, 0.12f, 0);
                cd = TrapTuning.TrapdoorCreak + TrapTuning.TrapdoorLeverOpen + 5f + TrapTuning.LeverCooldown;
                break;
            }
        }
        int mid = g.ServerSpawnObject(objKind, mfp, data);
        Placed.Add(mfp);
        g.ServerSpawnObject("trap_lever", lp, TrapBase.Mk(("s", "ready"), ("mid", mid), ("mk", mech), ("cd", cd), ("fx", mfp.X), ("fz", mfp.Z), ("ax", anchor.X), ("ay", anchor.Y), ("az", anchor.Z)));
        return true;
    }
}
