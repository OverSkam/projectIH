using Godot;

namespace AppStein;

/// <summary>Screenshot helper: `--screen=<name>` stages a state so tools/screenshot.sh can capture it. Does nothing without the arg.
/// goblin | goblin_happy | goblin_angry | miner | hands_pick | hands_lantern | hud | hud_idle | hud_pick | hud_full | dial | fuse | fuse10 | wicksdrain | downed | lootprompt | stuffprompt | lowoil | score | pause | matchend | settings | guide | ghost
/// (the minimal-HUD screens: hud_idle = nothing happened for a while, hud_pick = a ruby was just picked up, hud_full = heavy sack, dial = 55% of the round left (use --roundsecs=360), fuse = 34 s left, fuse10 = 6.5 s left, wicksdrain = Wicks drinking the oil)</summary>
public partial class UiDebug : Node
{
    string _screen;
    double _t, _rt = -1;
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
        ProcessPriority = -100;   // staged values are applied before the HUD reads them each frame
    }

    public override void _Process(double delta)
    {
        _t += delta;
        if (_done) return;
        var g = Game.I;
        bool inRound = g.Phase == GamePhase.Round && g.World?.Cave != null && (g.LocalMiner != null || g.LocalGhost != null);
        if (inRound) { if (_rt < 0) _rt = 0; _rt += delta; } else _rt = -1;
        switch (_screen)
        {
            case "goblin": case "goblin_happy": case "goblin_angry":
                if (!inRound || _t < 1.0) return;
                var gp = g.World.Cave.GoblinSeat;
                _cam = new Camera3D { Current = true, Fov = 40f, Near = 0.05f };
                g.World.AddChild(_cam);
                _cam.GlobalPosition = gp + new Vector3(0.0f, 6.0f, 13f);
                _cam.LookAt(gp + new Vector3(0f, 7.2f, 0f), Vector3.Up);
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
            case "lootprompt":
                if (!inRound || g.LocalMiner == null || _t < 1.0) return;
                {
                    var me = g.LocalMiner;
                    var dn = new Miner { Name = "9998", PeerId = 9998, PlayerName = "Gus", IsLocal = false };
                    g.World.MinersRoot.AddChild(dn);
                    var f = -me.GlobalTransform.Basis.Z;
                    dn.GlobalPosition = me.GlobalPosition + f * 2.1f;
                    dn.Rotation = new Vector3(0, me.Rotation.Y + 2.6f, 0);
                    dn.ApplyNetState(dn.GlobalPosition, dn.Rotation.Y, 0, 0);
                    dn.SetDowned(true);
                    (me.Controller as HumanController)?.SetLook(me.Rotation.Y, -0.62f);
                }
                _done = true; break;
            case "guide":
                if (_t > 0.5) { MenuUI.I?.ShowGuide(true); _done = true; }
                break;
            case "settings":
                if (_t > 0.5) { MenuUI.I?.ShowSettings(true); _done = true; }
                break;
            case "matchend":
                if (!inRound || _t < 1.0) return;
                Net.I.Names[11] = "Cam"; Net.I.Names[22] = "Alice"; Net.I.Names[33] = "Bob";
                g.TotalRounds = 2;
                g.DebugShowMatchEnd(new MatchEndData { Winner = "Alice", Names = new[] { "Alice", "Cam", "Bob" }, ThrownRound = new[] { 0, 2, 1 } });
                MenuUI.I.Visible = false;
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
                g.OwnInv.Sack.Clear(); g.OwnInv.Sack.AddRange(new[] { "coal", "coal", "coal", "iron", "iron", "silver", "gold", "coal", "iron", "silver" });
                g.OwnTribute = Ores.TributeSum(g.OwnInv.Sack); g.OwnBurden = Ores.BurdenSum(g.OwnInv.Sack); g.OwnOil = _screen == "lowoil" ? 38 : 150;
                if (g.LocalMiner != null) g.LocalMiner.Burden = g.OwnBurden;
                if (_screen == "downed") { g.OwnDowned = true; g.OwnDownedUntil = Time.GetTicksMsec() / 1000.0 + 22; g.OwnHealth = 0; }
                break;
            case "hud_idle": break;   // nothing staged: the HUD fades away by itself
            case "hud_pick":   // applied every frame (the server's own-state updates would overwrite it): coal + iron, then a ruby arrives at 3.2 s
                if (!inRound || _rt < 2.5 || g.LocalMiner == null) return;
                g.OwnInv.Sack.Clear(); g.OwnInv.Sack.AddRange(_rt < 3.2 ? new[] { "coal", "iron" } : new[] { "coal", "iron", "ruby" });
                g.OwnTribute = Ores.TributeSum(g.OwnInv.Sack); g.OwnBurden = Ores.BurdenSum(g.OwnInv.Sack); g.LocalMiner.Burden = g.OwnBurden;
                break;
            case "hud_full":
                if (!inRound || _rt < 2.5 || g.LocalMiner == null) return;
                g.OwnInv.Sack.Clear(); for (int i = 0; i < 9; i++) g.OwnInv.Sack.Add(i % 3 == 0 ? "coal" : i % 3 == 1 ? "iron" : "silver");
                g.OwnInv.Sack.Add("gold"); g.OwnInv.Sack.Add("ruby");
                g.OwnTribute = Ores.TributeSum(g.OwnInv.Sack); g.OwnBurden = Ores.BurdenSum(g.OwnInv.Sack); g.LocalMiner.Burden = g.OwnBurden;
                break;
            case "dial": if (inRound) g.TimeLeft = g.RoundTotal * 0.55f; break;
            case "fuse": if (inRound) g.TimeLeft = 34f; break;
            case "fuse10": if (inRound) g.TimeLeft = 6.5f; break;
            case "wicksdrain":
                if (!inRound || _rt < 3.0) return;
                if (!Hud.I.DebugDrain) Hud.I.ShowHint("Wicks drink lamp oil. Put the light out (L).", 8);
                Hud.I.DebugDrain = true; g.OwnOil = 120; break;
            case "stuffprompt":
                if (!inRound || g.LocalMiner == null || _t < 1.0) return;
                g.LocalMiner.DebugHoldProgress(0.6f);
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
