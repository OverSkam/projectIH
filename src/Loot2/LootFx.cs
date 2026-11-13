using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Client-side effects of reveals: light, sparks, pillar of light, flying stones, floating labels, sounds, and the local screen feedback (flash, shake).
/// Everything is built in code from CPU particles, additive unshaded materials and omni lights: no renderer-specific features.</summary>
public static class LootFx
{
    static bool Headless => DisplayServer.GetName() == "headless";

    static float CamDistance(Vector3 p)
    {
        var cam = Game.I?.World?.GetViewport()?.GetCamera3D();
        return cam == null ? 999f : cam.GlobalPosition.DistanceTo(p);
    }

    /// <summary>Camera shake for the local player, falling off with distance (range = metres where it fades out).</summary>
    public static void Shake(Vector3 pos, float range, float amp, float secs)
    {
        float k = Mathf.Clamp(1f - CamDistance(pos) / range, 0f, 1f);
        if (k <= 0f) return;
        Loot2Ui.I?.Shake(amp * (0.35f + 0.65f * k), secs);
    }

    static float PeakEnergy(int tier) => tier switch { 0 => 1.0f, 1 => 6f, 2 => 11f, _ => 16f };

    public static void Reveal(Vector3 pos, int tier, Color col, int[] ids, string[] items, Vector3[] land)
    {
        var w = Game.I.World;
        tier = Mathf.Clamp(tier, 0, 3);
        string snd = tier switch { 0 => SfxNames.RevealDull, 1 => SfxNames.RevealBright, 2 => SfxNames.RevealGem, _ => SfxNames.RevealStein };
        Sfx.Play(snd, pos + Vector3.Up * 0.4f);
        var root = new Node3D { Name = "Reveal" };
        w.AddChild(root);
        root.GlobalPosition = pos;
        float life = Math.Max(LootTuning.RevealSeconds[tier] + 0.6f, 3.4f);
        var tw = root.CreateTween();
        tw.TweenInterval(life); tw.TweenCallback(Callable.From(root.QueueFree));

        // ---- light: the brighter the result, the further it reaches (dull <= 2 m ... stein pillar fills the tunnel)
        float range = LootTuning.RevealLight[tier], peak = PeakEnergy(tier), secs = LootTuning.RevealSeconds[tier];
        var light = new OmniLight3D { LightColor = tier == 0 ? new Color(0.75f, 0.75f, 0.8f) : col.Lerp(Colors.White, 0.25f), OmniRange = range, LightEnergy = peak, Position = new Vector3(0, 0.5f, 0), ShadowEnabled = false };
        root.AddChild(light);
        var lt = root.CreateTween();
        if (tier >= 2)
        {
            lt.TweenProperty(light, "light_energy", peak * 0.55f, 0.9f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            lt.TweenProperty(light, "light_energy", tier == 3 ? peak * 0.4f : 1.3f, secs * 0.45f);
            lt.TweenProperty(light, "light_energy", 0f, secs * 0.55f);
        }
        else lt.TweenProperty(light, "light_energy", 0f, secs).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        if (tier == 2)   // a gem leaves a lingering glow behind: the range shrinks slowly
        {
            var rt = root.CreateTween(); rt.TweenProperty(light, "omni_range", range * 0.45f, secs).SetDelay(0.9f);
        }

        // ---- sparks and puffs
        if (!Headless)
        {
            if (tier == 0) LootVis.Sparks(root, new Vector3(0, 0.3f, 0), new Color(0.6f, 0.6f, 0.62f), 14, 1.8f, 0.7f, 0.12f, 5f);
            else if (tier == 1) { LootVis.Sparks(root, new Vector3(0, 0.3f, 0), col, 45, 5f, 0.9f, 0.1f, 8f); Ring(root, col, 4.5f, 0.55f, 0.7f); }
            else if (tier == 2)
            {
                LootVis.Sparks(root, new Vector3(0, 0.3f, 0), col, 90, 7f, 1.3f, 0.11f, 7f);
                LootVis.Sparks(root, new Vector3(0, 0.3f, 0), Colors.White, 40, 9f, 0.8f, 0.07f, 9f);
                Ring(root, col, 7f, 0.7f, 0.9f); Ring(root, Colors.White, 4f, 0.35f, 0.6f, 0.12f);
            }
            else Pillar(root, pos, col);
        }
        float haloSize = tier switch { 0 => 1.5f, 1 => 4.5f, 2 => 8f, _ => 13f };
        float haloA = tier switch { 0 => 0.25f, 1 => 0.7f, 2 => 0.95f, _ => 1f };
        var halo = LootVis.Halo(col.Lerp(Colors.White, tier >= 2 ? 0.35f : 0.1f), haloSize, haloA, new Vector3(0, 0.5f, 0));
        root.AddChild(halo);
        var ht = root.CreateTween();
        ht.TweenMethod(Callable.From<float>(a => { if (GodotObject.IsInstanceValid(halo)) LootVis.SetAlpha(halo, a); }), haloA, 0f, tier == 2 ? 2.2f : tier == 3 ? 3.2f : 0.7f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        // ---- stones fly out and land (the pickups exist already at their landing spots)
        for (int i = 0; i < ids.Length && i < land.Length; i++) FlyAndLabel(root, ids[i], pos + new Vector3(0, 0.35f, 0), land[i], items[i], i * 0.07f, 0.45f, 0.9f, tier);

        // ---- screen feedback for the local player, by distance and rarity
        float dist = CamDistance(pos);
        float rng = LootTuning.ScreenRange[tier];
        if (tier > 0 && dist < rng)
        {
            float k = Mathf.Clamp(1f - dist / rng, 0f, 1f);
            float flash = (tier switch { 1 => 0.22f, 2 => 0.55f, _ => 0.95f }) * (0.3f + 0.7f * k);
            Loot2Ui.I?.Flash(col.Lerp(Colors.White, 0.4f), flash, tier switch { 1 => 0.35f, 2 => 0.7f, _ => 1.3f });
            Shake(pos, rng, tier switch { 1 => 0.012f, 2 => 0.04f, _ => 0.085f }, tier switch { 1 => 0.3f, 2 => 0.55f, _ => 1.3f });
            Util.Pass("loot_screen_fx", $"tier={tier} dist={dist:0.#}");
        }
    }

    static void Ring(Node3D root, Color c, float maxSize, float alpha, float secs, float startSize = 0.3f)
    {
        var mat = LootVis.Additive(new Color(c.R, c.G, c.B, alpha), true, LootVis.Radial);
        var mi = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(1, 1) }, MaterialOverride = mat, Position = new Vector3(0, 0.06f, 0), Rotation = new Vector3(-Mathf.Pi / 2f, 0, 0), Scale = Vector3.One * startSize, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        root.AddChild(mi);
        var t = root.CreateTween();
        t.TweenProperty(mi, "scale", Vector3.One * maxSize, secs).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        t.Parallel().TweenMethod(Callable.From<float>(a => { if (GodotObject.IsInstanceValid(mi)) mat.AlbedoColor = new Color(c.R, c.G, c.B, a); }), alpha, 0f, secs);
    }

    /// <summary>The power stein reveal: a pillar of light from floor to ceiling that fills the tunnel, ~4 s, with lights along it and rising sparks.</summary>
    static void Pillar(Node3D root, Vector3 pos, Color col)
    {
        float h = 6.5f;
        var cam = Game.I.World.GetWorld3D().DirectSpaceState;
        var hit = cam.IntersectRay(PhysicsRayQueryParameters3D.Create(pos + Vector3.Up * 0.3f, pos + Vector3.Up * 16f, 1));
        if (hit.Count > 0) h = Mathf.Max(2.5f, hit["position"].AsVector3().Y - pos.Y);
        var outer = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.0f, BottomRadius = 1.25f, Height = h, RadialSegments = 14, Rings = 1 }, Position = new Vector3(0, h / 2f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var inner = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.35f, BottomRadius = 0.45f, Height = h, RadialSegments = 10, Rings = 1 }, Position = new Vector3(0, h / 2f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        var mo = LootVis.Additive(new Color(col.R, col.G, col.B, 0f), true); var mi = LootVis.Additive(new Color(1f, 1f, 1f, 0f), true);
        outer.MaterialOverride = mo; inner.MaterialOverride = mi;
        root.AddChild(outer); root.AddChild(inner);
        var t = root.CreateTween();
        t.TweenMethod(Callable.From<float>(u =>
        {
            float a = u < 0.06f ? u / 0.06f : u < 0.7f ? 1f : Mathf.Clamp((1f - u) / 0.3f, 0f, 1f);
            float flick = 0.88f + 0.12f * Mathf.Sin(u * 90f);
            mo.AlbedoColor = new Color(col.R, col.G, col.B, 0.42f * a * flick);
            mi.AlbedoColor = new Color(1f, 1f, 1f, 0.8f * a * flick);
            float s = 1f + 0.12f * Mathf.Sin(u * 40f);
            if (GodotObject.IsInstanceValid(outer)) outer.Scale = new Vector3(s, 1f, s);
        }), 0f, 1f, LootTuning.RevealSeconds[3]);
        foreach (float f in new[] { 0.12f, 0.5f, 0.92f })
        {
            var l = new OmniLight3D { LightColor = col.Lerp(Colors.White, 0.3f), OmniRange = 16f, LightEnergy = 9f, Position = new Vector3(0, h * f, 0), ShadowEnabled = false };
            root.AddChild(l);
            var lt = root.CreateTween();
            lt.TweenProperty(l, "light_energy", 5f, 1.0f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            lt.TweenProperty(l, "light_energy", 0f, LootTuning.RevealSeconds[3] - 1.0f);
        }
        LootVis.Sparks(root, new Vector3(0, 0.3f, 0), col.Lerp(Colors.White, 0.3f), 150, h * 1.6f, 1.6f, 0.1f, -1f, 12f);
        LootVis.Sparks(root, new Vector3(0, 0.3f, 0), col, 110, 7f, 1.2f, 0.09f, 6f, 180f);
        Ring(root, col, 9f, 0.8f, 1.1f); Ring(root, Colors.White, 6f, 0.5f, 0.7f, 0.2f);
    }

    static void FlyAndLabel(Node3D root, int pickupId, Vector3 from, Vector3 target, string item, float delay, float dur, float arc, int tier)
    {
        var w = Game.I.World;
        int it = LootTuning.TierOfItem(item);
        if (w.Objects.TryGetValue(pickupId, out var o) && GodotObject.IsInstanceValid(o))
        {
            o.GlobalPosition = from;
            var t = root.CreateTween();
            t.TweenMethod(Callable.From<float>(u =>
            {
                if (!GodotObject.IsInstanceValid(o)) return;
                o.GlobalPosition = from.Lerp(target, u) + new Vector3(0, 4f * u * (1f - u) * arc, 0);
            }), 0f, 1f, dur).SetDelay(delay).SetTrans(Tween.TransitionType.Sine);
        }
        if (Headless) return;
        var col = LootTuning.ColorOfItem(item);
        var lab = new Label3D
        {
            Text = LootTuning.LabelOf(item), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.0042f * (1f + 0.3f * it), FontSize = 64, OutlineSize = 14,
            OutlineModulate = new Color(0, 0, 0, 0.9f), Modulate = new Color(col.R, col.G, col.B, 0f), Font = UiTheme.Chunky, NoDepthTest = false,
            Position = root.ToLocal(target + new Vector3(0, 0.65f, 0)), TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
        };
        root.AddChild(lab);
        var lt = root.CreateTween();
        lt.TweenInterval(delay + dur * 0.9f);
        lt.TweenProperty(lab, "modulate:a", 1f, 0.12f);
        lt.Parallel().TweenProperty(lab, "position:y", lab.Position.Y + 0.55f, 2.3f).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        lt.TweenInterval(1.5f);
        lt.TweenProperty(lab, "modulate:a", 0f, 0.7f);
    }

    // ------------------------------------------------------------------ chests
    public static void ChestBurst(GlowChestObj c)
    {
        var w = Game.I.World;
        var top = c.GlobalPosition + new Vector3(0, 0.6f * c.VisScale, 0);
        var root = new Node3D { Name = "ChestBurst" };
        w.AddChild(root); root.GlobalPosition = top;
        int tier = c.Tier;
        var col = tier == 2 ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.78f, 0.38f);
        var light = new OmniLight3D { LightColor = col, OmniRange = 9f + tier * 3f, LightEnergy = 7f + tier * 3f, ShadowEnabled = false, Position = new Vector3(0, 0.3f, 0) };
        root.AddChild(light);
        var t = root.CreateTween();
        t.TweenProperty(light, "light_energy", 0f, 1.4f).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        t.TweenCallback(Callable.From(root.QueueFree));
        if (!Headless)
        {
            var shaft = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.3f, Height = 3.4f + tier, RadialSegments = 10, Rings = 1 }, Position = new Vector3(0, 1.7f + tier * 0.5f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            var sm = LootVis.Additive(new Color(col.R, col.G, col.B, 0.55f), true); shaft.MaterialOverride = sm;
            root.AddChild(shaft);
            var st = root.CreateTween();
            st.TweenMethod(Callable.From<float>(a => { if (GodotObject.IsInstanceValid(shaft)) sm.AlbedoColor = new Color(col.R, col.G, col.B, a); }), 0.55f, 0f, 1.0f);
            LootVis.Sparks(root, new Vector3(0, 0.1f, 0), col, 40 + tier * 30, 4.5f, 1.1f, 0.09f, 6f, 55f);
            LootVis.Sparks(root, new Vector3(0, 0.1f, 0), Colors.White, 20, 6f, 0.7f, 0.06f, 8f, 40f);
            Ring(root, col, 4f + tier, 0.6f, 0.6f);
        }
        float dist = CamDistance(top);
        if (dist < 9f) Loot2Ui.I?.Flash(col, 0.18f + 0.06f * tier, 0.4f);
    }

    public static void ChestPop(int chestId, int index, int total, int pickupId, Vector3 land, string item)
    {
        var w = Game.I.World;
        if (!w.Objects.TryGetValue(chestId, out var co) || co is not GlowChestObj c) return;
        var from = c.GlobalPosition + new Vector3(0, 0.8f * c.VisScale, 0);
        string[] pings = { SfxNames.ChestPing1, SfxNames.ChestPing2, SfxNames.ChestPing3, SfxNames.ChestPing4, SfxNames.ChestPing5, SfxNames.ChestPing6 };
        Sfx.Play(pings[Math.Clamp(index, 0, 5)], from);
        var root = new Node3D { Name = "ChestPop" };
        w.AddChild(root); root.GlobalPosition = from;
        var col = LootTuning.ColorOfItem(item);
        int it = LootTuning.TierOfItem(item);
        var light = new OmniLight3D { LightColor = col.Lerp(Colors.White, 0.3f), OmniRange = 3.5f + it * 1.5f, LightEnergy = 2.5f + it, ShadowEnabled = false };
        root.AddChild(light);
        var t = root.CreateTween();
        t.TweenProperty(light, "light_energy", 0f, 0.5f);
        t.TweenInterval(3.5f); t.TweenCallback(Callable.From(root.QueueFree));
        if (!Headless) LootVis.Sparks(root, Vector3.Zero, col, 12 + it * 6, 2.6f, 0.5f, 0.07f, 6f, 60f);
        FlyAndLabel(root, pickupId, from, land, item, 0f, 0.5f, 1.3f, it);
        Util.Pass("bchest_pop_seen", $"index={index} total={total}");
    }

    // ------------------------------------------------------------------ duds
    public static void Dud(Vector3 pos, int kind)
    {
        var w = Game.I.World;
        var root = new Node3D { Name = "Dud" };
        w.AddChild(root); root.GlobalPosition = pos;
        var t = root.CreateTween(); t.TweenInterval(4.5f); t.TweenCallback(Callable.From(root.QueueFree));
        var light = new OmniLight3D { LightColor = kind == 1 ? new Color(0.5f, 1f, 0.3f) : new Color(0.7f, 0.7f, 0.8f), OmniRange = 4f, LightEnergy = 1.8f, Position = new Vector3(0, 0.4f, 0), ShadowEnabled = false };
        root.AddChild(light);
        var lt = root.CreateTween(); lt.TweenProperty(light, "light_energy", 0f, 0.9f);
        if (!Headless) LootVis.Sparks(root, new Vector3(0, 0.3f, 0), new Color(0.5f, 0.5f, 0.52f), 14, 1.8f, 0.7f, 0.12f, 5f);
        if (kind == 2)
        {
            Sfx.Play(SfxNames.Bats, pos + Vector3.Up);
            if (!Headless) root.AddChild(new BatSwarm());
            Loot2Ui.I?.Flash(new Color(0f, 0f, 0f), 0.3f, 0.5f);
            Shake(pos, 12f, 0.03f, 0.5f);
        }
        else Sfx.Play(SfxNames.GasPuff, pos + Vector3.Up * 0.5f);
        var lab = new Label3D { Text = kind == 1 ? "DUD: GAS" : "DUD: BATS", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, PixelSize = 0.0042f, FontSize = 64, OutlineSize = 14, Modulate = new Color(0.8f, 0.8f, 0.8f, 1f), Font = UiTheme.Chunky, Position = new Vector3(0, 1.0f, 0) };
        if (!Headless) { root.AddChild(lab); var tt = root.CreateTween(); tt.TweenProperty(lab, "modulate:a", 0f, 2.0f).SetDelay(0.6f); }
    }
}

/// <summary>A harmless swarm of bats that circles up and away for ~3 s. Purely visual (the noise is made by the server).</summary>
public partial class BatSwarm : Node3D
{
    readonly List<(Node3D n, float ph, float rad, float spd, float climb)> _bats = new();
    double _t;

    public override void _Ready()
    {
        var rng = new RandomNumberGenerator { Seed = 99 };
        for (int i = 0; i < 16; i++)
        {
            var n = new Node3D();
            var body = Util.BoxMesh(new Vector3(0.07f, 0.05f, 0.1f), new Color(0.04f, 0.03f, 0.04f)); n.AddChild(body);
            foreach (float s in new[] { -1f, 1f })
            {
                var wing = Util.BoxMesh(new Vector3(0.2f, 0.01f, 0.1f), new Color(0.05f, 0.04f, 0.05f)); wing.Position = new Vector3(s * 0.12f, 0, 0); wing.Name = s < 0 ? "WL" : "WR"; n.AddChild(wing);
            }
            AddChild(n);
            _bats.Add((n, rng.Randf() * 6.28f, rng.RandfRange(0.4f, 1.8f), rng.RandfRange(2.2f, 4.5f), rng.RandfRange(0.35f, 1.1f)));
        }
    }

    public override void _Process(double delta)
    {
        _t += delta;
        float t = (float)_t;
        foreach (var (n, ph, rad, spd, climb) in _bats)
        {
            float a = ph + t * spd;
            n.Position = new Vector3(Mathf.Cos(a) * rad * (0.6f + t * 0.5f), 0.4f + t * climb + Mathf.Sin(a * 2f) * 0.2f, Mathf.Sin(a) * rad * (0.6f + t * 0.5f));
            n.Rotation = new Vector3(0, -a, 0);
            float flap = Mathf.Sin(t * 30f + ph) * 0.8f;
            n.GetNode<Node3D>("WL").Rotation = new Vector3(0, 0, flap); n.GetNode<Node3D>("WR").Rotation = new Vector3(0, 0, -flap);
        }
        if (_t > 3.6) QueueFree();
    }
}
