using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Small shared helpers: logging, command-line args, mesh/material helpers.</summary>
public static class Util
{
    static Dictionary<string, string> _args;

    /// <summary>Parses user args after "--": "--host", "--join=1.2.3.4", "--name=X" ...</summary>
    public static Dictionary<string, string> Args
    {
        get
        {
            if (_args != null) return _args;
            _args = new Dictionary<string, string>();
            foreach (var a in OS.GetCmdlineUserArgs())
            {
                var s = a.TrimStart('-');
                int eq = s.IndexOf('=');
                if (eq < 0) _args[s] = "";
                else _args[s.Substring(0, eq)] = s.Substring(eq + 1);
            }
            return _args;
        }
    }

    public static bool HasArg(string k) => Args.ContainsKey(k);
    public static string Arg(string k, string def = "") => Args.TryGetValue(k, out var v) ? v : def;
    public static bool Automated => HasArg("host") || HasArg("join") || HasArg("bot");

    public static void Log(string msg) => GD.Print(msg);
    public static void Pass(string name, string detail = "") => GD.Print("PASS: " + name + (detail == "" ? "" : " " + detail));
    public static void Fail(string name, string detail = "") => GD.Print("FAIL: " + name + (detail == "" ? "" : " " + detail));

    static readonly Dictionary<string, StandardMaterial3D> _mats = new();
    public static StandardMaterial3D Mat(Color c, float emission = 0f)
    {
        string key = c.ToHtml() + emission;
        if (_mats.TryGetValue(key, out var m)) return m;
        m = new StandardMaterial3D { AlbedoColor = c, Roughness = 1f };
        if (emission > 0f) { m.EmissionEnabled = true; m.Emission = c; m.EmissionEnergyMultiplier = emission; }
        _mats[key] = m;
        return m;
    }

    public static MeshInstance3D BoxMesh(Vector3 size, Color c, float emission = 0f)
    {
        return new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Mat(c, emission) };
    }

    public static MeshInstance3D SphereMesh(float r, Color c, float emission = 0f)
    {
        return new MeshInstance3D { Mesh = new SphereMesh { Radius = r, Height = r * 2f, RadialSegments = 8, Rings = 4 }, MaterialOverride = Mat(c, emission) };
    }

    public static MeshInstance3D CapsuleMesh(float r, float h, Color c)
    {
        return new MeshInstance3D { Mesh = new CapsuleMesh { Radius = r, Height = h, RadialSegments = 8, Rings = 2 }, MaterialOverride = Mat(c) };
    }

    public static CollisionShape3D BoxShape(Vector3 size, Vector3 pos)
    {
        return new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = pos };
    }

    public static int[] ParseInts(string s)
    {
        if (string.IsNullOrEmpty(s)) return Array.Empty<int>();
        var p = s.Split(',');
        var r = new int[p.Length];
        for (int i = 0; i < p.Length; i++) int.TryParse(p[i], out r[i]);
        return r;
    }
}
