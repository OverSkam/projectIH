using System;
using Godot;

namespace AppStein;

/// <summary>Oil drum: refills the interacting miner's lantern completely. A few refills each (Config.OilDrumRefills). Spawned from cave markers of kind "oil".</summary>
public partial class OilDrumObj : NetObject
{
    int _uses = 3;
    MeshInstance3D _band;

    public override float InteractRange => 2.8f;

    public static string MakeData(int uses) => $"uses={uses}";

    void Parse()
    {
        var kv = Kv(Data);
        if (kv.TryGetValue("uses", out var u)) int.TryParse(u, out _uses);
    }

    public override void Build()
    {
        Parse();
        CollisionLayer = 1 | 4; CollisionMask = 0;
        var body = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.36f, BottomRadius = 0.36f, Height = 0.9f, RadialSegments = 10, Rings = 1 }, MaterialOverride = Util.Mat(new Color(0.22f, 0.24f, 0.2f)) };
        body.Position = new Vector3(0, 0.45f, 0);
        AddChild(body);
        _band = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.375f, BottomRadius = 0.375f, Height = 0.12f, RadialSegments = 10, Rings = 1 }, MaterialOverride = Util.Mat(new Color(1f, 0.6f, 0.15f), 0.6f) };
        _band.Position = new Vector3(0, 0.55f, 0);
        AddChild(_band);
        var cap = Util.BoxMesh(new Vector3(0.18f, 0.06f, 0.18f), new Color(0.5f, 0.5f, 0.5f));
        cap.Position = new Vector3(0.1f, 0.93f, 0.05f);
        AddChild(cap);
        AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 0.4f, Height = 0.9f }, Position = new Vector3(0, 0.45f, 0) });
        AddChild(new OmniLight3D { Position = new Vector3(0, 1.2f, 0), LightColor = new Color(1f, 0.6f, 0.2f), LightEnergy = 0.5f, OmniRange = 3f });
    }

    public override void ApplyData(string data)
    {
        base.ApplyData(data);
        Parse();
        if (_band != null) _band.MaterialOverride = Util.Mat(_uses > 0 ? new Color(1f, 0.6f, 0.15f) : new Color(0.3f, 0.3f, 0.3f), _uses > 0 ? 0.6f : 0f);
    }

    public override string Prompt(Inventory inv) => _uses > 0 ? $"E  Refill lantern  ({_uses} left)" : "The drum is empty";

    public override void ServerInteract(ServerPlayer p)
    {
        Parse();
        var g = Game.I;
        if (_uses <= 0) { g.ServerNotice(p, "The drum is empty."); return; }
        if (!ItemsServer.HasUtil(p, "lantern")) { g.ServerNotice(p, "You have no lantern to fill."); return; }
        if (p.Oil >= g.Config.LanternOilSeconds - 1f) { g.ServerNotice(p, "Your lantern is already full."); return; }
        ItemsServer.Refuel(p);
        _uses--;
        g.ServerSetObjectData(Id, MakeData(_uses));
        g.ServerNotice(p, "You refill your lantern.");
        Util.Pass("oil_refilled", $"name={p.Name} left={_uses}");
    }
}
