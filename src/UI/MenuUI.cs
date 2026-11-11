using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Animated cave backdrop: silhouette, drifting dust, flickering lantern.</summary>
public partial class MenuBackdrop : Control
{
    readonly List<Vector3> _dust = new(); // x,y in 0..1, z = speed/size seed
    double _t;
    readonly float[] _stal = new float[22];

    public MenuBackdrop()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var r = new RandomNumberGenerator { Seed = 7 };
        for (int i = 0; i < 70; i++) _dust.Add(new Vector3(r.Randf(), r.Randf(), r.Randf()));
        for (int i = 0; i < _stal.Length; i++) _stal[i] = r.RandfRange(0.05f, 0.3f);
    }

    public override void _Process(double delta) { _t += delta; QueueRedraw(); }

    public override void _Draw()
    {
        var s = Size;
        float flick = 0.85f + 0.1f * Mathf.Sin((float)_t * 9f) + 0.05f * Mathf.Sin((float)_t * 23f + 1f);
        var lp = new Vector2(s.X * 0.17f, s.Y * 0.6f);
        // background gradient
        for (int i = 0; i < 16; i++)
        {
            float k = i / 16f;
            DrawRect(new Rect2(0, s.Y * k, s.X, s.Y / 16f + 1), new Color(0.05f + 0.03f * k, 0.04f + 0.02f * k, 0.04f + 0.015f * k));
        }
        // lantern glow
        for (int i = 0; i < 9; i++)
            DrawCircle(lp, s.Y * (0.08f + i * 0.07f), new Color(1f, 0.6f, 0.2f, 0.045f * flick));
        // far arch / beams
        var beam = new Color(0.12f, 0.085f, 0.055f);
        for (int i = 0; i < 3; i++)
        {
            float x = s.X * (0.42f + i * 0.2f);
            DrawRect(new Rect2(x - 14, s.Y * 0.2f, 28, s.Y * 0.8f), beam.Darkened(0.4f * i * 0.3f));
            DrawRect(new Rect2(x - s.X * 0.1f, s.Y * 0.18f, s.X * 0.2f, 24), beam.Darkened(0.4f * i * 0.3f));
        }
        // goblin silhouette at a counter in the distance
        var gp = new Vector2(s.X * 0.82f, s.Y * 0.74f);
        var sil = new Color(0.03f, 0.025f, 0.02f);
        DrawRect(new Rect2(gp.X - 90, gp.Y, 180, 60), sil);
        DrawCircle(gp + new Vector2(0, -50), 26, sil);
        DrawColoredPolygon(new[] { gp + new Vector2(-24, -55), gp + new Vector2(-80, -85), gp + new Vector2(-22, -40) }, sil);
        DrawColoredPolygon(new[] { gp + new Vector2(24, -55), gp + new Vector2(80, -85), gp + new Vector2(22, -40) }, sil);
        DrawRect(new Rect2(gp.X - 34, gp.Y - 30, 68, 34), sil);
        DrawCircle(gp + new Vector2(-12, -52), 3.5f, new Color(1f, 0.8f, 0.3f, 0.8f * flick));
        DrawCircle(gp + new Vector2(12, -52), 3.5f, new Color(1f, 0.8f, 0.3f, 0.8f * flick));
        // stalactites
        var rock = new Color(0.02f, 0.018f, 0.016f);
        for (int i = 0; i < _stal.Length; i++)
        {
            float x = s.X * i / (_stal.Length - 1f); float w = s.X / _stal.Length * 1.3f; float h = s.Y * _stal[i];
            DrawColoredPolygon(new[] { new Vector2(x - w, 0), new Vector2(x + w, 0), new Vector2(x + w * 0.05f, h) }, rock);
        }
        DrawRect(new Rect2(0, 0, s.X, s.Y * 0.04f), rock);
        // floor rubble
        var pts = new List<Vector2> { new(0, s.Y) };
        for (int i = 0; i <= 24; i++) pts.Add(new Vector2(s.X * i / 24f, s.Y * (0.9f + 0.05f * Mathf.Sin(i * 1.7f) + 0.03f * Mathf.Cos(i * 3.1f))));
        pts.Add(new Vector2(s.X, s.Y));
        DrawColoredPolygon(pts.ToArray(), rock);
        // hanging lantern
        DrawLine(new Vector2(lp.X, 0), lp + new Vector2(0, -22), new Color(0.15f, 0.1f, 0.06f), 3f);
        DrawRect(new Rect2(lp.X - 11, lp.Y - 22, 22, 6), new Color(0.35f, 0.25f, 0.1f));
        DrawRect(new Rect2(lp.X - 9, lp.Y - 16, 18, 26), new Color(1f, 0.75f, 0.3f, 0.9f * flick));
        DrawRect(new Rect2(lp.X - 11, lp.Y + 10, 22, 5), new Color(0.35f, 0.25f, 0.1f));
        // dust
        foreach (var d in _dust)
        {
            float sp = 0.004f + d.Z * 0.01f;
            float x = Mathf.PosMod(d.X + (float)_t * sp * 0.6f, 1f), y = Mathf.PosMod(d.Y - (float)_t * sp * 0.3f + Mathf.Sin((float)_t * 0.5f + d.Z * 9f) * 0.002f, 1f);
            var p = new Vector2(x * s.X, y * s.Y);
            float near = Mathf.Clamp(1f - p.DistanceTo(lp) / (s.Y * 0.7f), 0f, 1f);
            DrawCircle(p, 1f + d.Z * 1.6f, new Color(1f, 0.85f, 0.6f, 0.08f + 0.5f * near * flick));
        }
        // vignette
        DrawRect(new Rect2(Vector2.Zero, s), new Color(0, 0, 0, 0.25f));
    }
}

/// <summary>Main menu + lobby + settings (one screen). Visible while Game.Phase == Lobby.</summary>
public partial class MenuUI : CanvasLayer
{
    public static MenuUI I;
    Control _root;
    LineEdit _name, _ip;
    Label _status;
    Button _start;
    VBoxContainer _menuBox, _lobbyBox, _playerList, _connectingBox;
    PanelContainer _mainPanel;
    SettingsPanel _settings;
    VBoxContainer _titleBox;
    Label _startHint, _roundsLabel, _ipLabel;

    public override void _Ready()
    {
        I = this;
        Layer = 10;
        UserSettings.Load();
        UserSettings.Apply(GetWindow());
        if (!Util.HasArg("name") && !Util.HasArg("bot") && UserSettings.PlayerName != "") Net.I.LocalName = UserSettings.PlayerName;

        _root = UiTheme.Apply(new Control());
        UiKit.FullRect(_root);
        AddChild(_root);
        var back = new MenuBackdrop(); UiKit.FullRect(back); _root.AddChild(back);

        var center = new CenterContainer(); UiKit.FullRect(center); _root.AddChild(center);
        var col = UiKit.VBox(14);
        col.Alignment = BoxContainer.AlignmentMode.Center;
        center.AddChild(col);

        _titleBox = UiKit.VBox(0);
        _titleBox.AddChild(UiKit.MakeTitle("APP STEIN", 92));
        _titleBox.AddChild(UiKit.MakeLabel("Dig deep. Pay the Goblin. Trust nobody.", 20, UiTheme.Dim, HorizontalAlignment.Center));
        col.AddChild(_titleBox);
        col.AddChild(UiKit.Spacer(8));

        _mainPanel = new PanelContainer { CustomMinimumSize = new Vector2(440, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        col.AddChild(_mainPanel);

        // ---- menu
        _menuBox = UiKit.VBox(10);
        _mainPanel.AddChild(_menuBox);
        _menuBox.AddChild(UiKit.MakeLabel("MINER NAME", 14, UiTheme.Brass));
        _name = new LineEdit { Text = Net.I.LocalName, PlaceholderText = "Your name", MaxLength = 16 };
        _name.TextChanged += t => { UserSettings.PlayerName = t; UserSettings.Save(); };
        _menuBox.AddChild(_name);
        _menuBox.AddChild(UiKit.MakeButton("Host game", OnHost, 160, true));
        _menuBox.AddChild(UiKit.Rule());
        _menuBox.AddChild(UiKit.MakeLabel("HOST ADDRESS", 14, UiTheme.Brass));
        _ip = new LineEdit { Text = UserSettings.LastIp, PlaceholderText = "Host IP address" };
        _ip.TextSubmitted += _ => OnJoin();
        _menuBox.AddChild(_ip);
        _menuBox.AddChild(UiKit.MakeButton("Join game", OnJoin));
        _menuBox.AddChild(UiKit.Rule());
        var hb = UiKit.HBox(10);
        var sb = UiKit.MakeButton("Settings", () => ShowSettings(true), 100); sb.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var qb = UiKit.MakeButton("Quit", () => GetTree().Quit(), 100); qb.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hb.AddChild(sb); hb.AddChild(qb);
        _menuBox.AddChild(hb);

        // ---- connecting
        _connectingBox = UiKit.VBox(12);
        _mainPanel.AddChild(_connectingBox);
        _connectingBox.AddChild(UiKit.MakeLabel("Connecting...", 26, UiTheme.Gold, HorizontalAlignment.Center));
        _connectingBox.AddChild(UiKit.MakeButton("Cancel", () => Game.I.LeaveToMenu(), 200));

        // ---- lobby
        _lobbyBox = UiKit.VBox(10);
        _mainPanel.AddChild(_lobbyBox);
        _lobbyBox.AddChild(UiKit.MakeTitle("LOBBY", 34));
        _roundsLabel = UiKit.MakeLabel("", 17, UiTheme.Dim, HorizontalAlignment.Center);
        _lobbyBox.AddChild(_roundsLabel);
        _lobbyBox.AddChild(UiKit.Rule());
        _playerList = UiKit.VBox(6);
        _lobbyBox.AddChild(_playerList);
        _lobbyBox.AddChild(UiKit.Rule());
        _ipLabel = UiKit.MakeLabel("", 16, UiTheme.Parchment, HorizontalAlignment.Center);
        _ipLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _lobbyBox.AddChild(_ipLabel);
        _start = UiKit.MakeButton("Start match", () => Game.I.ServerStartMatch(), 200, true);
        _lobbyBox.AddChild(_start);
        _startHint = UiKit.MakeLabel("", 15, UiTheme.Dim, HorizontalAlignment.Center);
        _lobbyBox.AddChild(_startHint);
        _lobbyBox.AddChild(UiKit.MakeButton("Leave", () => { _status.Text = ""; Game.I.LeaveToMenu(); }, 200));

        _status = UiKit.MakeLabel("", 18, new Color(1f, 0.55f, 0.4f), HorizontalAlignment.Center);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; _status.CustomMinimumSize = new Vector2(480, 0);
        col.AddChild(_status);

        _settings = new SettingsPanel(() => ShowSettings(false)) { Visible = false };
        var sc = new CenterContainer(); UiKit.FullRect(sc); sc.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(sc); sc.AddChild(_settings);

        Net.I.LobbyChanged += Refresh;
        Net.I.ConnectionFailed += () => { _status.Text = $"Could not reach the host at {_ip.Text.Trim()}. Check the address and that the host is running (UDP port {Net.DefaultPort})."; Refresh(); };
        Net.I.ServerLost += () => { _status.Text = "Lost connection to the host. The match is over."; Refresh(); };
        Game.I.PhaseChanged += Refresh;
        Game.I.ReturnedToMenu += Refresh;
        Refresh();
        UiDebug.Ensure(this);
    }

    public void ShowSettings(bool on)
    {
        _settings.Visible = on;
        _mainPanel.GetParent<Control>().GetParent<Control>().Visible = !on;
    }

    void OnHost()
    {
        Net.I.LocalName = _name.Text; UserSettings.PlayerName = _name.Text; UserSettings.Save();
        _status.Text = "";
        if (Net.I.Host() != Error.Ok) _status.Text = $"Could not host: UDP port {Net.DefaultPort} is busy (another game running?).";
        Refresh();
    }

    void OnJoin()
    {
        Net.I.LocalName = _name.Text; UserSettings.PlayerName = _name.Text; UserSettings.LastIp = _ip.Text.Trim(); UserSettings.Save();
        _status.Text = "";
        if (_ip.Text.Trim() == "") { _status.Text = "Type the host's IP address first."; return; }
        if (Net.I.Join(_ip.Text.Trim()) != Error.Ok) _status.Text = "That address does not look right.";
        Refresh();
    }

    public override void _Process(double delta)
    {
        if (Visible && _start != null)
        {
            _start.Disabled = !Game.I.CanStart;
            _startHint.Text = Net.I.IsServer ? (Game.I.CanStart ? "" : "At least 2 miners are needed.") : "";
        }
    }

    static IEnumerable<string> LocalIps()
    {
        var l = new List<string>();
        foreach (var a in IP.GetLocalAddresses())
            if (System.Text.RegularExpressions.Regex.IsMatch(a, @"^\d+\.\d+\.\d+\.\d+$") && !a.StartsWith("127.") && !a.StartsWith("169.254.")) l.Add(a);
        return l.OrderBy(a => a.StartsWith("192.168.") ? 0 : a.StartsWith("10.") ? 1 : 2).Take(4);
    }

    void Refresh()
    {
        Visible = Game.I.Phase == GamePhase.Lobby;
        if (!Visible) return;
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Visible;
        bool inLobby = Net.I.Online && Net.I.Names.Count > 0;
        bool connecting = Net.I.Online && !inLobby;
        _menuBox.Visible = !Net.I.Online;
        _connectingBox.Visible = connecting;
        _lobbyBox.Visible = inLobby;
        _titleBox.Visible = !inLobby;
        if (inLobby || connecting) { if (Net.I.IsServer || inLobby) { } }
        if (inLobby)
        {
            _roundsLabel.Text = $"{Game.I.Config.Rounds} rounds  -  most money wins";
            foreach (var c in _playerList.GetChildren()) c.QueueFree();
            foreach (var kv in Net.I.Names)
            {
                var row = UiKit.HBox(12);
                row.CustomMinimumSize = new Vector2(0, 38);
                var hi = new HelmetIcon(PlayerColors.Of(kv.Key), 36) { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
                row.AddChild(hi);
                var n = UiKit.MakeLabel(kv.Value, 24, kv.Key == Net.I.MyId ? UiTheme.Gold : UiTheme.Parchment);
                n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; n.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                row.AddChild(n);
                if (kv.Key == 1) { var h = UiKit.MakeLabel("HOST", 14, UiTheme.Gold, HorizontalAlignment.Center); h.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(h); }
                if (kv.Key == Net.I.MyId) { var y = UiKit.MakeLabel("you", 14, UiTheme.Dim, HorizontalAlignment.Center); y.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; row.AddChild(y); }
                _playerList.AddChild(row);
            }
            _start.Visible = Net.I.IsServer;
            if (Net.I.IsServer)
            {
                var ips = LocalIps().ToList();
                _ipLabel.Text = ips.Count > 0 ? "Friends join with:  " + string.Join("   /   ", ips) + $"   (UDP {Net.DefaultPort})" : $"No network address found. Friends on this PC can use 127.0.0.1 (UDP {Net.DefaultPort}).";
                _ipLabel.Visible = true;
            }
            else { _ipLabel.Text = "Waiting for the host to start..."; _ipLabel.Visible = true; _startHint.Text = ""; }
        }
    }
}
