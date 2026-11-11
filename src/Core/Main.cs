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
        AddChild(new Screens { Name = "Screens" });
        AddChild(new MenuUI { Name = "Menu" });

        int port = int.TryParse(Util.Arg("port", ""), out var pp) ? pp : Net.DefaultPort;
        if (Util.HasArg("autostart") && int.TryParse(Util.Arg("autostart"), out var n)) _autostart = n;

        if (Util.HasArg("host"))
        {
            if (Net.I.Host(port) != Error.Ok) { Util.Fail("host_start"); GetTree().Quit(1); }
        }
        else if (Util.HasArg("join"))
        {
            string ip = Util.Arg("join", "127.0.0.1"); if (ip == "") ip = "127.0.0.1";
            Net.I.ConnectionFailed += () => { Util.Fail("connected", "connection failed"); GetTree().Quit(1); };
            Net.I.Join(ip, port);
        }
        if (Util.HasArg("timeout") && double.TryParse(Util.Arg("timeout"), out var secs))
            GetTree().CreateTimer(secs).Timeout += () => { Util.Fail("timeout", "process hit --timeout"); GetTree().Quit(2); };
    }

    public override void _Process(double delta)
    {
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
