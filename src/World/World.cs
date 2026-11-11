using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Holds the cave, miners and networked objects for one round. Created per round on every peer by Game.</summary>
public partial class World : Node3D
{
    public CaveInfo Cave;
    public Node3D MinersRoot, ObjectsRoot;
    public readonly Dictionary<int, NetObject> Objects = new();
    public int NextObjectId = 1; // server only

    public static ICaveBuilder Builder = new GreyBoxCave(); // swap for the procedural generator

    public void BuildCave(int seed, int round)
    {
        var geo = new Node3D { Name = "Geometry" };
        AddChild(geo);
        Cave = Builder.Build(geo, seed, round);
        MinersRoot = new Node3D { Name = "Miners" };
        ObjectsRoot = new Node3D { Name = "Objects" };
        AddChild(MinersRoot); AddChild(ObjectsRoot);

        // Pitch black: no ambient light, black background.
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Colors.Black,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.Black,
            AmbientLightEnergy = 0f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true,
            FogLightColor = new Color(0.02f, 0.02f, 0.03f),
            FogDensity = 0.015f,
        };
        AddChild(new WorldEnvironment { Environment = env });
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
