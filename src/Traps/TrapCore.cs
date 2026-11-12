using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>All trap numbers in one place (guesses; tune by playing).</summary>
public static class TrapTuning
{
    public const uint PitLayer = 64;                // physics layer of trapdoor pit geometry (miners add it to their mask)

    // spike plate
    public const float SpikeHalf = 0.85f, SpikeClickDelay = 0.6f, SpikeDamage = 30f, SpikeUpTime = 1.2f, SpikeRearm = 3f;
    // rockfall
    public const float RockRadius = 2.4f, RockRumble = 1.5f, RockFallTime = 0.45f, RockCrushDamage = 65f, CollapseDamage = 200f;
    public const float RockNoiseMin = 12f;           // loudness needed (metres) to trigger a rockfall...
    public const float RockNoiseRange = 18f;         // ...and max distance (also limited by the loudness itself)
    public const float RockRandomMin = 110f, RockRandomMax = 260f;
    public const int RockOreMin = 1, RockOreMax = 2;
    // gas
    public const float GasRadius = 2.8f, GasWarn = 1.6f, GasVent = 4.5f, GasTick = 0.5f, GasTickDamage = 4f, GasIdleMin = 8f, GasIdleMax = 14f;
    // darts
    public const float DartWireHeight = 0.22f, DartTriggerHeight = 0.4f, DartTelegraph = 0.45f, DartSpeed = 22f, DartDamage = 9f, DartReload = 5f, DartStagger = 0.12f;
    public const int DartCount = 3;
    // trapdoor
    public const float PitDepth = 4.6f, PitLength = 7f, PitWidth = 2.2f, TrapdoorStand = 0.5f, TrapdoorCreak = 0.9f, TrapdoorOpen = 7f, TrapdoorLeverOpen = 6f, TrapdoorRearm = 6f;
    // levers / gate
    public const float GateWarn = 1.0f, GateDown = 10f, GateRise = 0.8f, LeverCooldown = 20f, CollapseLeverCooldown = 30f;
    // placement
    public const float MinTrapSpacing = 6.5f, MinLeverSpacing = 3.2f, MinMechSpacing = 4.5f, CampMargin = 4f;
    public const int SafeDepth = 1;                  // rooms this close to the camp (depth <= 1) get no lethal traps
    static readonly int[] Budget = { 2, 3, 4, 6, 8 };
    public static int MaxTraps(int round) => Budget[Math.Clamp(round - 1, 0, Budget.Length - 1)];   // traps + levers per round
}

/// <summary>Server-side helpers shared by all traps.</summary>
public static class TrapUtil
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    public static string F(float v) => v.ToString("0.###", Inv);
    public static float ToF(string s, float def = 0f) => float.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : def;

    /// <summary>Alive round participants outside the Goblin camp (traps never hurt or trigger inside the camp).</summary>
    public static IEnumerable<ServerPlayer> Miners()
    {
        var g = Game.I;
        if (g?.World?.Cave == null) yield break;
        foreach (var p in g.Players.Values)
            if (p.Participant && p.Alive && !g.World.Cave.InCamp(p.Pos)) yield return p;
    }

    public static float Hurt(ServerPlayer v, float amount, ServerPlayer by, string cause, string kind)
    {
        float d = v.TakeDamage(amount, by, cause);
        Util.Log($"TRAPDMG kind={kind} victim={v.Name} cause={cause} dmg={d:0.#} hp={v.Health:0.#} source={by?.Name ?? "-"}");
        if (!v.Alive) Util.Log($"TRAPKILL kind={kind} victim={v.Name} cause={cause} killer={by?.Name ?? "-"}");
        return d;
    }

    public static Vector3 Dir(float a) => new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
    public static float DistToSegXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = new Vector2(b.X - a.X, b.Z - a.Z); var ap = new Vector2(p.X - a.X, p.Z - a.Z);
        float l2 = ab.LengthSquared();
        float t = l2 < 1e-6f ? 0 : Mathf.Clamp(ap.Dot(ab) / l2, 0, 1);
        return (ap - ab * t).Length();
    }
}

/// <summary>Raycast-based reading of the surroundings of a marker (works with any cave geometry; falls back when rays miss).</summary>
public struct Probe
{
    public float FloorY, CeilH;   // CeilH = ceiling height above the floor
    public float[] D;             // free distance (cap 12) in 16 directions at chest height, angle i*pi/8 (dir = (cos,0,sin))
    public float CorrAng;         // direction of the passage axis (pointing to the freer side)
    public float Width;           // passage width across that axis
    public float FreeFwd, FreeBack, SideP, SideN; // free along corridor +/-, and across (+Z local, -Z local) in metres
    public bool Valid;

    const float Cap = 12f;
    static PhysicsDirectSpaceState3D Space => Game.I.World.GetWorld3D().DirectSpaceState;

    public static bool Ray(Vector3 a, Vector3 b, out Vector3 hit, uint mask = 1)
    {
        var q = PhysicsRayQueryParameters3D.Create(a, b, mask);
        var r = Space.IntersectRay(q);
        hit = b;
        if (r.Count == 0) return false;
        hit = r["position"].AsVector3();
        return true;
    }

    public static Probe At(Vector3 p)
    {
        var pr = new Probe { D = new float[16], FloorY = p.Y, CeilH = 4.5f };
        if (Ray(p + Vector3.Up * 1.5f, p + Vector3.Down * 3f, out var fh)) pr.FloorY = fh.Y;
        if (Ray(new Vector3(p.X, pr.FloorY + 0.4f, p.Z), new Vector3(p.X, pr.FloorY + 14f, p.Z), out var ch)) pr.CeilH = ch.Y - pr.FloorY;
        var o = new Vector3(p.X, pr.FloorY + 1.0f, p.Z);
        for (int i = 0; i < 16; i++)
        {
            var d = TrapUtil.Dir(i * Mathf.Pi / 8f);
            pr.D[i] = Ray(o, o + d * Cap, out var h) ? o.DistanceTo(h) : Cap;
        }
        float best = 1e9f; int bi = 0;
        for (int i = 0; i < 8; i++) { float w = pr.D[i] + pr.D[i + 8]; if (w < best) { best = w; bi = i; } }
        // wire/gate axis = bi; corridor axis = bi + 4
        int c = (bi + 4) % 16, c2 = (c + 8) % 16;
        bool fwd = pr.D[c] >= pr.D[c2];
        int ci = fwd ? c : c2;
        pr.CorrAng = ci * Mathf.Pi / 8f;
        pr.FreeFwd = pr.D[ci]; pr.FreeBack = pr.D[(ci + 8) % 16];
        // across directions: local +Z of a frame whose +X is CorrAng is dir(CorrAng + 90deg)
        int zi = (ci + 4) % 16;
        pr.SideP = pr.D[zi]; pr.SideN = pr.D[(zi + 8) % 16];
        pr.Width = pr.SideP + pr.SideN;
        pr.Valid = pr.D[0] > 0.3f || pr.D[8] > 0.3f;
        return pr;
    }
}

/// <summary>Procedural one-shot sounds (no asset files).</summary>
public static class TrapSfx
{
    /// <summary>Plays a trap sound through the audio engine. `name` is an SfxNames constant.</summary>
    public static void Play(Vector3 pos, string name, float volDb = 0f) => Sfx.Play(name, pos, volDb);
}

/// <summary>Base of every trap: replicated state lives in Data ("s=state;key=val;..."), the server runs the logic, all peers animate.</summary>
public abstract partial class TrapBase : NetObject
{
    protected Dictionary<string, string> D = new();
    protected string State = "";
    protected Node3D Pivot;                       // rotated by the layout angle; local +X = "a" direction
    protected static bool Srv => Net.I != null && Net.I.IsServer;

    protected float GF(string k, float def = 0f) => D.TryGetValue(k, out var s) ? TrapUtil.ToF(s, def) : def;
    protected bool GB(string k) => D.TryGetValue(k, out var s) && s == "1";
    protected float Floor => GlobalPosition.Y;
    public string St => State;
    public bool Flag(string k) => GB(k);

    public static string Mk(params (string k, object v)[] kv)
    {
        return string.Join(";", kv.Select(e => e.k + "=" + (e.v is float f ? TrapUtil.F(f) : e.v is bool b ? (b ? "1" : "0") : e.v.ToString())));
    }

    /// <summary>Called by Build() of subclasses first.</summary>
    protected void Prelude(string angleKey = null)
    {
        CollisionLayer = 0; CollisionMask = 0;
        D = Kv(Data);
        Pivot = new Node3D { Rotation = new Vector3(0, angleKey == null ? 0f : -GF(angleKey), 0) };
        AddChild(Pivot);
    }

    public override void ApplyData(string data)
    {
        string old = State;
        base.ApplyData(data);
        D = Kv(data);
        State = D.TryGetValue("s", out var s) ? s : "";
        if (State != old)
        {
            Util.Log($"TRAPSTATE kind={Kind} id={Id} state={State} peer={Net.I.MyId}");
            OnState(old, State);
        }
        OnData();
    }

    protected virtual void OnState(string old, string now) { }
    protected virtual void OnData() { }

    /// <summary>Server: change state (and optional extra keys) and replicate.</summary>
    protected void SetState(string s, params (string k, object v)[] extra)
    {
        D["s"] = s;
        foreach (var (k, v) in extra) D[k] = v is float f ? TrapUtil.F(f) : v.ToString();
        Game.I.ServerSetObjectData(Id, string.Join(";", D.Select(e => e.Key + "=" + e.Value)));
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Srv || Game.I == null || Game.I.Phase != GamePhase.Round || Game.I.World == null) return;
        ServerTick((float)delta);
    }
    protected virtual void ServerTick(float dt) { }
    /// <summary>Server debug (--trapforce): trigger without a victim.</summary>
    public virtual void DebugForce() { }

    // ---- visual helpers
    protected static MeshInstance3D Box(Node parent, Vector3 size, Vector3 pos, Color c, float em = 0f, Vector3? rot = null)
    {
        var m = Util.BoxMesh(size, c, em);
        m.Position = pos;
        if (rot.HasValue) m.Rotation = rot.Value;
        parent.AddChild(m);
        return m;
    }

    protected static MeshInstance3D Beam(Node parent, Vector3 a, Vector3 b, float th, Color c, float em = 0f)
    {
        var d = b - a; float len = d.Length();
        var m = Util.BoxMesh(new Vector3(th, th, Mathf.Max(len, 0.01f)), c, em);
        parent.AddChild(m);
        var n = len < 1e-4f ? Vector3.Forward : d / len;
        var up = Mathf.Abs(n.Y) > 0.99f ? Vector3.Right : Vector3.Up;
        m.Transform = new Transform3D(Basis.LookingAt(n, up), (a + b) * 0.5f);
        return m;
    }

    protected static MeshInstance3D Cone(Node parent, float r, float h, Vector3 pos, Color c, bool inverted = false)
    {
        var m = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = inverted ? r : 0f, BottomRadius = inverted ? 0f : r, Height = h, RadialSegments = 6, Rings = 1 }, MaterialOverride = Util.Mat(c) };
        m.Position = pos;
        parent.AddChild(m);
        return m;
    }

    protected CpuParticles3D Particles(Node parent, Vector3 pos, Color c, float size, int amount, float life, float speed, Vector3 dir, float spread, Vector3 grav, Vector3 extents, bool alpha = false, bool boxMesh = false)
    {
        Mesh mesh = boxMesh ? new BoxMesh { Size = new Vector3(size, size, size) } : new SphereMesh { Radius = size, Height = size * 2f, RadialSegments = 5, Rings = 3 };
        var mat = new StandardMaterial3D { AlbedoColor = c, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        if (alpha) mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        mesh.SurfaceSetMaterial(0, mat);
        var p = new CpuParticles3D
        {
            Mesh = mesh, Amount = amount, Lifetime = life, Position = pos, Emitting = false,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = extents,
            Direction = dir, Spread = spread, InitialVelocityMin = speed * 0.5f, InitialVelocityMax = speed,
            Gravity = grav, LocalCoords = false,
        };
        parent.AddChild(p);
        return p;
    }

    protected static float Rand01(ref uint s) { s = s * 1664525u + 1013904223u; return (s >> 8) / (float)(1 << 24); }
}
