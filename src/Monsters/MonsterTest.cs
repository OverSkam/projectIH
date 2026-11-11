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
        // front: hits barely scratch it
        fake.Pos = gol.GlobalPosition + gol.Forward * 2f;
        float h = gol.Hp;
        for (int i = 0; i < 3; i++) gol.ServerHit(fake, 1f, true);
        yield return Until(() => h - gol.Hp <= 3f * 20f * MonsterTuning.GolemFrontMul + 0.01f && h - gol.Hp > 0f, 1, "golem_front_armoured");
        // back: the crystal
        fake.Pos = gol.GlobalPosition - gol.Forward * 2f;
        h = gol.Hp;
        gol.ServerHit(fake, 1f, true);
        yield return Until(() => h - gol.Hp >= 30f, 1, "golem_back_hit_hurts");
        for (int i = 0; i < 6 && gol.State != MonsterState.Dead; i++) gol.ServerHit(fake, 1f, true);
        yield return Until(() => gol.State == MonsterState.Dead, 2, "golem_dies_from_behind");
        yield return Until(() => OreNear(gol.GlobalPosition, 4f) >= 3, 2, "golem_drops_loot");
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
        float h0 = gol.Hp;
        yield return Until(() => gol.Hp < h0, 15, "golem_front_hit_registered");
        yield return Sleep(4f);
        yield return Until(() => h0 - gol.Hp <= 12f * 36f * MonsterTuning.GolemFrontMul + 0.01f && gol.State != MonsterState.Dead, 1, "golem_front_real_hits_barely_hurt");
        gol.Yaw = 0f; // turns its back on her
        float h1 = gol.Hp;
        yield return Until(() => h1 - gol.Hp >= 30f, 8, "golem_back_real_hit_hurts");
        yield return Until(() => gol.State == MonsterState.Dead, 15, "golem_killed_by_bot");
        yield return Until(() => OreNear(V(36, -4.4f), 5f) >= 3, 3, "golem_loot_dropped");
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
        yield return Until(() => e1.State == MonsterState.Dead, 3, "ember_dies_in_water");
        yield return Until(() => OreNear(new Vector3(27.5f, 0, -7.5f), 3f, "ruby") >= 1, 2, "ember_drops_ruby");
        // 2. burns a miner
        var e2 = MonsterModule.Spawn<EmberObj>("ember", V(36, 5));
        yield return Until(() => a.Health < 100f && MonsterModule.IsBurning(a.Peer), 12, "ember_burns_miner");
        float hb = a.Health;
        yield return Until(() => a.Health < hb - 3f, 5, "ember_burn_damage_over_time");
        var fake = Fake();
        fake.Pos = e2.GlobalPosition + V(1, 0);
        e2.ServerHit(fake, 1f, true);
        yield return Until(() => e2.State == MonsterState.Dead, 1, "ember_weak_to_melee");
        Heal(a);
        // 3. chasing through a pool kills it; wandering ember avoids water (WanderOk / Allowed)
        cave.WaterZones.Add(new Aabb(new Vector3(31, -1, -8), new Vector3(2, 3, 12)));
        var e3 = MonsterModule.Spawn<EmberObj>("ember", V(27, -2));
        yield return Until(() => e3.State == MonsterState.Dead, 12, "ember_dies_chasing_into_water");
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
        float hp = w.Hp;
        w.ServerHit(fake, 1f, true); w.ServerHit(fake, 1f, true);
        yield return Until(() => w.Hp == hp && w.State != MonsterState.Dead, 1, "wisp_unkillable_by_melee");
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
        d.ServerHit(fake, 1f, true);
        yield return Until(() => d.State != MonsterState.Dead, 1, "dragon_unkillable");
    }

    // ------------------------------------------------------------------ marker spawning is deterministic from the rng
    static IEnumerable<Wait> SpawnTest()
    {
        yield return Sleep(1f);
        var kinds = MonsterObj.Live.Select(m => m.Kind).OrderBy(k => k).ToList();
        Util.Log("MONSTER: spawn list round1 = " + string.Join(",", kinds));
        yield return Until(() => true, 1, kinds.Count > 0 ? "markers_spawn_monsters" : "markers_spawn_nothing");
    }
}
