using Godot;

namespace AppStein;

/// <summary>Root scene script: builds the UI layers and handles command-line automation.</summary>
public partial class Main : Node
{
    int _autostart = -1;
    double _autoTimer;
    bool _started;

    public override void _Ready()
    {
        if (SfxTools.Run(GetTree())) return;
        if (Util.HasArg("name")) Net.I.LocalName = Util.Arg("name");
        else if (Util.HasArg("bot")) Net.I.LocalName = "Bot" + (int)(GD.Randi() % 1000);

        AddChild(new Hud { Name = "Hud" });
        AddChild(new WeighingUi { Name = "WeighingUi" });
        AddChild(new Screens { Name = "Screens" });
        AddChild(new MenuUI { Name = "Menu" });

        int port = int.TryParse(Util.Arg("port", ""), out var pp) ? pp : Net.DefaultPort;
        if (Util.HasArg("autostart") && int.TryParse(Util.Arg("autostart"), out var n)) _autostart = n;

        if (Util.HasArg("host"))
        {
            if (Net.I.Host(port, Util.Arg("password", "")) != Error.Ok) { Util.Fail("host_start"); GetTree().Quit(1); }
        }
        else if (Util.HasArg("join"))
        {
            string target = Util.Arg("join", "127.0.0.1"); if (target == "") target = "127.0.0.1";
            if (!RoomCode.Resolve(target, port, out var ip, out var rport, out var rerr)) { Util.Fail("connected", rerr); GetTree().Quit(1); return; }
            Net.I.ConnectionFailed += () => { Util.Log("JOIN_REJECTED: " + Net.I.FailReason); Util.Fail("connected", Net.I.FailReason); GetTree().Quit(1); };
            Net.I.Join(ip, rport, Util.Arg("password", ""));
        }
        if (Util.HasArg("timeout") && double.TryParse(Util.Arg("timeout"), out var secs))
            GetTree().CreateTimer(secs).Timeout += () => { Util.Fail("timeout", "process hit --timeout"); GetTree().Quit(2); };
    }

    public override void _ExitTree() { Queries.DisposeAll(); }

    public override void _Process(double delta)
    {
        if (Util.HasArg("nohud")) foreach (var n in new[] { "Hud", "WeighingUi", "Screens" }) if (GetNodeOrNull(n) is CanvasLayer cl) cl.Visible = false;   // screenshot staging
        if (_autostart > 0 && !_started && Net.I.IsServer && Game.I.Phase == GamePhase.Lobby && Net.I.Names.Count >= _autostart)
        {
            _autoTimer += delta;
            if (_autoTimer > 1.5)
            {
                _started = true;
                Util.Pass("peers_connected", $"count={Net.I.Names.Count}");
                Game.I.ServerStartMatch();
            }
        }
    }
}
