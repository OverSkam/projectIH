using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>
/// Base class of every server-id'd world object (ore vein, loose ore, loot bag, goblin; later: monsters, traps, levers...).
/// The node exists on all peers; the server alone mutates Data and broadcasts it (Game.ServerSetObjectData).
/// Register new kinds in NetObject.Factories.
/// </summary>
public abstract partial class NetObject : StaticBody3D
{
    public int Id;
    public string Kind = "";
    public string Data = "";

    public static readonly Dictionary<string, Func<NetObject>> Factories = new()
    {
        ["vein"] = () => new VeinObj(),
        ["pickup"] = () => new PickupObj(),
        ["bag"] = () => new BagObj(),
        ["goblin"] = () => new GoblinObj(),
    };

    public static NetObject Create(string kind)
    {
        if (!Factories.TryGetValue(kind, out var f)) return null;
        var o = f();
        o.Kind = kind;
        return o;
    }

    /// <summary>Build visuals + collision. Called on every peer after Id/Kind/Data/Position are set.</summary>
    public abstract void Build();
    /// <summary>Called on every peer when the server updates Data.</summary>
    public virtual void ApplyData(string data) { Data = data; }
    /// <summary>Client-side look-at prompt text (null = not interactable with E).</summary>
    public virtual string Prompt(Inventory inv) => null;
    /// <summary>Server: a validated E-press by player p.</summary>
    public virtual void ServerInteract(ServerPlayer p) { }
    /// <summary>Server: p swung a tool at this object. mining = pickaxe in hand * mining speed multiplier.</summary>
    public virtual void ServerHit(ServerPlayer p, float mining, bool pickaxe) { }
    /// <summary>Max interaction distance for the server-side check.</summary>
    public virtual float InteractRange => 3.2f;

    public static Dictionary<string, string> Kv(string data)
    {
        var d = new Dictionary<string, string>();
        foreach (var part in data.Split(';'))
        {
            int i = part.IndexOf('=');
            if (i > 0) d[part.Substring(0, i)] = part.Substring(i + 1);
        }
        return d;
    }
}

// ----------------------------------------------------------------------------- Ore vein
public partial class VeinObj : NetObject
{
    MeshInstance3D _rock;
    string _ore = "coal"; float _hp = 3, _max = 3;
    public string OreId => _ore;

    static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    public static string MakeData(string ore, float hp, float max) => $"ore={ore};hp={F(hp)};max={F(max)}";

    void Parse()
    {
        var kv = Kv(Data);
        if (kv.TryGetValue("ore", out var o)) _ore = o;
        if (kv.TryGetValue("hp", out var h)) float.TryParse(h, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _hp);
        if (kv.TryGetValue("max", out var m)) float.TryParse(m, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _max);
    }

    public override void Build()
    {
        Parse();
        CollisionLayer = 1 | 4;
        CollisionMask = 0;
        var def = Ores.Get(_ore);
        _rock = Util.BoxMesh(new Vector3(1.4f, 1.4f, 1.4f), new Color(0.25f, 0.24f, 0.25f));
        _rock.Position = new Vector3(0, 0.7f, 0);
        AddChild(_rock);
        var rng = new RandomNumberGenerator { Seed = (ulong)(Id * 7919 + 13) };
        for (int i = 0; i < 4; i++)
        {
            var c = Util.BoxMesh(new Vector3(0.38f, 0.38f, 0.38f), def.Color, Math.Max(0.8f, def.Glow * 0.8f));   // veins glow faintly: findable in the dark
            c.Position = new Vector3(rng.RandfRange(-0.5f, 0.5f), rng.RandfRange(0.3f, 1.2f), rng.RandfRange(-0.5f, 0.5f)).Normalized() * 0.75f + new Vector3(0, 0.7f, 0);
            c.Rotation = new Vector3(rng.Randf(), rng.Randf(), rng.Randf());
            _rock.AddChild(c);
            c.Position -= _rock.Position;
        }
        AddChild(Util.BoxShape(new Vector3(1.5f, 1.5f, 1.5f), new Vector3(0, 0.75f, 0)));
        AddChild(new OmniLight3D { Position = new Vector3(0, 0.9f, 0), LightColor = def.Color.Lerp(Colors.White, 0.25f), LightEnergy = 0.22f, OmniRange = 3.2f, ShadowEnabled = false });
    }

    public override void ApplyData(string data)
    {
        base.ApplyData(data);
        Parse();
        if (_rock != null) _rock.Scale = Vector3.One * (0.6f + 0.4f * Mathf.Clamp(_hp / _max, 0f, 1f));
    }

    public override string Prompt(Inventory inv) => Utilities.Get(inv.Held)?.IsPickaxe == true ? "LMB  Mine " + Ores.Get(_ore)?.Name : "Hold the pickaxe (1/2) and mine " + Ores.Get(_ore)?.Name;

    public override void ServerHit(ServerPlayer p, float mining, bool pickaxe)
    {
        if (!pickaxe) return;
        Parse();
        Game.ServerNoise(GlobalPosition, 12f, p);
        _hp -= mining;
        if (_hp > 0.001f)
        {
            Game.I.ServerSetObjectData(Id, MakeData(_ore, _hp, _max));
            return;
        }
        Game.I.ServerDespawnObject(Id);
        Loot2Module.OnVeinBroken(GlobalPosition, _ore, p);   // a vein no longer drops stones: it breaks off 1-3 sealed geodes (src/Loot2)
        p.Fire(new SteinEvent { Kind = SteinEventKind.Mined, Amount = Ores.WeightOf(_ore) });
        Util.Pass("ore_mined", $"ore={_ore} by={p.Name}");
    }
}

// ----------------------------------------------------------------------------- Loose item on the floor
/// <summary>Data = "ore:gold" | "stein:regen" | "util:rope".</summary>
public partial class PickupObj : NetObject
{
    public override float InteractRange => 3.0f;

    public static string NameOf(string item)
    {
        int c = item.IndexOf(':'); if (c < 0) return item;
        string id = item.Substring(c + 1);
        return item.StartsWith("ore:") ? Ores.Get(id)?.Name ?? id : item.StartsWith("stein:") ? Steins.Get(id)?.Name ?? id : Utilities.Get(id)?.Name ?? id;
    }

    public override void Build()
    {
        CollisionLayer = 8; CollisionMask = 0;
        int c = Data.IndexOf(':'); string kind = Data.Substring(0, c), id = Data.Substring(c + 1);
        MeshInstance3D m;
        if (kind == "ore") { var d = Ores.Get(id); m = Util.BoxMesh(new Vector3(0.3f, 0.3f, 0.3f), d.Color, Math.Max(0.5f, d.Glow)); m.Rotation = new Vector3(0.6f, 0.7f, 0.2f); }
        else if (kind == "stein") { m = Util.SphereMesh(0.01f, Steins.Get(id)?.Color ?? Colors.White); AddChild(ItemVisuals.Stein(id)); }
        else m = Util.BoxMesh(new Vector3(0.3f, 0.2f, 0.5f), Utilities.Get(id)?.Color ?? Colors.Gray);
        AddChild(m);
        AddChild(Util.BoxShape(new Vector3(0.7f, 0.7f, 0.7f), Vector3.Zero));
        if (kind != "util") AddChild(new OmniLight3D { LightColor = m.MaterialOverride is StandardMaterial3D sm ? sm.AlbedoColor : Colors.White, OmniRange = 2.5f, LightEnergy = 0.6f });
    }

    public override string Prompt(Inventory inv)
    {
        if (!Data.StartsWith("ore:")) return "E  Pick up " + ItemInfo.Full(Data);
        var d = Ores.Get(Data.Substring(4));
        // the sack is bottomless and nothing ever leaves it: taking a stone is a choice, so the prompt shows what it is worth and what it costs
        return d == null ? "E  Pick up " + NameOf(Data) : $"E  Take {d.Name}   +{d.TributeWeight} tribute  /  +{d.Burden} burden";
    }

    public override void ServerInteract(ServerPlayer p)
    {
        if (!p.TryAdd(Data) && !ItemsServer.TrySwap(p, Data)) return;
        if (!Data.StartsWith("ore:")) Game.I.ServerNotice(p, "You take: " + ItemInfo.Line(Data));
        Game.I.ServerDespawnObject(Id);
    }
}

// ----------------------------------------------------------------------------- Loot bag
/// <summary>Server holds Items; clients never learn the contents until they loot it.</summary>
public partial class BagObj : NetObject
{
    public List<string> Items = new(); // server only

    public override void Build()
    {
        CollisionLayer = 8; CollisionMask = 0;
        var sack = Util.BoxMesh(new Vector3(0.6f, 0.5f, 0.5f), new Color(0.45f, 0.3f, 0.15f));
        sack.Position = new Vector3(0, 0.25f, 0);
        AddChild(sack);
        var tie = Util.BoxMesh(new Vector3(0.2f, 0.15f, 0.2f), new Color(0.3f, 0.2f, 0.1f));
        tie.Position = new Vector3(0, 0.55f, 0);
        AddChild(tie);
        AddChild(Util.BoxShape(new Vector3(0.9f, 0.9f, 0.9f), new Vector3(0, 0.4f, 0)));
    }

    public override string Prompt(Inventory inv) => "E  Pour this bag into your sack (all of it)";

    /// <summary>All or nothing: every stone in the bag is poured into the interacting miner's sack (with its burden). Steins and utilities go to free slots, the rest stays.</summary>
    public override void ServerInteract(ServerPlayer p)
    {
        int stones = 0, w = 0, bu = 0;
        foreach (var it in Items.ToArray())
        {
            if (it.StartsWith("ore:")) { string id = it.Substring(4); p.Inv.Sack.Add(id); w += Ores.WeightOf(id); bu += Ores.BurdenOf(id); stones++; Items.Remove(it); p.Dirty = true; }
            else if (p.TryAdd(it)) { Items.Remove(it); Game.I.ServerNotice(p, "You take: " + ItemInfo.Line(it)); }
        }
        if (stones > 0) Game.I.ServerNotice(p, $"You pour {stones} stone{(stones == 1 ? "" : "s")} into your sack: +{w} tribute, +{bu} burden.");
        Util.Pass("bag_poured", $"by={p.Name} stones={stones} tribute={w} left={Items.Count}");
        if (Items.Count == 0) Game.I.ServerDespawnObject(Id);
    }
}

// ----------------------------------------------------------------------------- Goblin (model: src/Goblin)
public partial class GoblinObj : NetObject
{
    public override float InteractRange => 3.6f;

    /// <summary>Visuals, collision and animation live in src/Goblin/GoblinRig.cs.</summary>
    public override void Build() => GoblinRig.Attach(this);

    /// <summary>No feeding any more: the Goblin is only scenery (and a collision body) during the round. The prompt is deliberately empty.</summary>
    public override string Prompt(Inventory inv) => null;
}
