using Godot;

namespace AppStein;

/// <summary>Screenshot helper: `--screen=<name>` stages a state so tools/screenshot.sh can capture it. Does nothing without the arg.
/// goblin | goblin_happy | goblin_angry | miner | hands_pick | hands_lantern | hud | downed | feedprompt | feedprompt_stein | lowoil | score | pause | matchend | settings | guide | ghost</summary>
public partial class UiDebug : Node
{
    string _screen;
    double _t;
    bool _done;
    Camera3D _cam;

    public static void Ensure(Node parent)
    {
        if (!Util.HasArg("screen")) return;
        parent.AddChild(new UiDebug { Name = "UiDebug" });
    }

    public override void _Ready()
    {
        _screen = Util.Arg("screen");
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_done) return;
        var g = Game.I;
        bool inRound = g.Phase == GamePhase.Round && g.World?.Cave != null && (g.LocalMiner != null || g.LocalGhost != null);
        switch (_screen)
        {
            case "goblin": case "goblin_happy": case "goblin_angry":
                if (!inRound || _t < 1.0) return;
                var gp = g.World.Cave.GoblinPos;
                _cam = new Camera3D { Current = true, Fov = 55f, Near = 0.05f };
                g.World.AddChild(_cam);
                _cam.GlobalPosition = gp + new Vector3(0.0f, 1.75f, 2.6f);
                _cam.LookAt(gp + new Vector3(0f, 1.5f, 0f), Vector3.Up);
                Hud.I.Visible = false;
                if (_screen == "goblin_happy") GoblinRig.React("happy");
                if (_screen == "goblin_angry") GoblinRig.React("angry");
                _done = true; break;
            case "miner":
                if (!inRound || g.LocalMiner == null || _t < 1.0) return;
                var lm = g.LocalMiner;
                var other = new Miner { Name = "9999", PeerId = 9999, PlayerName = "Gus", IsLocal = false };
                g.World.MinersRoot.AddChild(other);
                var fwd = -lm.GlobalTransform.Basis.Z;
                other.GlobalPosition = lm.GlobalPosition + fwd * 3.2f + lm.GlobalTransform.Basis.X * 0.8f;
                other.Rotation = new Vector3(0, lm.Rotation.Y + 2.6f, 0);
                other.SetHeld("pickaxe");
                other.ApplyNetState(other.GlobalPosition, other.Rotation.Y, 0, 0);
                _done = true; break;
            case "guide":
                if (_t > 0.5) { MenuUI.I?.ShowGuide(true); _done = true; }
                break;
            case "settings":
                if (_t > 0.5) { MenuUI.I?.ShowSettings(true); _done = true; }
                break;
            case "matchend":
                if (_t < 0.5) return;
                Net.I.Names[11] = "Cam"; Net.I.Names[22] = "Alice"; Net.I.Names[33] = "Bob";
                g.TotalRounds = 2;
                g.LastMatchEnd = new MatchEndData { Winner = "Alice", Names = new[] { "Alice", "Cam", "Bob" }, ThrownRound = new[] { 0, 2, 1 } };
                g.Phase = GamePhase.MatchEnd;
                MenuUI.I.Visible = false; Screens.I.Refresh();
                _done = true; break;
            case "pause":
                if (!inRound || _t < 1.0) return;
                PauseMenu.I.Open(true); _done = true; break;
            case "score":
                if (!inRound || _t < 1.0) return;
                Net.I.Names[11] = "Cam"; Net.I.Names[22] = "Alice"; Net.I.Names[33] = "Bob";
                g.Status[11] = MinerStatus.Downed; g.Status[22] = MinerStatus.Dead; g.Status[33] = MinerStatus.Eliminated;
                Hud.I.ForceScoreboard = true; _done = true; break;
            case "hud": case "downed": case "lowoil":
                if (!inRound || _t < 1.0) return;
                g.OwnTribute = 14; g.OwnOil = _screen == "lowoil" ? 38 : 150;
                if (_screen == "downed") { g.OwnDowned = true; g.OwnDownedUntil = Time.GetTicksMsec() / 1000.0 + 22; g.OwnHealth = 0; }
                break;
            case "feedprompt": case "feedprompt_stein":
                if (!inRound || g.LocalMiner == null || _t < 1.0) return;
                var gob = g.World.Cave.GoblinPos;
                g.LocalMiner.Teleport(gob + new Vector3(0, 0, 2.6f), 0);
                g.LocalMiner.FaceYaw(0f);
                if (_screen == "feedprompt_stein") g.LocalMiner.DebugHoldProgress(0.6f);
                break;
            case "hands_pick":
            case "hands_lantern":
                if (!inRound || g.LocalMiner == null || _t < 1.0) return;
                Hud.I.Visible = false;
                g.LocalMiner.SetHeld(_screen == "hands_pick" ? "pickaxe" : "lantern");
                if (_screen == "hands_pick" && _t > 1.0) { _done = true; }
                else _done = true;
                break;
        }
    }
}
