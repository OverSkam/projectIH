using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Server-side scripted scenarios for tools/monsters_test.sh (--montest=NAME on the host; honoured only by the server).
/// Each scenario is an iterator: it yields a Wait (condition + timeout + PASS name) and the MonsterDirector polls it.
/// Bots (--bot=mon --monpos=x:z) are the real miners; monsters/noise/water are injected by the server.
/// </summary>
public static class MonsterTest
{
    public struct Wait { public Func<bool> Cond; public float Timeout; public string Name; }
    public static bool Finished;

    static Wait Until(Func<bool> c, float timeout, string pass) => new Wait { Cond = c, Timeout = timeout, Name = pass };
    static Wait Sleep(float sec) { double t0 = Time.GetTicksMsec() / 1000.0; return new Wait { Cond = () => Time.GetTicksMsec() / 1000.0 - t0 >= sec, Timeout = sec + 5, Name = null }; }
    static ServerPlayer Find(string name) => Game.I.Players.Values.FirstOrDefault(p => p.Name == name);
    static Vector3 V(float x, float z) => new Vector3(x, 0, z);
    static void Heal(ServerPlayer p) { p.Health = 100; p.Dirty = true; }
    static ServerPlayer Fake(string name = "Tester")
    {
        var f = new ServerPlayer { Name = name, Alive = true, Participant = true, Peer = -1 };
        f.Inv.Utils[0] = "pickaxe"; f.Inv.Selected = 0;
        return f;
    }
    static int OreNear(Vector3 p, float r, string ore = null) =>
        Game.I.World.Objects.Values.Count(o => o is PickupObj && o.Data.StartsWith("ore:") && (ore == null || o.Data == "ore:" + ore) && o.GlobalPosition.DistanceTo(p) < r);

    public static IEnumerable<Wait> Create(string name) => name switch
    {
        "golem" => Golem(),
        "golemhit" => GolemHit(),
        "ember" => Ember(),
        "wisp" => WispTest(),
        "dragon" => DragonTest(),
        "spawn" => SpawnTest(),
        "roam" => Roam(),
        "dark" => Dark(),
        "hall" => HallTest(),
        "lamp" => LampTest(),
        "assessor" => AssessorTest(),
        "plug" => PlugTest(),
        "plugwalk" => PlugWalk(),
        "loudloot" => LoudLoot(),
        "wicks" => WicksTest(),
        "director" => DirectorTest(),
        "replicate" => Replicate(),
        _ => null,
    };

    public static void Finish(Node n, bool ok)
    {
        if (Finished) return;
        Finished = true;
        if (ok) Util.Pass("montest_done"); else Util.Fail("montest_done");
        n.GetTree().CreateTimer(2.0).Timeout += () => n.GetTree().Quit(ok ? 0 : 1);
    }

    // ------------------------------------------------------------------ golem: hears noise, walks the nav graph, hunts, slams
    static IEnumerable<Wait> Golem()
    {
        var a = Find("Alice");
        yield return Until(() => a.Alive && a.Pos.DistanceTo(V(36, -2)) < 3f, 40, "alice_in_position");
        var gol = MonsterModule.Spawn<GolemObj>("golem", V(32, 26));
        gol.Blind = true;
        var start = gol.GlobalPosition;
        yield return Sleep(1.5f);
        Game.ServerNoise(a.Pos, 70f, a);
        yield return Until(() => gol.State == MonsterState.Investigate, 3, "golem_hears_noise");
        yield return Until(() => gol.GlobalPosition.DistanceTo(start) > 8f, 15, "golem_approaches");
        gol.Blind = false;
        float hp0 = a.Health;
        yield return Until(() => a.Health < hp0 || !a.Alive, 40, "golem_damages_miner");
        gol.Frozen = true;
        yield return Sleep(1.0f); // let the client see the damage before healing
        Heal(a);
        var fake = Fake();
        // front: hits do nothing at all
        fake.Pos = gol.GlobalPosition + gol.Forward * 2f;
        for (int i = 0; i < 5; i++) gol.ServerHit(fake, 1f, true);
        yield return Until(() => gol.State != MonsterState.Stunned, 1, "golem_front_hit_ignored");
        // back: the crystal stuns it (and only stuns it)
        fake.Pos = gol.GlobalPosition - gol.Forward * 2f;
        gol.ServerHit(fake, 1f, true);
        yield return Until(() => gol.State == MonsterState.Stunned, 1, "golem_stunned_by_back_hit");
        var stunPos = gol.GlobalPosition;
        for (int i = 0; i < 10; i++) gol.ServerHit(fake, 1f, true);   // no killing it however hard you hit
        yield return Sleep(2f);
        yield return Until(() => gol.State == MonsterState.Stunned && gol.GlobalPosition.DistanceTo(stunPos) < 0.05f, 1, "golem_stunned_stays_put");
        yield return Until(() => gol.State != MonsterState.Stunned, MonsterTuning.GolemStunSeconds + 3f, "golem_recovers_after_stun");
        yield return Until(() => OreNear(gol.GlobalPosition, 4f) == 0 && Game.I.World.Objects.ContainsKey(gol.Id), 1, "golem_unkillable_no_loot");
        // dynamite and bear traps stun, never damage
        gol.OnExplosion(gol.GlobalPosition + V(3, 0), 6.5f, 70f);
        yield return Until(() => gol.State == MonsterState.Stunned, 1, "golem_stunned_by_blast");
        yield return Until(() => gol.State != MonsterState.Stunned, 6, "golem_blast_stun_ends");
        gol.ServerRoot(3f);
        yield return Until(() => gol.State == MonsterState.Stunned, 1, "golem_stunned_by_beartrap");
    }

    // ------------------------------------------------------------------ golem: real melee through RequestAttack from a bot
    static IEnumerable<Wait> GolemHit()
    {
        var a = Find("Alice");
        yield return Until(() => a.Alive && a.Pos.DistanceTo(V(36, -2)) < 3f, 40, "alice_in_position");
        var gol = MonsterModule.Spawn<GolemObj>("golem", V(36, -4.4f));
        gol.Frozen = true;
        gol.Yaw = Mathf.Pi; // faces +Z, i.e. toward Alice: she hits the armour
        yield return Sleep(0.5f);
        yield return Sleep(5f);   // she keeps swinging at its armoured front
        yield return Until(() => gol.State != MonsterState.Stunned, 1, "golem_front_real_hits_do_nothing");
        gol.Yaw = 0f; // turns its back on her
        yield return Until(() => gol.State == MonsterState.Stunned, 10, "golem_back_real_hit_stuns");
        yield return Until(() => Game.I.World.Objects.ContainsKey(gol.Id) && OreNear(V(36, -4.4f), 5f) == 0, 1, "golem_survives_no_loot");
    }

    // ------------------------------------------------------------------ ember
    static IEnumerable<Wait> Ember()
    {
        var a = Find("Alice");
        var cave = Game.I.World.Cave;
        yield return Until(() => a.Alive && a.Pos.DistanceTo(V(36, -2)) < 3f, 40, "alice_in_position");
        // 1. dies instantly in water, drops ruby
        cave.WaterZones.Add(new Aabb(new Vector3(26, -1, -9), new Vector3(3, 3, 3)));
        var e1 = MonsterModule.Spawn<EmberObj>("ember", new Vector3(27.5f, 0, -7.5f));
        yield return Until(() => e1.State == MonsterState.Stunned, 3, "ember_doused_in_water");
        // 2. burns a miner
        var e2 = MonsterModule.Spawn<EmberObj>("ember", V(36, 5));
        yield return Until(() => a.Health < 100f && MonsterModule.IsBurning(a.Peer), 12, "ember_burns_miner");
        float hb = a.Health;
        yield return Until(() => a.Health < hb - 3f, 5, "ember_burn_damage_over_time");
        var fake = Fake();
        fake.Pos = e2.GlobalPosition + V(1, 0);
        e2.ServerHit(fake, 1f, true);
        yield return Until(() => Game.I.World.Objects.ContainsKey(e2.Id) && e2.State != MonsterState.Stunned, 1, "ember_unkillable_by_melee");
        Heal(a);
        // 3. chasing through a pool kills it; wandering ember avoids water (WanderOk / Allowed)
        cave.WaterZones.Add(new Aabb(new Vector3(31, -1, -8), new Vector3(2, 3, 12)));
        var e3 = MonsterModule.Spawn<EmberObj>("ember", V(27, -2));
        yield return Until(() => e3.State == MonsterState.Stunned, 12, "ember_doused_chasing_into_water");
    }

    // ------------------------------------------------------------------ wisp
    static IEnumerable<Wait> WispTest()
    {
        var a = Find("Alice"); var b = Find("Bob");
        yield return Until(() => a.Alive && b.Alive && a.Pos.DistanceTo(V(36, -2)) < 3f && b.Pos.DistanceTo(V(40, -2)) < 3f, 40, "bots_in_position");
        b.Stats.GasImmune = true; // stands in for a gas mask (not implemented yet)
        var w2 = MonsterModule.Spawn<WispObj>("wisp", V(26, -8));
        w2.Blind = true;
        var s2 = w2.GlobalPosition;
        yield return Sleep(0.5f);
        Game.ServerNoise(V(36, -2), 30f, a);
        yield return Until(() => w2.State == MonsterState.Investigate && w2.GlobalPosition.DistanceTo(s2) > 2f, 8, "wisp_attracted_to_noise");
        Game.I.ServerDespawnObject(w2.Id);
        var w = MonsterModule.Spawn<WispObj>("wisp", V(38, -2));
        w.Frozen = true;
        yield return Until(() => a.Health < 100f, 6, "wisp_damages_miner");
        yield return Sleep(3f);
        yield return Until(() => b.Health >= 100f && a.Health < 90f, 1, "wisp_gasimmune_safe");
        var fake = Fake();
        fake.Pos = w.GlobalPosition + V(1, 0);
        w.ServerHit(fake, 1f, true); w.ServerHit(fake, 1f, true);
        yield return Until(() => w.State != MonsterState.Dispersed && w.State != MonsterState.Stunned && Game.I.World.Objects.ContainsKey(w.Id), 1, "wisp_unkillable_by_melee");
        w.Disperse(3f);
        float h1 = a.Health;
        yield return Sleep(2.4f);
        yield return Until(() => a.Health >= h1, 1, "wisp_dispersed_harmless");
        yield return Until(() => w.State != MonsterState.Dispersed, 4, "wisp_reforms");
    }

    // ------------------------------------------------------------------ dragon
    static IEnumerable<Wait> DragonTest()
    {
        var a = Find("Alice");
        yield return Until(() => a.Alive && a.Pos.DistanceTo(V(36, 36)) < 3f, 45, "alice_in_lair");
        var dragonPos = V(32, 26);
        var d = MonsterModule.SpawnDragon(dragonPos);
        yield return Until(() => OreNear(dragonPos, 8f) >= MonsterTuning.DragonHoardCount, 2, "dragon_hoard_spawned");
        yield return Sleep(7f);
        yield return Until(() => d.State == MonsterState.Asleep && d.Meter < 1f && a.Health >= 100f, 1, "dragon_sleeps_when_quiet");
        for (int i = 0; i < 8 && d.State == MonsterState.Asleep; i++)
        {
            Game.ServerNoise(a.Pos, 40f, a);
            yield return Sleep(0.6f);
        }
        yield return Until(() => d.State != MonsterState.Asleep, 2, "dragon_wakes_from_noise");
        yield return Until(() => a.Health < 100f, 14, "dragon_breath_damages_miner");
        yield return Until(() => d.Lair.Grow(0.1f).HasPoint(new Vector3(d.GlobalPosition.X, d.Lair.Position.Y + 1f, d.GlobalPosition.Z)), 1, "dragon_stays_in_lair");
        var fake = Fake(); fake.Pos = d.GlobalPosition - d.Forward * 4f;
        var st0 = d.State;
        d.ServerHit(fake, 1f, true); d.OnExplosion(d.GlobalPosition, 6f, 70f); d.ServerRoot(3f);
        yield return Until(() => d.State == st0 && Game.I.World.Objects.ContainsKey(d.Id), 1, "dragon_unaffected_by_hits_blasts_traps");
    }

    // ------------------------------------------------------------------ marker spawning is deterministic from the rng
    static IEnumerable<Wait> SpawnTest()
    {
        yield return Sleep(1f);
        var kinds = MonsterObj.Live.Select(m => m.Kind).OrderBy(k => k).ToList();
        Util.Log("MONSTER: spawn list round1 = " + string.Join(",", kinds));
        yield return Until(() => true, 1, kinds.Count > 0 ? "markers_spawn_monsters" : "markers_spawn_nothing");
    }

    // ================================================================== v2.1 scenarios (generated cave)
    static MonsterNav Nav => MonsterNav.For(Game.I.World.Cave);
    static Vector3 Deep(float h, int v = 0) => Nav.DeepNode(h, v);
    static float FlatD(Vector3 a, Vector3 b) => new Vector3(a.X - b.X, 0, a.Z - b.Z).Length();
    static double Clock => Time.GetTicksMsec() / 1000.0;
    static void Remove(MonsterObj m) { if (m != null && Game.I.World.Objects.ContainsKey(m.Id)) Game.I.ServerDespawnObject(m.Id); }

    /// <summary>A node the profile can reach, at least minPath path metres from p but at most maxStraight metres in a straight line.</summary>
    static Vector3 FarFrom(Vector3 p, float minPath, float maxStraight, NavProfile pr, bool tunnel = false)
    {
        var nav = Nav; int s = nav.Nearest(pr, p);
        var d = nav.Distances(pr, s);
        int best = -1; float bd = float.MaxValue;
        for (int k = 0; k < d.Length; k++)
        {
            if (float.IsPositiveInfinity(d[k]) || d[k] < minPath || FlatD(nav.Pos[k], p) > maxStraight || (tunnel && !nav.Tunnel[k])) continue;
            if (d[k] < bd) { bd = d[k]; best = k; }
        }
        if (best < 0)
        {
            Util.Log("MONTEST: no node satisfies the distances, using the farthest reachable one");
            for (int k = 0; k < d.Length; k++) if (!float.IsPositiveInfinity(d[k]) && (best < 0 || d[k] > d[best])) best = k;
        }
        return nav.Pos[best];
    }

    /// <summary>A node between dmin and dmax metres from p with a clear line of sight to p.</summary>
    static Vector3 NodeNear(MonsterObj by, Vector3 p, float dmin, float dmax)
    {
        var nav = Nav; var space = by.GetViewport().World3D.DirectSpaceState;
        for (int k = 0; k < nav.Count; k++)
        {
            float d = FlatD(nav.Pos[k], p);
            if (d < dmin || d > dmax || !nav.Ok(NavProfile.Tall, k) || Mathf.Abs(nav.Pos[k].Y - p.Y) > 0.6f) continue;
            var q = PhysicsRayQueryParameters3D.Create(nav.Pos[k] + new Vector3(0, 1.2f, 0), p + new Vector3(0, 1.2f, 0), Layers.World);
            if (space.IntersectRay(q).Count == 0) return nav.Pos[k];
        }
        return Vector3.Zero;
    }

    static float ZoneDist(Aabb z, Vector3 p)
    {
        float dx = Mathf.Max(Mathf.Max(z.Position.X - p.X, 0), p.X - (z.Position.X + z.Size.X));
        float dz = Mathf.Max(Mathf.Max(z.Position.Z - p.Z, 0), p.Z - (z.Position.Z + z.Size.Z));
        return new Vector2(dx, dz).Length();
    }

    static T Mk<T>(string kind, Vector3 pos, Vector3 face) where T : MonsterObj
    {
        var m = MonsterModule.Spawn<T>(kind, pos);
        var d = face - pos; m.Yaw = Mathf.Atan2(-d.X, -d.Z);
        return m;
    }

    static NavProfile ProfileFor(MonsterObj m) => m is WicksObj ? NavProfile.Swarm : m is LamplighterObj || m is AssessorObj ? NavProfile.Person : NavProfile.Tall;

    // ------------------------------------------------------------------ roaming: a far monster reaches a noisy lit miner; a deaf, blind one patrols the whole cave
    static IEnumerable<Wait> Roam()
    {
        var a = Find("Alice"); var spot = Deep(70f);
        yield return Until(() => a.Alive && FlatD(a.Pos, spot) < 3f, 120, "alice_in_position");
        var start = FarFrom(spot, 45f, 52f, NavProfile.Tall);
        Util.Log($"MONTEST: roam golem starts {FlatD(start, spot):0} m straight from Alice");
        var gol = Mk<GolemObj>("golem", start, spot);
        yield return Sleep(0.5f);
        Game.ServerNoise(a.Pos, 60f, a);
        yield return Until(() => gol.State == MonsterState.Investigate || gol.State == MonsterState.Chase, 3, "roam_hears_noise_far_away");
        yield return Until(() => FlatD(gol.GlobalPosition, a.Pos) < 4.5f || a.Health < 100f, 80, "roam_golem_reaches_noisy_lit_miner");
        gol.Frozen = true; yield return Sleep(1f); Heal(a); Remove(gol);
        // a blind golem that hears nothing still covers the cave: far beyond the old 10 m home radius
        var b = Mk<GolemObj>("golem", start, spot); b.Blind = true;
        var p0 = b.GlobalPosition; float travelled = 0; var last = p0; var rooms = new HashSet<int>(); double t0 = Clock; float maxFromStart = 0;
        yield return Until(() =>
        {
            travelled += (b.GlobalPosition - last).Length(); last = b.GlobalPosition;
            var r = Game.I.World.Cave.RoomAt(b.GlobalPosition); if (r != null) rooms.Add(r.Id);
            maxFromStart = Mathf.Max(maxFromStart, FlatD(b.GlobalPosition, p0));
            return Clock - t0 > 45;
        }, 60, null);
        Util.Log($"MONTEST: patrol travelled={travelled:0} m rooms={rooms.Count} farthest={maxFromStart:0} m");
        yield return Until(() => travelled > 100f && maxFromStart > 25f && rooms.Count >= 3, 1, "roam_patrols_whole_cave");
        Remove(b);
    }

    // ------------------------------------------------------------------ dark and still: ignored at 4 m, noticed at 1.5 m
    static IEnumerable<Wait> Dark()
    {
        var a = Find("Alice"); var spot = Deep(60f);
        yield return Until(() => a.Alive && FlatD(a.Pos, spot) < 3f && !MonsterSenses.Lit(a) && MonsterSenses.Speed(a) < 0.3f, 120, "alice_dark_and_still");
        yield return Sleep(1f);
        var gol = MonsterModule.Spawn<GolemObj>("golem", spot);
        var at4 = NodeNear(gol, a.Pos, 3.5f, 4.8f);
        gol.GlobalPosition = at4; gol.Yaw = Mathf.Atan2(-(a.Pos.X - at4.X), -(a.Pos.Z - at4.Z));
        gol.TestHold(60f);
        yield return Sleep(7f);
        yield return Until(() => gol.State == MonsterState.Idle && a.Health >= 100f && !MonsterSenses.Lit(a), 1, "dark_still_miner_ignored_at_4m");
        var at15 = NodeNear(gol, a.Pos, 1.2f, 1.9f);
        gol.GlobalPosition = at15; gol.Yaw = Mathf.Atan2(-(a.Pos.X - at15.X), -(a.Pos.Z - at15.Z));
        yield return Until(() => gol.State == MonsterState.Chase || gol.State == MonsterState.Windup || gol.State == MonsterState.Attack, 3, "dark_still_miner_seen_at_1_5m");
        gol.Frozen = true; Remove(gol);
    }

    // ------------------------------------------------------------------ the Hall is never entered
    static IEnumerable<Wait> HallTest()
    {
        var a = Find("Alice"); var cave = Game.I.World.Cave; var nav = Nav;
        yield return Until(() => a.Alive && cave.InHall(a.Pos), 20, "alice_in_hall");
        var picks = new List<Vector3>();
        for (int k = 0; k < nav.Count && picks.Count < 5; k++)
        {
            if (nav.HallDist[k] < 3f || nav.HallDist[k] > 14f || !nav.Ok(NavProfile.Tall, k)) continue;
            bool far = true; foreach (var q in picks) if (q.DistanceTo(nav.Pos[k]) < 5f) far = false;
            if (far) picks.Add(nav.Pos[k]);
        }
        while (picks.Count < 5) picks.Add(picks[0]);
        var hallC = cave.HallZone.GetCenter();
        var mons = new List<MonsterObj>
        {
            Mk<GolemObj>("golem", picks[0], hallC), Mk<WicksObj>("wicks", picks[1], hallC), Mk<PlugObj>("plug", picks[2], hallC),
            Mk<LamplighterObj>("lamplighter", picks[3], hallC), Mk<AssessorObj>("assessor", picks[4], hallC),
        };
        mons[0].Blind = true;
        foreach (var m in mons) { m.Dispatch(hallC); Game.ServerNoise(hallC, 40f, a); }
        a.TributeWeight = 50;   // the Assessor would love to take it, but she stands in the Hall
        bool bad = false; float minGolem = 999; double t0 = Clock;
        while (Clock - t0 < 30)
        {
            foreach (var m in mons)
            {
                if (cave.HallZone.HasPoint(m.GlobalPosition)) bad = true;
                if (m is GolemObj) minGolem = Mathf.Min(minGolem, ZoneDist(cave.HallZone, m.GlobalPosition));
            }
            yield return Sleep(0.1f);
        }
        Util.Log($"MONTEST: hall edge closest approach (golem) = {minGolem:0.0} m");
        yield return Until(() => !bad && a.Health >= 100f, 1, "hall_never_entered");
        yield return Until(() => minGolem < 6f, 1, "monster_stops_at_hall_edge");
        foreach (var m in mons) Remove(m);
    }

    // ------------------------------------------------------------------ the Lamplighter
    static IEnumerable<Wait> LampTest()
    {
        var a = Find("Alice"); var spot = Deep(60f);
        yield return Until(() => a.Alive && FlatD(a.Pos, spot) < 3f, 120, "alice_in_position");
        yield return Sleep(1f);
        var lamp = MonsterModule.Spawn<LamplighterObj>("lamplighter", spot);
        var at6 = NodeNear(lamp, a.Pos, 5f, 7f);
        lamp.GlobalPosition = at6; lamp.TestHold(120f);
        yield return Sleep(3f);
        yield return Until(() => lamp.State == MonsterState.Idle && a.Health >= 100f, 1, "lamplighter_ignores_miner_beyond_3m");
        lamp.ServerHit(a, 1f, true);   // she attacks it from 6 m: it grabs her
        yield return Until(() => lamp.State == MonsterState.Windup, 1, "lamplighter_snuffs_when_attacked");
        yield return Until(() => lamp.State == MonsterState.Attack, 3, "lamplighter_grabs_attacker");
        yield return Until(() => a.Health <= 66f, 4, "lamplighter_deals_40_damage");
        yield return Until(() => lamp.State == MonsterState.Recover, 4, "lamplighter_lets_go_and_walks_away");
        var rel = lamp.GlobalPosition;
        yield return Until(() => FlatD(lamp.GlobalPosition, rel) > 7f, 14, "lamplighter_walks_away_far");
        yield return Until(() => lamp.State == MonsterState.Idle, 25, "lamplighter_relights_and_stands");
        Heal(a); yield return Sleep(1f);
        var at25 = NodeNear(lamp, a.Pos, 2.2f, 2.7f);
        lamp.ResetCooldown(); lamp.GlobalPosition = at25; lamp.TestHold(60f);
        yield return Until(() => lamp.State == MonsterState.Windup || lamp.State == MonsterState.Attack, 3, "lamplighter_triggers_within_3m");
        yield return Until(() => lamp.State == MonsterState.Attack, 3, "lamplighter_second_grab");
        lamp.Frozen = true;
    }

    // ------------------------------------------------------------------ the Assessor
    static IEnumerable<Wait> AssessorTest()
    {
        var a = Find("Alice"); var b = Find("Bob"); var spotA = Deep(60f); var spotB = Deep(60f, 1);
        yield return Until(() => a.Alive && b.Alive && FlatD(a.Pos, spotA) < 3f && FlatD(b.Pos, spotB) < 3f, 150, "bots_in_position");
        Util.Log($"MONTEST: tribute Alice={AssessorHooks.TributeOf(a)} Bob={AssessorHooks.TributeOf(b)}");
        var bobWeight = b.TributeWeight; int bobStones = b.Inv.Sack.Count; int aliceWeight = a.TributeWeight; float aliceBurden = a.Burden;
        var start = FarFrom(spotA, 40f, 70f, NavProfile.Person);
        var ass = Mk<AssessorObj>("assessor", start, spotA);
        float maxSpeed = 0; var last = ass.GlobalPosition; double lt = Clock;
        bool Track()
        {
            double now = Clock; float dt = (float)(now - lt); lt = now;
            if (dt > 0.001f && dt < 0.5f) { float sp = (ass.GlobalPosition - last).Length() / dt; if (sp < 30f && ass.State != MonsterState.Attack) maxSpeed = Mathf.Max(maxSpeed, sp); }
            last = ass.GlobalPosition;
            return true;
        }
        yield return Until(() => { Track(); return ass.Stalking == a; }, 10, "assessor_stalks_highest_tribute");
        yield return Until(() => { Track(); return ass.State == MonsterState.Attack; }, 90, "assessor_catches_target");
        yield return Until(() => { Track(); return a.Inv.Sack.Contains("diamond"); }, 1, "assessor_still_holding_during_pin");
        yield return Until(() => { Track(); return ass.State == MonsterState.Recover; }, 6, "assessor_pin_ends");
        yield return Until(() => !a.Inv.Sack.Contains("diamond") && a.Inv.Sack.Contains("coal"), 1, "assessor_takes_the_single_highest_stone");
        yield return Until(() => b.Inv.Sack.Count == bobStones && b.TributeWeight == bobWeight && a.TributeWeight == aliceWeight - Ores.WeightOf("diamond"), 1, "assessor_credits_nobody");
        yield return Until(() => a.Burden < aliceBurden, 1, "assessor_burden_follows_the_sack");
        yield return Until(() => { Track(); return FlatD(ass.GlobalPosition, a.Pos) > 12f; }, 25, "assessor_retreats");
        Util.Log($"MONTEST: assessor max speed {ass.MaxStepSpeed:0.0} m/s (sampled {maxSpeed:0.0})");
        yield return Until(() => ass.MaxStepSpeed <= MonsterTuning.AssessorWalk + 0.3f && ass.MaxStepSpeed > 1f, 1, "assessor_never_runs");
        ass.Frozen = true;
    }

    // ------------------------------------------------------------------ the Plug
    static bool NavFits()
    {
        var n = Nav;
        for (int k = 0; k < n.Count; k++)
        {
            if (n.Crawl[k]) { if (n.Ok(NavProfile.Tall, k) || n.Ok(NavProfile.Person, k) || !n.Ok(NavProfile.Swarm, k)) return false; }
            if (n.Squeeze[k] && !n.Crawl[k] && n.Ok(NavProfile.Tall, k)) return false;
            if (n.Ceil[k] < NavProfile.Tall.MinCeil && !n.Crawl[k] && n.Ok(NavProfile.Tall, k)) return false;
            if (n.Hall[k] && (n.Ok(NavProfile.Tall, k) || n.Ok(NavProfile.Swarm, k))) return false;
        }
        return true;
    }

    static IEnumerable<Wait> PlugTest()
    {
        var a = Find("Alice"); var spot = Deep(60f);
        yield return Until(() => a.Alive && FlatD(a.Pos, spot) < 3f, 120, "alice_in_position");
        yield return Until(NavFits, 1, "nav_profiles_fit");
        var start = FarFrom(spot, 25f, 45f, NavProfile.Tall, true);
        var plug = Mk<PlugObj>("plug", start, spot);
        yield return Until(() => Nav.Tunnel[Nav.Nearest(NavProfile.Tall, plug.GlobalPosition)], 1, "plug_spawns_in_a_tunnel");
        yield return Sleep(0.5f);
        Game.ServerNoise(a.Pos, 20f, a);
        double t0 = Clock; var p0 = plug.GlobalPosition; float travelled = 0; var last = p0; float d0 = FlatD(p0, a.Pos);
        yield return Until(() => { travelled += (plug.GlobalPosition - last).Length(); last = plug.GlobalPosition; return Clock - t0 > 12; }, 20, null);
        Util.Log($"MONTEST: plug travelled {travelled:0.0} m in 12 s, distance to the noise {d0:0.0} -> {FlatD(plug.GlobalPosition, a.Pos):0.0}");
        yield return Until(() => travelled > 7f && travelled < 14f, 1, "plug_creeps_about_1_mps");
        yield return Until(() => FlatD(plug.GlobalPosition, a.Pos) < d0 - 4f, 1, "plug_creeps_toward_the_noise");
        // solid: a ray along its length hits the plug itself
        bool hit;
        {
            var space = plug.GetViewport().World3D.DirectSpaceState;
            var fwd = plug.Forward;
            var q = PhysicsRayQueryParameters3D.Create(plug.GlobalPosition + fwd * 3f + new Vector3(0, 1.1f, 0), plug.GlobalPosition - fwd * 3f + new Vector3(0, 1.1f, 0), Layers.World);
            var r = space.IntersectRay(q);
            hit = r.Count > 0 && r["collider"].AsGodotObject() == plug;
        }
        yield return Until(() => hit, 1, "plug_body_blocks_the_tunnel");
        // short stun only
        plug.Stun(6f);
        yield return Until(() => plug.State == MonsterState.Stunned, 1, "plug_stun_applied");
        yield return Until(() => plug.State != MonsterState.Stunned, 2, "plug_stun_is_short");
        // dynamite shoves it back
        var before = plug.GlobalPosition;
        plug.OnExplosion(plug.GlobalPosition + plug.Forward * 2f, 6.5f, 70f);
        yield return Until(() => FlatD(plug.GlobalPosition, before) >= 3f, 5, "plug_pushed_back_by_dynamite");
        yield return Sleep(1.5f);
        // crush: on top of her
        var home = plug.GlobalPosition;
        plug.GlobalPosition = a.Pos + new Vector3(0.3f, 0, 0.3f);
        float hp = a.Health;
        yield return Until(() => a.Health <= hp - 15f, 3, "plug_crushes_20_dps");
        plug.GlobalPosition = home; plug.Frozen = true; Heal(a);
    }

    // ------------------------------------------------------------------ Wicks
    static IEnumerable<Wait> WicksTest()
    {
        var a = Find("Alice"); var spot = Deep(60f);
        yield return Until(() => a.Alive && FlatD(a.Pos, spot) < 3f, 120, "alice_in_position");
        // crawl: the swarm walks through a crawl space that nothing else can use
        var mc = ModularCave.Last; var tun = mc?.Tunnels.FirstOrDefault(t => t.Kind == 1);
        if (tun == null) yield return Until(() => true, 1, "wicks_crawl_skipped_no_crawl_in_this_cave");
        else
        {
            var c0 = ModularCave.CellPos(tun.Cells[0].i, tun.Cells[0].j, 0); var c3 = ModularCave.CellPos(tun.Cells[3].i, tun.Cells[3].j, 0);
            var wc = MonsterModule.Spawn<WicksObj>("wicks", c0);
            wc.GlobalPosition = Nav.Pos[Nav.Nearest(NavProfile.Swarm, c0)];
            wc.Blind = true; wc.Dispatch(c3);
            bool crossed = false;
            yield return Until(() =>
            {
                var cell = mc.At((int)Mathf.Round(wc.GlobalPosition.X / 2f), (int)Mathf.Round(wc.GlobalPosition.Z / 2f));
                if (cell.Crawl) crossed = true;
                return FlatD(wc.GlobalPosition, c3) < 1.8f;
            }, 25, "wicks_reach_far_side_of_crawl");
            yield return Until(() => crossed, 1, "wicks_pass_through_crawl_cells");
            Remove(wc);
        }
        var start = FarFrom(spot, 35f, 34f, NavProfile.Swarm);
        var w = Mk<WicksObj>("wicks", start, spot);
        yield return Until(() => FlatD(w.GlobalPosition, a.Pos) < 2.5f, 60, "wicks_drawn_to_lit_lantern");
        float oil0 = a.Oil;
        yield return Until(() => a.Oil < oil0 - 25f, 10, "wicks_drain_oil_fast");
        yield return Until(() => a.Health <= 96f, 6, "wicks_bite");
        a.LanternOn = false;   // switched off
        yield return Until(() => w.State != MonsterState.Attack && w.State != MonsterState.Chase, 12, "wicks_lose_interest_when_lantern_off");
        w.Frozen = true;
    }

    // ------------------------------------------------------------------ the density director
    static IEnumerable<Wait> DirectorTest()
    {
        var a = Find("Alice"); var spot = Deep(60f);
        yield return Until(() => a.Alive && FlatD(a.Pos, spot) < 3f, 120, "alice_in_position");
        var start = FarFrom(spot, 70f, 120f, NavProfile.Tall);
        var gol = Mk<GolemObj>("golem", start, spot);
        gol.Blind = true; gol.TestHold(1000f);
        double t0 = Clock; float d0 = FlatD(start, a.Pos);
        Util.Log($"MONTEST: director test, golem {d0:0} m away, quiet={Util.Arg("dirquiet", "60")}");
        yield return Until(() => MonsterDirector.Sent > 0, 120, "director_sent_a_monster_after_quiet");
        double sentAt = MonsterDirector.SentAt;
        float quiet = float.TryParse(Util.Arg("dirquiet", "60"), out var qq) ? qq : 60f;
        yield return Until(() => sentAt >= quiet - 2f, 1, "director_waited_for_the_quiet_period");
        yield return Until(() => gol.State == MonsterState.Investigate, 2, "director_monster_heads_to_the_miner");
        yield return Until(() => FlatD(gol.GlobalPosition, a.Pos) < d0 - 20f, 60, "director_monster_closes_in");
        gol.Frozen = true;
    }

    // ------------------------------------------------------------------ everything replicates to a client
    static IEnumerable<Wait> Replicate()
    {
        var a = Find("Alice"); var spot = Deep(60f);
        yield return Until(() => a.Alive && FlatD(a.Pos, spot) < 3f, 120, "alice_in_position");
        var kinds = new[] { "golem", "wicks", "plug", "lamplighter", "assessor" };
        var mons = new List<MonsterObj>();
        var used = new List<Vector3>();
        foreach (var k in kinds)
        {
            var cells = Nav.SpawnCells(k == "wicks" ? NavProfile.Swarm : NavProfile.Tall, 1, 25f, 10f, new RandomNumberGenerator { Seed = (ulong)(k.Length * 77) }, used, k == "plug");
            used.Add(cells[0]);
            mons.Add(MonsterModule.Spawn<MonsterObj>(k, cells[0]));
        }
        yield return Sleep(1.0f);
        foreach (var m in mons) m.Dispatch(FarFrom(m.GlobalPosition, 30f, 120f, ProfileFor(m)));
        yield return Until(() => mons.All(m => FlatD(m.GlobalPosition, m.Home) > 2.5f), 60, "all_monster_kinds_moved_on_host");
        // state changes: stun the golem, trigger the lamplighter
        mons[0].Stun(2f);
        var lamp = (LamplighterObj)mons[3];
        lamp.GlobalPosition = NodeNear(lamp, a.Pos, 5f, 7f); lamp.TestHold(30f);
        yield return Sleep(0.5f);
        lamp.ServerHit(a, 1f, true);
        yield return Until(() => lamp.State == MonsterState.Attack, 4, "lamplighter_grab_for_replication");
        yield return Sleep(3f);
        foreach (var m in mons) m.Frozen = true;
        Heal(a);
    }

    // ------------------------------------------------------------------ a real miner body walks into the Plug: it blocks, nobody is launched or pushed through
    static IEnumerable<Wait> PlugWalk()
    {
        var a = Find("Alice"); var mc = ModularCave.Last; var tun = mc.Tunnels.First(x => x.Kind == 0);
        var c0 = ModularCave.CellPos(tun.Cells[0].i, tun.Cells[0].j, 0f); var c2 = ModularCave.CellPos(tun.Cells[2].i, tun.Cells[2].j, 0f); var c3 = ModularCave.CellPos(tun.Cells[3].i, tun.Cells[3].j, 0f);
        var axis = (c3 - c0).Normalized();
        yield return Until(() => a.Alive && FlatD(a.Pos, c0) < 1.5f, 150, "alice_at_tunnel_mouth");
        var plug = MonsterModule.Spawn<PlugObj>("plug", new Vector3(c2.X, Nav.Pos[Nav.Nearest(NavProfile.Tall, c2)].Y, c2.Z));
        plug.Yaw = Mathf.Atan2(-axis.X, -axis.Z);
        plug.Frozen = true;
        float plugS = (plug.GlobalPosition - c0).Dot(axis);
        var last = a.Pos; float maxJump = 0, maxS = -99; double t0 = Clock, lastT = Clock;
        yield return Until(() =>
        {
            if (Clock - lastT >= 0.1) { maxJump = Mathf.Max(maxJump, (a.Pos - last).Length()); last = a.Pos; lastT = Clock; }
            maxS = Mathf.Max(maxS, (a.Pos - c0).Dot(axis));
            Heal(a);
            return Clock - t0 > 12;
        }, 30, null);
        Util.Log($"MONTEST: plugwalk frozen: miner reached s={maxS:0.00} (plug centre at {plugS:0.00}), max 0.1 s jump {maxJump:0.00} m");
        yield return Until(() => maxS < plugS - 0.3f, 1, "miner_cannot_pass_the_plug");
        yield return Until(() => maxJump < 1.2f, 1, "miner_not_launched_by_the_plug");
        // now it creeps into the pressed miner
        plug.Frozen = false; plug.Dispatch(c0 - axis * 3f); t0 = Clock; maxJump = 0; maxS = -99; lastT = Clock; last = a.Pos;
        yield return Until(() =>
        {
            if (Clock - lastT >= 0.1) { maxJump = Mathf.Max(maxJump, (a.Pos - last).Length()); last = a.Pos; lastT = Clock; }
            maxS = Mathf.Max(maxS, (a.Pos - c0).Dot(axis));
            Heal(a);
            return Clock - t0 > 10;
        }, 30, null);
        float plugS2 = (plug.GlobalPosition - c0).Dot(axis);
        Util.Log($"MONTEST: plugwalk creeping: miner max s={maxS:0.00}, plug now at {plugS2:0.00}, max 0.1 s jump {maxJump:0.00} m");
        yield return Until(() => maxJump < 1.8f, 1, "miner_not_launched_by_creeping_plug");
        yield return Until(() => (a.Pos - c0).Dot(axis) < plugS2 + 0.1f, 1, "miner_pushed_back_not_through");
        plug.Frozen = true;
    }

    // ------------------------------------------------------------------ loot noise pulls monsters: a legendary geode reveal (45 m) and a mimic shriek (70 m)
    static IEnumerable<Wait> LoudLoot()
    {
        var a = Find("Alice"); var spot = Deep(60f);
        yield return Until(() => a.Alive && FlatD(a.Pos, spot) < 3f, 150, "alice_in_position");
        var far = FarFrom(spot, 30f, 42f, NavProfile.Tall);
        var gol = Mk<GolemObj>("golem", far, spot); gol.Blind = true; gol.TestHold(1000f);
        int gid = Game.I.ServerSpawnObject("geode", spot + new Vector3(1.5f, 0.1f, 0), GeodeObj.MakeData(1, 2));
        var ge = (GeodeObj)Game.I.World.Objects[gid];
        ge.ForcedResult = new GeodeResult { Items = { "stein:midas" } };
        yield return Sleep(1f);
        yield return Until(() => gol.State == MonsterState.Idle, 1, "golem_waits_before_the_reveal");
        float d0 = FlatD(gol.GlobalPosition, a.Pos);
        ge.Crack(a);   // the legendary reveal: Game.ServerNoise(45 m)
        yield return Until(() => gol.State == MonsterState.Investigate, 3, "reveal_noise_pulls_a_far_golem");
        yield return Until(() => FlatD(gol.GlobalPosition, a.Pos) < d0 - 12f, 40, "golem_walks_toward_the_reveal");
        Remove(gol);
        var far2 = FarFrom(spot, 50f, 68f, NavProfile.Tall);
        var g2 = Mk<GolemObj>("golem", far2, spot); g2.Blind = true; g2.TestHold(1000f);
        yield return Sleep(0.5f);
        Game.ServerNoise(spot, LootTuning.ShriekNoise, null);   // a mimic's shriek
        yield return Until(() => g2.State == MonsterState.Investigate, 3, "mimic_shriek_pulls_a_far_golem");
        g2.Frozen = true;
        // mimics only settle where there is room all round (1 m clear), so a one-wide tunnel can never be plugged by one
        var nav = Nav; int tunnelNodes = 0, roomyTunnelNodes = 0;
        for (int k = 0; k < nav.Count; k++) if (nav.Tunnel[k]) { tunnelNodes++; if (LootPlacement.Clear(Game.I.World, nav.Pos[k] + new Vector3(0, 0.05f, 0), 1.0f)) roomyTunnelNodes++; }
        Util.Log($"MONTEST: tunnel nodes={tunnelNodes} with 1 m clear all round={roomyTunnelNodes}");
        yield return Until(() => roomyTunnelNodes == 0, 1, "mimic_cannot_settle_in_a_tunnel");
    }
}
