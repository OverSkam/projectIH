using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

namespace AppStein;

/// <summary>
/// Sound engine. All sounds are synthesised in code (SfxRecipes) into AudioStreamWav buffers at startup.
/// Does nothing in headless mode (dummy audio) unless `--sfxsim` is given, in which case plays are only counted.
/// Play(name, pos) keeps the signature of the original stub.
/// </summary>
public static class Sfx
{
    public static bool Active;                 // engine usable (real playback or simulation)
    public static bool Sim;                    // headless simulation: count instead of play
    public static double GenMs;
    public static int BufferCount;
    public static readonly Dictionary<string, int> Counts = new();

    static readonly Dictionary<string, AudioStreamWav[]> Bank = new();
    static readonly Dictionary<string, AudioStreamPlayer> Loops2D = new();
    static readonly Dictionary<string, AudioStreamPlayer3D> Loops3D = new();
    static readonly HashSet<string> Warned = new();
    static readonly Random Rnd = new();
    static readonly List<AudioStreamPlayer3D> Pool3D = new();
    static readonly List<AudioStreamPlayer> Pool2D = new();
    static AudioDirector _node;
    const int Pool3DSize = 32, Pool2DSize = 12;

    public static bool IsHeadless => DisplayServer.GetName() == "headless";

    sealed class Raw { public Recipe Rc; public float[][] Data; }
    static System.Threading.Tasks.Task<(List<Raw> list, double ms)> _gen;

    /// <summary>Pure DSP for every recipe (thread-safe, parallel). No engine objects are touched.</summary>
    static (List<Raw>, double) Compute()
    {
        var sw = Stopwatch.StartNew();
        var res = new System.Collections.Concurrent.ConcurrentBag<Raw>();
        System.Threading.Tasks.Parallel.ForEach(SfxRecipes.Table.Values, rc =>
        {
            var d = new float[rc.Variants][];
            for (int v = 0; v < rc.Variants; v++) d[v] = SfxRecipes.Render(rc, v, out _);
            res.Add(new Raw { Rc = rc, Data = d });
        });
        return (new List<Raw>(res), sw.Elapsed.TotalMilliseconds);
    }

    static void Commit(List<Raw> list, double ms)
    {
        Bank.Clear(); BufferCount = 0;
        foreach (var r in list)
        {
            var arr = new AudioStreamWav[r.Data.Length];
            for (int v = 0; v < arr.Length; v++)
            {
                var x = r.Data[v];
                var w = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = Syn.SR, Stereo = false, Data = Syn.ToPcm16(x) };
                if (r.Rc.Loop) { w.LoopMode = AudioStreamWav.LoopModeEnum.Forward; w.LoopBegin = 0; w.LoopEnd = x.Length; }
                arr[v] = w; BufferCount++;
            }
            Bank[r.Rc.Name] = arr;
        }
        GenMs = ms;
    }

    /// <summary>Generates every buffer synchronously (dump/check tools). Returns elapsed milliseconds of the DSP part.</summary>
    public static double GenerateAll()
    {
        var (l, ms) = Compute();
        Commit(l, ms);
        return ms;
    }

    /// <summary>Director calls this each frame until the background generation has finished.</summary>
    public static bool PollReady()
    {
        if (_gen == null) return true;
        if (!_gen.IsCompleted) return false;
        var (l, ms) = _gen.Result; _gen = null;
        Commit(l, ms);
        Util.Log($"SFX: {SfxRecipes.Table.Count} sounds, {BufferCount} buffers generated in {ms:0} ms (background)" + (Sim ? " (sim)" : ""));
        return true;
    }

    /// <summary>Called once at startup on every peer (AudioModule.Register).</summary>
    public static void Init()
    {
        if (_node != null) return;
        try
        {
            Sim = Util.HasArg("sfxsim");
            if (IsHeadless && !Sim) return;               // dummy audio: no buffers, no players, nothing to fail
            if (Util.HasArg("sfxcheck") || Util.HasArg("sfxdump")) return; // tools render on their own
            MakeBuses();
            _gen = System.Threading.Tasks.Task.Run(Compute);
            Active = true;
            _node = new AudioDirector { Name = "AudioDirector", ProcessMode = Node.ProcessModeEnum.Always };
            ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred(Node.MethodName.AddChild, _node);
        }
        catch (Exception e) { Active = false; GD.Print("SFX: init failed, audio disabled: " + e.Message); }
    }

    static void MakeBuses()
    {
        foreach (var n in new[] { "SFX", "Ambience", "UI" })
        {
            if (AudioServer.GetBusIndex(n) >= 0) continue;
            AudioServer.AddBus();
            int i = AudioServer.BusCount - 1;
            AudioServer.SetBusName(i, n);
            AudioServer.SetBusSend(i, "Master");
        }
        int sfx = AudioServer.GetBusIndex("SFX");
        AudioServer.AddBusEffect(sfx, new AudioEffectReverb { RoomSize = 0.75f, Damping = 0.55f, Wet = 0.2f, Dry = 1f, Spread = 1f, Hipass = 0.05f });
        AudioServer.AddBusEffect(0, new AudioEffectHardLimiter { CeilingDb = -0.5f });
        UserSettings.Load();
        AudioServer.SetBusVolumeDb(0, UserSettings.Volume <= 0.001f ? -80f : Mathf.LinearToDb(UserSettings.Volume));
    }

    // ------------------------------------------------------------ playback
    static bool Resolve(string name, out Recipe rc, out AudioStreamWav stream)
    {
        rc = null; stream = null;
        if (!Active || string.IsNullOrEmpty(name)) return false;
        if (!SfxRecipes.Table.TryGetValue(name, out rc))
        {
            if (Warned.Add(name)) GD.Print("SFX: unknown sound name '" + name + "'");
            return false;
        }
        if (Sim) { Counts[name] = Counts.GetValueOrDefault(name) + 1; return false; }
        if (_gen != null || !Bank.TryGetValue(name, out var arr) || _node == null || !_node.IsInsideTree()) return false;
        stream = arr[arr.Length == 1 ? 0 : Rnd.Next(arr.Length)];
        return true;
    }

    static float Vary(Recipe rc, out float pitch)
    {
        pitch = rc.PitchVar > 0 ? 1f + (float)(Rnd.NextDouble() * 2 - 1) * rc.PitchVar : 1f;
        return rc.VolVar > 0 ? (float)(Rnd.NextDouble() * 2 - 1) * rc.VolVar : 0f;
    }

    /// <summary>World position -> positional pooled player; null -> non-positional. dbOffset adds to the recipe loudness.</summary>
    public static void Play(string name, Vector3? pos = null, float dbOffset = 0f)
    {
        try
        {
            if (!Resolve(name, out var rc, out var stream)) return;
            float vdb = Vary(rc, out float pitch);
            if (pos is Vector3 p)
            {
                var cam = _node.GetViewport()?.GetCamera3D();
                if (cam != null && cam.GlobalPosition.DistanceTo(p) > rc.MaxDist) return;
                var pl = Take3D();
                if (pl == null) return;
                Configure(pl, rc);
                pl.Stream = stream; pl.VolumeDb = rc.Db + vdb + dbOffset; pl.PitchScale = pitch;
                pl.GlobalPosition = p;
                pl.Play();
            }
            else Play2D(rc, stream, vdb + dbOffset, pitch);
        }
        catch (Exception e) { if (Warned.Add("ex:" + name)) GD.Print("SFX: play failed: " + e.Message); }
    }

    /// <summary>Non-positional (UI / own-body sounds).</summary>
    public static void PlayUi(string name, float dbOffset = 0f)
    {
        try
        {
            if (!Resolve(name, out var rc, out var stream)) return;
            float vdb = Vary(rc, out float pitch);
            Play2D(rc, stream, vdb + dbOffset, pitch);
        }
        catch (Exception e) { if (Warned.Add("ex:" + name)) GD.Print("SFX: play failed: " + e.Message); }
    }

    static void Play2D(Recipe rc, AudioStreamWav stream, float db, float pitch)
    {
        var pl = Take2D();
        if (pl == null) return;
        pl.Bus = rc.Bus; pl.Stream = stream; pl.VolumeDb = rc.Db + db; pl.PitchScale = pitch;
        pl.Play();
    }

    static AudioStreamPlayer3D Take3D()
    {
        foreach (var p in Pool3D) if (!p.Playing) return p;
        if (Pool3D.Count >= Pool3DSize) return null;
        var n = new AudioStreamPlayer3D { AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance, AttenuationFilterCutoffHz = 4000f, MaxDb = 3f };
        _node.AddChild(n); Pool3D.Add(n);
        return n;
    }

    static AudioStreamPlayer Take2D()
    {
        foreach (var p in Pool2D) if (!p.Playing) return p;
        if (Pool2D.Count >= Pool2DSize) return null;
        var n = new AudioStreamPlayer();
        _node.AddChild(n); Pool2D.Add(n);
        return n;
    }

    static void Configure(AudioStreamPlayer3D p, Recipe rc)
    {
        p.Bus = rc.Bus; p.MaxDistance = rc.MaxDist; p.UnitSize = Mathf.Max(1f, rc.MaxDist / 8f);
    }

    // ------------------------------------------------------------ loops
    /// <summary>Starts a looping sound under a unique key; attached to `at` (positional, freed with it) or global when null. No-op if already running.</summary>
    public static void Loop(string key, string name, Node3D at = null, float dbOffset = 0f)
    {
        try
        {
            if (!Active) return;
            if (Sim) { if (!Loops3D.ContainsKey(key) && !Loops2D.ContainsKey(key)) { Loops2D[key] = null; Counts["loop:" + name] = Counts.GetValueOrDefault("loop:" + name) + 1; } return; }
            if (IsLooping(key)) return;
            if (!Resolve(name, out var rc, out var stream)) return;
            if (at != null && GodotObject.IsInstanceValid(at))
            {
                var p = new AudioStreamPlayer3D { Stream = stream, Bus = rc.Bus, MaxDistance = rc.MaxDist, UnitSize = Mathf.Max(1f, rc.MaxDist / 8f), VolumeDb = rc.Db + dbOffset, AttenuationModel = AudioStreamPlayer3D.AttenuationModelEnum.InverseSquareDistance, AttenuationFilterCutoffHz = 4000f };
                at.AddChild(p); p.Play(); Loops3D[key] = p;
            }
            else
            {
                var p = new AudioStreamPlayer { Stream = stream, Bus = rc.Bus, VolumeDb = rc.Db + dbOffset };
                _node.AddChild(p); p.Play(); Loops2D[key] = p;
            }
        }
        catch (Exception e) { if (Warned.Add("ex:" + name)) GD.Print("SFX: loop failed: " + e.Message); }
    }

    public static bool IsLooping(string key)
    {
        if (Loops3D.TryGetValue(key, out var a)) { if (GodotObject.IsInstanceValid(a)) return true; Loops3D.Remove(key); }
        if (Loops2D.TryGetValue(key, out var b)) { if (Sim || GodotObject.IsInstanceValid(b)) return true; Loops2D.Remove(key); }
        return false;
    }

    public static void StopLoop(string key)
    {
        if (Loops3D.Remove(key, out var a) && GodotObject.IsInstanceValid(a)) a.QueueFree();
        if (Loops2D.Remove(key, out var b) && b != null && GodotObject.IsInstanceValid(b)) b.QueueFree();
    }

    /// <summary>Drops bookkeeping for loops whose nodes were freed (round change). Cheap; called by the director.</summary>
    public static void Prune()
    {
        List<string> dead = null;
        foreach (var kv in Loops3D) if (!GodotObject.IsInstanceValid(kv.Value)) (dead ??= new()).Add(kv.Key);
        if (dead != null) foreach (var k in dead) Loops3D.Remove(k);
    }

    public static void OnExit()
    {
        if (!Sim || Counts.Count == 0) return;
        var parts = new List<string>(); foreach (var kv in Counts) parts.Add($"{kv.Key}={kv.Value}");
        parts.Sort();
        Util.Log("SFXSIM: " + string.Join(" ", parts));
    }
}
