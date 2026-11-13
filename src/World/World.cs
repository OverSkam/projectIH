using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Holds the cave, miners and networked objects for one round. Created per round on every peer by Game.</summary>
public partial class World : Node3D
{
    public CaveInfo Cave;
    /// <summary>The cave's environment (fog density / saturation are driven locally by LocalAtmosphere).</summary>
    public Godot.Environment Env;
    public Node3D MinersRoot, ObjectsRoot;
    public readonly Dictionary<int, NetObject> Objects = new();
    public int NextObjectId = 1; // server only

    public static ICaveBuilder Builder = Util.Arg("cave", "modular") == "greybox" ? new GreyBoxCave() : new ModularCave();

    public void BuildCave(int seed, int round, int miners = 2)
    {
        var geo = new Node3D { Name = "Geometry" };
        AddChild(geo);
        Cave = Builder.Build(geo, seed, round, miners);
        MinersRoot = new Node3D { Name = "Miners" };
        ObjectsRoot = new Node3D { Name = "Objects" };
        AddChild(MinersRoot); AddChild(ObjectsRoot);
        AddChild(new HallCeremony { Name = "HallCeremony" });

        // No ambient light, black background, depth + height fog (see Atmosphere). Dust and the blindness rig attach to the local camera.
        Env = Atmosphere.MakeEnvironment();
        var env = Env;
        AddChild(new WorldEnvironment { Environment = env });
        if (DisplayServer.GetName() != "headless" && Builder is ModularCave) Atmosphere.BuildFogCards(this, Cave);   // nothing to draw (and nothing to slow tests down) without a display
        if (Util.HasArg("camroom") || Util.HasArg("cam") || Util.HasArg("camtunnel") || Util.HasArg("camgoblin")) AddChild(DebugCam.Make(Cave));
    }

    public Miner GetMiner(int peer) => MinersRoot.GetNodeOrNull<Miner>(peer.ToString());

    public IEnumerable<Miner> Miners()
    {
        foreach (var c in MinersRoot.GetChildren()) if (c is Miner m) yield return m;
    }

    public NetObject SpawnObject(int id, string kind, Vector3 pos, string data)
    {
        var o = NetObject.Create(kind);
        if (o == null) { GD.PushError("Unknown object kind " + kind); return null; }
        o.Id = id; o.Data = data; o.Name = kind + "_" + id;
        ObjectsRoot.AddChild(o);
        o.Position = pos;
        o.Build();
        o.ApplyData(data);
        Objects[id] = o;
        return o;
    }

    public void RemoveObject(int id)
    {
        if (Objects.Remove(id, out var o)) o.QueueFree();
    }
}
