using Godot;

namespace AppStein;

/// <summary>Screenshot helper: `--screen=<name>` stages a state so tools/screenshot.sh can capture it. Does nothing without the arg.
/// goblin | goblin_happy | goblin_angry | miner | hands_pick | hands_lantern | hud | hud_idle | hud_pick | hud_full | dial | fuse | fuse10 | wicksdrain | downed | lootprompt | stuffprompt | lowoil | score | pause | matchend | settings | guide | ghost | steamroom | steampause (Steam-mode host lobby / pause menu, need --fakesteam) | join (join form with an example code; the host lobby is `--host --fakeupnp=ready|checking|failed --password=x`)
/// (the minimal-HUD screens: hud_idle = nothing happened for a while, hud_pick = a ruby was just picked up, hud_full = heavy sack, dial = 55% of the round left (use --roundsecs=360), fuse = 34 s left, fuse10 = 6.5 s left, wicksdrain = Wicks drinking the oil)</summary>
public partial class UiDebug : Node
{
    string _screen;
    double _t, _rt = -1;
    bool _done;
    Camera3D _cam; Miner _shoveA; double _stealAt = -1;

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
            case "sacks": case "sacks_far": case "pickprompt": case "shove":
                if (!inRound || g.LocalMiner == null || _t < 1.0) return;
                {
                    var me = g.LocalMiner;
                    var f = -me.GlobalTransform.Basis.Z; var r = me.GlobalTransform.Basis.X; float yaw = me.Rotation.Y;
                    Miner Dummy(int id, string name, Vector3 at, float yw, int step)
                    {
                        var d = new Miner { Name = id.ToString(), PeerId = id, PlayerName = name, IsLocal = false };
                        g.World.MinersRoot.AddChild(d);
                        d.GlobalPosition = at; d.Rotation = new Vector3(0, yw, 0); d.SetHeld(id % 2 == 0 ? "pickaxe" : "lantern");
                        d.ApplyNetState(at, yw, 0, 0); d.SetSackStep(step);
                        return d;
                    }
                    if (_screen == "sacks")   // three miners seen from behind at 3 m: sack steps 0, 2, 4
                    { Dummy(9001, "Ann", me.GlobalPosition + f * 3f - r * 2.5f, yaw, 0); Dummy(9002, "Ben", me.GlobalPosition + f * 3f - r * 1.2f, yaw, 2); Dummy(9003, "Cy", me.GlobalPosition + f * 3f + r * 0.1f, yaw, 4); }
                    else if (_screen == "sacks_far")   // the five steps at 7 m
                    { for (int i = 0; i < 5; i++) Dummy(9001 + i, "M" + i, me.GlobalPosition + f * 7f + r * ((i - 2) * 1.5f - 2.2f), yaw, i); }
                    else if (_screen == "pickprompt")
                    {
                        Dummy(9001, "Ann", me.GlobalPosition + f * 1.35f, yaw, 3);
                        me.DebugHoldProgress(0.55f);
                    }
                    else   // shove: Ann lunges at Ben (her palms land on his back); the camera sees it from the side
                    {
                        float ry = Mathf.Atan2(-r.X, -r.Z);   // both face the camera's right
                        var basePos = me.GlobalPosition + f * 3.6f - r * 1.0f;
                        var ann = Dummy(9001, "Ann", basePos, ry, 1); var ben = Dummy(9002, "Ben", basePos + r * 1.15f, ry, 3);
                        _shoveA = ann;
                    }
                }
                _done = _screen != "shove" && _screen != "pickprompt"; if (_screen == "shove") _screen = "shove_hold"; else if (_screen == "pickprompt") _screen = "pick_hold";
                break;
            case "loot_hold":   // the downed-miner prompt rows (a bot controller never runs UpdateTarget)
                if (g.LocalMiner != null) g.LocalMiner.PromptRows = new[] { "Hold E\tHelp Gus up", "C\tRob Gus", "Hold RMB\tDrag" };
                break;
            case "pick_hold":   // staged rows every frame (a bot controller never runs UpdateTarget)
                if (g.LocalMiner != null) g.LocalMiner.PromptRows = new[] { "Hold E\t— pick the sack" };
                break;
            case "shove_hold": if (_shoveA != null && IsInstanceValid(_shoveA)) _shoveA.DebugShovePose(0.22f); break;
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
                _screen = "loot_hold"; break;
            case "join":   // the join form filled in, with a refusal showing
                if (_t > 0.5) { MenuUI.I?.DebugFillJoin("K7QM-4T2P-9XDA", "hunter2", "Wrong password."); _done = true; }
                break;
            case "steamroom":   // host lobby as it looks in Steam mode (use with --host --fakesteam; --anyone for the open-room wording)
                if (_t > 0.5) { Net.I.DebugSteamRoom(SteamLobbyCode.Encode(109775241012345678UL), Util.HasArg("anyone")); _done = true; }
                break;
            case "steampause":   // pause menu as the host of a Steam room (use with --host --fakesteam --autostart=1 --preset=debug --bot=idle)
                if (!inRound || _t < 1.0) return;
                Net.I.DebugSteamRoom(SteamLobbyCode.Encode(109775241012345678UL), false);
                PauseMenu.I.Open(true); _done = true; break;
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
                if (_screen == "downed") { Net.I.Names[7001] = "Anna"; Net.I.Names[7002] = "Ben"; g.DownedActors[7001] = (1, 1e12); g.DownedActors[7002] = (2, 1e12); g.OwnDowned = true; g.OwnDownedUntil = Time.GetTicksMsec() / 1000.0 + 22; g.OwnHealth = 0; }
                break;
            case "hud_idle": break;   // nothing staged: the HUD fades away by itself
            case "hud_pick":   // applied every frame (the server's own-state updates would overwrite it): coal + iron, then a ruby arrives at 3.2 s
                if (!inRound || _rt < 2.5 || g.LocalMiner == null) return;
                g.OwnInv.Sack.Clear(); g.OwnInv.Sack.AddRange(_rt < 3.2 ? new[] { "coal", "iron" } : new[] { "coal", "iron", "ruby" });
                g.OwnTribute = Ores.TributeSum(g.OwnInv.Sack); g.OwnBurden = Ores.BurdenSum(g.OwnInv.Sack); g.LocalMiner.Burden = g.OwnBurden;
                break;
            case "steal":   // a ruby is lifted out of the sack at 3.2 s: red "-10 RUBY" toast and the sack twitches
                if (!inRound || _rt < 2.5 || g.LocalMiner == null) return;
                {
                    bool gone = _rt >= 3.2;
                    g.OwnInv.Sack.Clear(); g.OwnInv.Sack.AddRange(gone ? new[] { "coal", "iron" } : new[] { "coal", "iron", "ruby" });
                    if (gone && _stealAt < 0) { _stealAt = _rt; g.StolenOre = "ruby"; g.StolenAt = Time.GetTicksMsec() / 1000.0; }
                    g.OwnTribute = Ores.TributeSum(g.OwnInv.Sack); g.OwnBurden = Ores.BurdenSum(g.OwnInv.Sack); g.LocalMiner.Burden = g.OwnBurden;
                }
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
