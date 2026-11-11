using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Godot;

namespace AppStein;

/// <summary>Debug commands: `-- --sfxdump=&lt;dir&gt;` renders every sound to .wav and prints a table; `-- --sfxcheck` validates the sound table (exit code 1 on failure).</summary>
public static class SfxTools
{
    /// <summary>Returns true if a tool ran (the process is quitting).</summary>
    public static bool Run(SceneTree tree)
    {
        bool dump = Util.HasArg("sfxdump"), check = Util.HasArg("sfxcheck");
        if (!dump && !check) return false;
        int fails = 0;
        void Fail(string m) { fails++; GD.Print("FAIL: sfxcheck " + m); }

        double ms = Sfx.GenerateAll();
        GD.Print($"SFXGEN sounds={SfxRecipes.Table.Count} buffers={Sfx.BufferCount} ms={ms:0}");
        if (ms > 1000) Fail($"startup generation too slow: {ms:0} ms");

        // registry <-> table
        var names = new HashSet<string>(SfxNames.All);
        foreach (var n in names) if (!SfxRecipes.Table.ContainsKey(n)) Fail($"name '{n}' has no recipe");
        foreach (var n in SfxRecipes.Table.Keys) if (!names.Contains(n)) Fail($"recipe '{n}' is not in SfxNames");
        if (names.Count != SfxNames.All.Count) Fail("duplicate sound name constants");

        string dir = dump ? Util.Arg("sfxdump") : "";
        if (dump)
        {
            if (string.IsNullOrEmpty(dir)) { GD.Print("FAIL: sfxdump needs a directory: --sfxdump=<dir>"); tree.Quit(1); return true; }
            Directory.CreateDirectory(dir);
        }
        var sb = new StringBuilder();
        sb.AppendLine($"{"name",-24}{"variant",8}{"dur_s",8}{"peak_dB",9}{"rms_dB",9}{"dc",9}  loop");
        foreach (var rc in SfxRecipes.Table.Values)
            for (int v = 0; v < rc.Variants; v++)
            {
                var x = SfxRecipes.Render(rc, v, out var raw);
                string id = $"{rc.Name}{(rc.Variants > 1 ? "#" + (v + 1) : "")}";
                bool nan = false; foreach (var s in raw) if (!float.IsFinite(s)) { nan = true; break; }
                if (nan) Fail($"{id}: NaN/Inf in raw buffer");
                var q = new float[x.Length]; for (int i = 0; i < q.Length; i++) q[i] = MathF.Round(x[i] * 32767f) / 32767f;   // as stored
                var st = Syn.Analyse(q);
                float pkDb = 20f * MathF.Log10(Math.Max(st.Peak, 1e-9f)), rmsDb = 20f * MathF.Log10(Math.Max(st.Rms, 1e-9f));
                sb.AppendLine($"{id,-24}{v + 1,8}{st.Dur,8:0.00}{pkDb,9:0.0}{rmsDb,9:0.0}{st.Dc,9:0.0000}  {(rc.Loop ? "loop" : "")}");
                if (st.Nan) Fail($"{id}: NaN in buffer");
                if (st.Peak > 1.0f) Fail($"{id}: peak above 0 dBFS");
                if (st.Peak < 0.5f) Fail($"{id}: peak too low ({pkDb:0.0} dB), expected about -3 dBFS");
                if (st.Rms < 0.004f) Fail($"{id}: silent or nearly silent (rms {rmsDb:0.0} dB)");
                if (MathF.Abs(st.Dc) > 0.005f) Fail($"{id}: DC offset {st.Dc:0.0000}");
                if (rc.Loop ? (st.Dur < 0.5f || st.Dur > 10f) : (st.Dur < 0.03f || st.Dur > SfxRecipes.MaxOneShotSec)) Fail($"{id}: duration {st.Dur:0.00}s out of range");
                if (rc.Loop && MathF.Abs(q[0] - q[q.Length - 1]) > 0.6f) Fail($"{id}: loop seam jump");
                if (!rc.Loop && (MathF.Abs(q[0]) > 0.02f || MathF.Abs(q[q.Length - 1]) > 0.02f)) Fail($"{id}: ends are not faded");
                if (dump) File.WriteAllBytes(Path.Combine(dir, id.Replace('#', '_') + ".wav"), Syn.WavFile(q));
            }
        GD.Print(sb.ToString());
        if (fails == 0) GD.Print("PASS: sfxcheck " + SfxRecipes.Table.Count + " sounds");
        tree.Quit(fails == 0 ? 0 : 1);
        return true;
    }
}
