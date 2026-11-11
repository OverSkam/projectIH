using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Main menu + lobby (one screen). Visible while Game.Phase == Lobby.</summary>
public partial class MenuUI : CanvasLayer
{
    LineEdit _name, _ip;
    Label _status, _players;
    Button _host, _join, _start, _leave;
    VBoxContainer _connectBox, _lobbyBox;

    public override void _Ready()
    {
        Layer = 10;
        var bg = new ColorRect { Color = new Color(0.04f, 0.03f, 0.03f) };
        UiKit.FullRect(bg);
        AddChild(bg);
        var center = new CenterContainer();
        UiKit.FullRect(center);
        AddChild(center);
        var panel = UiKit.MakePanel(new Color(0.12f, 0.1f, 0.08f, 0.95f));
        center.AddChild(panel);
        var v = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        v.AddThemeConstantOverride("separation", 10);
        panel.AddChild(v);
        v.AddChild(UiKit.MakeLabel("APP STEIN", 40, new Color(1f, 0.8f, 0.2f), HorizontalAlignment.Center));
        v.AddChild(UiKit.MakeLabel("Dig deep. Pay the Goblin. Trust nobody.", 14, Colors.Gray, HorizontalAlignment.Center));

        _connectBox = new VBoxContainer();
        _connectBox.AddThemeConstantOverride("separation", 8);
        v.AddChild(_connectBox);
        _name = new LineEdit { Text = Net.I.LocalName, PlaceholderText = "Your name", MaxLength = 16 };
        _connectBox.AddChild(UiKit.MakeLabel("Name", 14));
        _connectBox.AddChild(_name);
        _host = UiKit.MakeButton("Host game", OnHost);
        _connectBox.AddChild(_host);
        _connectBox.AddChild(UiKit.MakeLabel("Host IP", 14));
        _ip = new LineEdit { Text = "127.0.0.1", PlaceholderText = "Host IP address" };
        _connectBox.AddChild(_ip);
        _join = UiKit.MakeButton("Join game", OnJoin);
        _connectBox.AddChild(_join);

        _lobbyBox = new VBoxContainer { Visible = false };
        _lobbyBox.AddThemeConstantOverride("separation", 8);
        v.AddChild(_lobbyBox);
        _lobbyBox.AddChild(UiKit.MakeLabel("Lobby (2-8 players)", 18, Colors.White));
        _players = UiKit.MakeLabel("", 18);
        _lobbyBox.AddChild(_players);
        _start = UiKit.MakeButton("Start match", () => Game.I.ServerStartMatch());
        _lobbyBox.AddChild(_start);
        _leave = UiKit.MakeButton("Leave", () => Game.I.LeaveToMenu());
        _lobbyBox.AddChild(_leave);

        _status = UiKit.MakeLabel("", 14, new Color(1f, 0.6f, 0.4f), HorizontalAlignment.Center);
        v.AddChild(_status);

        Net.I.LobbyChanged += Refresh;
        Net.I.ConnectionFailed += () => { _status.Text = "Could not connect to host."; Refresh(); };
        Net.I.ServerLost += () => { _status.Text = "The host left the game."; Refresh(); };
        Game.I.PhaseChanged += Refresh;
        Game.I.ReturnedToMenu += Refresh;
        Refresh();
    }

    void OnHost()
    {
        Net.I.LocalName = _name.Text;
        _status.Text = "";
        if (Net.I.Host() != Error.Ok) _status.Text = "Could not host (port 7777 busy?).";
        Refresh();
    }

    void OnJoin()
    {
        Net.I.LocalName = _name.Text;
        _status.Text = "Connecting...";
        if (Net.I.Join(_ip.Text.Trim()) != Error.Ok) _status.Text = "Could not start connection.";
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (Visible && _start != null) _start.Disabled = !Game.I.CanStart;
    }

    void Refresh()
    {
        Visible = Game.I.Phase == GamePhase.Lobby;
        if (Visible) Input.MouseMode = Input.MouseModeEnum.Visible;
        bool inLobby = Net.I.Online && Net.I.Names.Count > 0;
        _connectBox.Visible = !Net.I.Online;
        _lobbyBox.Visible = inLobby;
        if (inLobby)
        {
            _players.Text = string.Join("\n", Net.I.Names.Select(kv => kv.Value + (kv.Key == 1 ? "  (host)" : "") + (kv.Key == Net.I.MyId ? "  (you)" : "")));
            _start.Visible = Net.I.IsServer;
            if (!Net.I.IsServer) _status.Text = "Waiting for the host to start...";
        }
        else if (Net.I.Online) _status.Text = _status.Text == "" ? "Connecting..." : _status.Text;
    }
}
