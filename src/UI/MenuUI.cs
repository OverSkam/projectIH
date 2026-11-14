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
    LineEdit _name, _code, _joinPw, _hostPw, _sHostPw, _sCode, _sJoinPw;
    VBoxContainer _steamBox, _directBox;
    CheckBox _anyone;
    Label _steamNote, _directHead;
    Button _directToggle;
    bool _advanced;
    Label _status;
    Button _start;
    VBoxContainer _menuBox, _lobbyBox, _playerList, _connectingBox;
    PanelContainer _mainPanel;
    SettingsPanel _settings;
    GuidePanel _guide;
    VBoxContainer _titleBox;
    Label _startHint, _roundsLabel, _ipLabel;
    RoomInfoPanel _roomInfo;
    LockIcon _lobbyLock;

    public override void _Ready()
    {
        I = this;
        Layer = 10;
        UserSettings.Load();
        UserSettings.Apply(GetWindow());
        if (!Util.HasArg("name") && !Util.HasArg("bot") && UserSettings.PlayerName != "") Net.I.LocalName = UserSettings.PlayerName;
        else if (!Util.HasArg("name") && !Util.HasArg("bot") && SteamService.Available && SteamService.PersonaName != "")
            Net.I.LocalName = SteamService.PersonaName.Length > 16 ? SteamService.PersonaName.Substring(0, 16) : SteamService.PersonaName;   // Steam mode: your Steam name unless you typed another

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

        _mainPanel = new PanelContainer { CustomMinimumSize = new Vector2(480, 0), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        col.AddChild(_mainPanel);

        // ---- menu
        _menuBox = UiKit.VBox(10);
        _mainPanel.AddChild(_menuBox);
        _menuBox.AddChild(UiKit.MakeLabel("MINER NAME", 14, UiTheme.Brass));
        _name = new LineEdit { Text = Net.I.LocalName, PlaceholderText = "Your name", MaxLength = 16 };
        _name.TextChanged += t => { UserSettings.PlayerName = t; UserSettings.Save(); };
        _menuBox.AddChild(_name);
        _menuBox.AddChild(UiKit.Rule());

        // Steam flow (the default when Steam is running): host / join through Steam, invites do the rest
        _steamBox = UiKit.VBox(8);
        _menuBox.AddChild(_steamBox);
        var hostHead = UiKit.HBox(8);
        var hl = UiKit.MakeLabel("HOST A ROOM", 14, UiTheme.Brass); hl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; hl.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        hostHead.AddChild(hl);
        _anyone = new CheckBox { Text = "Open to anyone with the code", FocusMode = Control.FocusModeEnum.None, TooltipText = "Off: only your Steam friends can join (invite or code)." };
        _anyone.AddThemeFontSizeOverride("font_size", 14);
        hostHead.AddChild(_anyone);
        _steamBox.AddChild(hostHead);
        var shRow = UiKit.HBox(10);
        _sHostPw = new LineEdit { PlaceholderText = "Password (optional)", Secret = true, MaxLength = 32, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _sHostPw.TextSubmitted += _ => OnHostSteam();
        shRow.AddChild(_sHostPw);
        shRow.AddChild(UiKit.MakeButton("Host game", OnHostSteam, 150, true));
        _steamBox.AddChild(shRow);
        _steamBox.AddChild(UiKit.Rule());
        _steamBox.AddChild(UiKit.MakeLabel("JOIN A ROOM", 14, UiTheme.Brass));
        var scRow = UiKit.HBox(10);
        _sCode = new LineEdit { PlaceholderText = "Room code", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _sCode.TextSubmitted += _ => OnJoinSteam();
        scRow.AddChild(_sCode);
        scRow.AddChild(UiKit.MakeButton("Paste", () => { _sCode.Text = DisplayServer.ClipboardGet().Trim(); _sCode.CaretColumn = _sCode.Text.Length; }, 90));
        _steamBox.AddChild(scRow);
        var sjRow = UiKit.HBox(10);
        _sJoinPw = new LineEdit { PlaceholderText = "Password (if the room has one)", Secret = true, MaxLength = 32, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _sJoinPw.TextSubmitted += _ => OnJoinSteam();
        sjRow.AddChild(_sJoinPw);
        sjRow.AddChild(UiKit.MakeButton("Join game", OnJoinSteam, 150));
        _steamBox.AddChild(sjRow);
        _steamBox.AddChild(UiKit.MakeLabel("or accept a Steam invite from a friend", 15, UiTheme.Dim, HorizontalAlignment.Center));

        // Direct connection (room code with an IP address inside, UPnP): the only flow without Steam, "advanced" with it
        _directBox = UiKit.VBox(10);
        _menuBox.AddChild(_directBox);
        _steamNote = UiKit.MakeLabel("Steam is not running — start Steam to invite friends", 16, new Color(1f, 0.72f, 0.4f), HorizontalAlignment.Center);
        _steamNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _directBox.AddChild(_steamNote);
        _directHead = UiKit.MakeLabel("DIRECT CONNECTION", 14, UiTheme.Brass);
        _directBox.AddChild(_directHead);
        _directBox.AddChild(UiKit.MakeLabel("HOST A ROOM", 14, UiTheme.Brass));
        var hostRow = UiKit.HBox(10);
        _hostPw = new LineEdit { PlaceholderText = "Password (optional)", Secret = true, MaxLength = 32, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _hostPw.TextSubmitted += _ => OnHost();
        hostRow.AddChild(_hostPw);
        hostRow.AddChild(UiKit.MakeButton("Host game", OnHost, 150, true));
        _directBox.AddChild(hostRow);
        _directBox.AddChild(UiKit.Rule());
        _directBox.AddChild(UiKit.MakeLabel("JOIN A ROOM", 14, UiTheme.Brass));
        var codeRow = UiKit.HBox(10);
        _code = new LineEdit { Text = UserSettings.LastIp == "127.0.0.1" ? "" : UserSettings.LastIp, PlaceholderText = "Room code", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _code.TextSubmitted += _ => OnJoin();
        codeRow.AddChild(_code);
        codeRow.AddChild(UiKit.MakeButton("Paste", () => { _code.Text = DisplayServer.ClipboardGet().Trim(); _code.CaretColumn = _code.Text.Length; }, 90));
        _directBox.AddChild(codeRow);
        var joinRow = UiKit.HBox(10);
        _joinPw = new LineEdit { PlaceholderText = "Password (if the room has one)", Secret = true, MaxLength = 32, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _joinPw.TextSubmitted += _ => OnJoin();
        joinRow.AddChild(_joinPw);
        joinRow.AddChild(UiKit.MakeButton("Join game", OnJoin, 150));
        _directBox.AddChild(joinRow);

        _directToggle = new Button { Text = "Direct connection (advanced)", Flat = true, FocusMode = Control.FocusModeEnum.None, SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(0, 28) };
        _directToggle.AddThemeFontSizeOverride("font_size", 14);
        _directToggle.AddThemeColorOverride("font_color", UiTheme.Brass);
        _directToggle.AddThemeColorOverride("font_hover_color", UiTheme.Gold);
        _directToggle.Pressed += () => { Sfx.Play(SfxNames.UiClick); _advanced = !_advanced; ApplyMode(); };
        _menuBox.AddChild(_directToggle);
        _menuBox.AddChild(UiKit.Rule());
        var hb = UiKit.HBox(10);
        var sb = UiKit.MakeButton("Settings", () => ShowSettings(true), 100); sb.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var qb = UiKit.MakeButton("Quit", () => GetTree().Quit(), 100); qb.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var gb = UiKit.MakeButton("Guide", () => ShowGuide(true), 100); gb.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hb.AddChild(gb); hb.AddChild(sb); hb.AddChild(qb);
        _menuBox.AddChild(hb);
        ApplyMode();

        // ---- connecting
        _connectingBox = UiKit.VBox(12);
        _mainPanel.AddChild(_connectingBox);
        _connectingBox.AddChild(UiKit.MakeLabel("Connecting...", 26, UiTheme.Gold, HorizontalAlignment.Center));
        _connectingBox.AddChild(UiKit.MakeButton("Cancel", () => Game.I.LeaveToMenu(), 200));

        // ---- lobby
        _lobbyBox = UiKit.VBox(10);
        _mainPanel.AddChild(_lobbyBox);
        var lobbyHead = UiKit.HBox(10); lobbyHead.Alignment = BoxContainer.AlignmentMode.Center;
        lobbyHead.AddChild(UiKit.MakeTitle("LOBBY", 34));
        _lobbyLock = new LockIcon(26) { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
        lobbyHead.AddChild(_lobbyLock);
        _lobbyBox.AddChild(lobbyHead);
        _roundsLabel = UiKit.MakeLabel("", 17, UiTheme.Dim, HorizontalAlignment.Center);
        _lobbyBox.AddChild(_roundsLabel);
        _lobbyBox.AddChild(UiKit.Rule());
        _playerList = UiKit.VBox(6);
        _lobbyBox.AddChild(_playerList);
        _lobbyBox.AddChild(UiKit.Rule());
        _ipLabel = UiKit.MakeLabel("", 16, UiTheme.Parchment, HorizontalAlignment.Center);
        _ipLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _lobbyBox.AddChild(_ipLabel);
        _roomInfo = new RoomInfoPanel(440, 26) { Visible = false };
        _lobbyBox.AddChild(_roomInfo);
        _start = UiKit.MakeButton("Start match", () => Game.I.ServerStartMatch(), 200, true);
        _lobbyBox.AddChild(_start);
        _startHint = UiKit.MakeLabel("", 15, UiTheme.Dim, HorizontalAlignment.Center);
        _lobbyBox.AddChild(_startHint);
        var leaveRow = UiKit.HBox(10); leaveRow.Alignment = BoxContainer.AlignmentMode.Center;
        leaveRow.AddChild(UiKit.MakeButton("Guide", () => ShowGuide(true), 120));
        leaveRow.AddChild(UiKit.MakeButton("Leave", () => { _status.Text = ""; Game.I.LeaveToMenu(); }, 200));
        _lobbyBox.AddChild(leaveRow);

        _status = UiKit.MakeLabel("", 18, new Color(1f, 0.55f, 0.4f), HorizontalAlignment.Center);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart; _status.CustomMinimumSize = new Vector2(480, 0);
        col.AddChild(_status);

        _settings = new SettingsPanel(() => ShowSettings(false)) { Visible = false };
        var sc = new CenterContainer(); UiKit.FullRect(sc); sc.MouseFilter = Control.MouseFilterEnum.Ignore;
        _root.AddChild(sc); sc.AddChild(_settings);
        _guide = new GuidePanel(() => ShowGuide(false)) { Visible = false };
        sc.AddChild(_guide);

        Net.I.LobbyChanged += Refresh;
        Net.I.ConnectionFailed += () =>
        {
            _status.Text = Net.I.FailReason != "" ? Net.I.FailReason : "Could not connect to the host.";
            if (Net.I.LastSteamLobby != 0) { var c = SteamLobbyCode.Encode(Net.I.LastSteamLobby); if (c != null && _sCode.Text.Trim() == "") _sCode.Text = c; }   // an invite that needed a password: keep the code so the password can be typed
            Refresh();
        };
        Net.I.Notice += msg => { _status.Text = msg; Refresh(); };
        Net.I.ServerLost += () => { _status.Text = "Lost connection to the host. The match is over."; Refresh(); };
        Game.I.PhaseChanged += Refresh;
        Game.I.ReturnedToMenu += Refresh;
        Refresh();
        UiDebug.Ensure(this);
    }

    public void ShowGuide(bool on)
    {
        _guide.Visible = on;
        _mainPanel.GetParent<Control>().GetParent<Control>().Visible = !on;
    }

    public void ShowSettings(bool on)
    {
        _settings.Visible = on;
        _mainPanel.GetParent<Control>().GetParent<Control>().Visible = !on;
    }

    /// <summary>Which menu block shows: Steam flow by default when Steam is there; the direct (IP code) block when it is not, or on request.</summary>
    void ApplyMode()
    {
        bool steam = SteamService.UiSteam;
        _steamBox.Visible = steam && !_advanced;
        _directBox.Visible = !steam || _advanced;
        _steamNote.Visible = !steam;
        _directHead.Visible = steam && _advanced;
        _directToggle.Visible = steam;
        _directToggle.Text = _advanced ? "Back to Steam" : "Direct connection (advanced)";
    }

    void SaveName() { Net.I.LocalName = _name.Text; UserSettings.PlayerName = _name.Text; UserSettings.Save(); }

    void OnHostSteam()
    {
        SaveName();
        _status.Text = "";
        Net.I.HostSteam(_sHostPw.Text, _anyone.ButtonPressed);
        Refresh();
    }

    void OnJoinSteam()
    {
        SaveName();
        _status.Text = "";
        string text = _sCode.Text;
        if (text.Trim().Length == 0) { _status.Text = "Type the room code first."; return; }
        if (!SteamLobbyCode.LooksLikeLobbyCode(text)) { _status.Text = "That is not a Steam room code. For an IP-address code use Direct connection (advanced)."; return; }
        if (!SteamLobbyCode.TryDecode(text, out var lobby)) { _status.Text = "That code is not valid. Check it for typos and try again."; return; }
        Net.I.JoinSteam(lobby, _sJoinPw.Text);
        Refresh();
    }

    void OnHost()
    {
        Net.I.LocalName = _name.Text; UserSettings.PlayerName = _name.Text; UserSettings.Save();
        _status.Text = "";
        if (Net.I.Host(Net.DefaultPort, _hostPw.Text) != Error.Ok) _status.Text = $"Could not host: UDP port {Net.DefaultPort} is busy (another game running?).";
        Refresh();
    }

    void OnJoin()
    {
        Net.I.LocalName = _name.Text; UserSettings.PlayerName = _name.Text; UserSettings.LastIp = _code.Text.Trim(); UserSettings.Save();
        _status.Text = "";
        if (SteamLobbyCode.LooksLikeLobbyCode(_code.Text))   // a Steam room code typed into the direct field
        {
            if (!SteamService.Available) { _status.Text = "That is a Steam room code. Start Steam to join it."; return; }
            if (!SteamLobbyCode.TryDecode(_code.Text, out var lobby)) { _status.Text = "That code is not valid. Check it for typos and try again."; return; }
            Net.I.JoinSteam(lobby, _joinPw.Text); Refresh(); return;
        }
        if (!RoomCode.Resolve(_code.Text, Net.DefaultPort, out var host, out var port, out var err)) { _status.Text = err; return; }
        if (Net.I.Join(host, port, _joinPw.Text) != Error.Ok) _status.Text = "That address does not look right.";
        Refresh();
    }

    /// <summary>Screenshot staging (--screen=join): types an example into the join form.</summary>
    public void DebugFillJoin(string code, string password, string status)
    {
        (_steamBox.Visible ? _sCode : _code).Text = code; (_steamBox.Visible ? _sJoinPw : _joinPw).Text = password; _status.Text = status;
    }

    public override void _Process(double delta)
    {
        if (Visible && _start != null)
        {
            _start.Disabled = !Game.I.CanStart;
            _startHint.Text = Net.I.IsServer ? (Game.I.CanStart ? "" : "At least 2 miners are needed.") : "";
        }
    }

    void Refresh()
    {
        Visible = Game.I.Phase == GamePhase.Lobby;
        if (!Visible) return;
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Visible;
        bool inLobby = Net.I.Online && Net.I.Names.Count > 0;
        bool connecting = (Net.I.Online && !inLobby) || Net.I.JoiningSteam;
        _menuBox.Visible = !Net.I.Online && !Net.I.JoiningSteam;
        _connectingBox.Visible = connecting;
        _lobbyBox.Visible = inLobby;
        _titleBox.Visible = !inLobby;
        if (inLobby || connecting) { if (Net.I.IsServer || inLobby) { } }
        if (inLobby)
        {
            _roundsLabel.Text = $"{MatchConfig.RoundsFor(Net.I.Names.Count)} round{(Net.I.Names.Count == 2 ? "" : "s")}  -  the lightest miner is thrown each round, the last one wins";
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
            _lobbyLock.Visible = Net.I.HasPassword;
            _roomInfo.Visible = Net.I.IsServer;
            if (Net.I.IsServer) _ipLabel.Visible = false;
            else { _ipLabel.Text = "Waiting for the host to start..."; _ipLabel.Visible = true; _startHint.Text = ""; }
        }
    }
}
