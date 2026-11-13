using System;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Screenshot staging for monsters (tools/screenshot.sh): `--cammonster=golem|plug|wicks|lamplighter|assessor|lampcompare [--camdist=4] [--camdark] [--camroom=0]` (camroom makes the debug camera exist).
/// The monster is spawned frozen in an open spot (plug: inside a tunnel), faces the camera; `lampcompare` puts a Lamplighter and a real miner holding a lantern side by side.
/// Use with `--nomonsters` so only the staged one exists. Does nothing without the argument.
/// </summary>
public static class MonsterStage
{
    static string _mode = "";
    static double _t;
    static bool _done;

    public static void Begin(Node n) { _mode = Util.Arg("cammonster", ""); _t = 0; _done = false; }

    public static void Tick(MonsterDirector d, float dt)
    {
        if (_mode == "" || _done) return;
        _t += dt;
        var g = Game.I;
        if (g.Phase != GamePhase.Round || g.World?.Cave == null || g.LocalMiner == null || _t < 1.5) return;
        var cam = g.World.GetNodeOrNull<Camera3D>("DebugCam");
        if (cam == null) { Util.Log("STAGE: needs --camroom=0 (debug camera)"); _done = true; return; }
        _done = true;
        float dist = float.TryParse(Util.Arg("camdist", _mode == "lampcompare" ? "12" : "4"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dd) ? dd : 4f;
        string kind = _mode == "lampcompare" ? "lamplighter" : _mode;
        var space = d.GetViewport().World3D.DirectSpaceState;
        Vector3 monPos, camPos, dir;
        if (kind == "plug" && ModularCave.Last is { } mc0 && mc0.Tunnels.Count > 0)
        {
            var t = mc0.Tunnels.FirstOrDefault(x => x.Kind == 0) ?? mc0.Tunnels[0];
            dir = t.AlongX ? Vector3.Right : Vector3.Back;
            var c2 = ModularCave.CellPos(t.Cells[2].i, t.Cells[2].j, 0f);
            var gy = Floor(space, c2);
            monPos = new Vector3(c2.X, gy, c2.Z);
            camPos = monPos - dir * dist + new Vector3(0, 1.6f, 0);
        }
        else
        {
            if (!FindSpot(g.World.Cave, space, dist + 1f, out monPos, out camPos, out dir, dist)) { Util.Log("STAGE: no open spot found"); return; }
        }
        float lateral = _mode == "lampcompare" ? 0.7f : 0f;
        var side = new Vector3(-dir.Z, 0, dir.X);
        var mon = MonsterModule.Spawn<MonsterObj>(kind, monPos + side * lateral);
        if (mon != null)
        {
            mon.Frozen = true;
            var toCam = camPos - mon.GlobalPosition;
            mon.Yaw = Mathf.Atan2(-toCam.X, -toCam.Z);
            mon.Rotation = new Vector3(0, mon.Yaw, 0);
        }
        if (_mode == "lampcompare")
        {
            var other = new Miner { Name = "9999", PeerId = 9999, PlayerName = "Gus", IsLocal = false };
            g.World.MinersRoot.AddChild(other);
            var p = monPos - side * lateral;
            other.GlobalPosition = p;
            float yaw = Mathf.Atan2(-(camPos.X - p.X), -(camPos.Z - p.Z));
            other.Rotation = new Vector3(0, yaw, 0);
            other.SetHeld("lantern");
            other.ApplyNetState(p, yaw, 0, 0);
        }
        // eye height 1.6; look at the monster's chest
        cam.GlobalPosition = camPos;
        var look = monPos + new Vector3(0, kind == "plug" ? 1.1f : 1.3f, 0);
        cam.LookAt(look, Vector3.Up);
        Util.Log($"STAGE: {_mode} monster at {monPos} camera at {camPos} dist={dist}");
    }

    static float Floor(PhysicsDirectSpaceState3D space, Vector3 p)
    {
        var q = PhysicsRayQueryParameters3D.Create(p + new Vector3(0, 1.5f, 0), p + new Vector3(0, -3f, 0), Layers.World);
        var r = space.IntersectRay(q);
        return r.Count > 0 ? r["position"].AsVector3().Y : p.Y;
    }

    /// <summary>A node in a roomy place with a clear straight line of 'need' metres in some direction (camera at the near end, monster 'dist' m away).</summary>
    static bool FindSpot(CaveInfo cave, PhysicsDirectSpaceState3D space, float need, out Vector3 monPos, out Vector3 camPos, out Vector3 dir, float dist)
    {
        var nav = MonsterNav.For(cave);
        monPos = camPos = Vector3.Zero; dir = Vector3.Forward;
        var dirs = new[] { Vector3.Right, Vector3.Left, Vector3.Forward, Vector3.Back };
        for (int k = 0; k < nav.Count; k++)
        {
            if (nav.Hall[k] || nav.Ceil[k] < 3f || nav.HallDist[k] < 12f) continue;
            var p = nav.Pos[k];
            foreach (var dv in dirs)
            {
                bool ok = true;
                for (float s = 0; s <= need + 1f && ok; s += 1f)
                {
                    var a = p + dv * s + new Vector3(0, 1.2f, 0);
                    var q = PhysicsRayQueryParameters3D.Create(a, a + dv * 1f, Layers.World);
                    if (space.IntersectRay(q).Count > 0) ok = false;
                    var gq = PhysicsRayQueryParameters3D.Create(a, a + new Vector3(0, -2.2f, 0), Layers.World);
                    if (space.IntersectRay(gq).Count == 0) ok = false;
                }
                if (!ok) continue;
                // side clearance for the lantern comparison
                var sd = new Vector3(-dv.Z, 0, dv.X);
                var cs = PhysicsRayQueryParameters3D.Create(p + new Vector3(0, 1.2f, 0) + dv * dist, p + new Vector3(0, 1.2f, 0) + dv * dist + sd * 1.2f, Layers.World);
                if (space.IntersectRay(cs).Count > 0) continue;
                camPos = p + new Vector3(0, 1.6f, 0);
                monPos = new Vector3(p.X + dv.X * dist, Floor(space, p + dv * dist), p.Z + dv.Z * dist);
                dir = dv;
                return true;
            }
        }
        return false;
    }
}
