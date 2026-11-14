using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

[ContentModule]
public static class FriendsModule
{
    public static void Register()
    {
        if (Net.I?.Friends == null) return;
        Game.I.CallDeferred(Node.MethodName.AddChild, new InviteBanner { Name = "InviteBanner" });
        if (Util.HasArg("screen")) Game.I.CallDeferred(Node.MethodName.AddChild, new FriendsDebug { Name = "FriendsDebug" });
    }
}

/// <summary>A coloured dot for a friend's state.</summary>
public partial class StateDot : Control
{
    readonly FriendState _st;
    public StateDot(FriendState st) { _st = st; MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(16, 16); SizeFlagsVertical = SizeFlags.ShrinkCenter; }
    public override void _Draw()
    {
        var c = _st switch { FriendState.InGame => UiTheme.Gold, FriendState.Online => UiTheme.Good, FriendState.Away => new Color(0.95f, 0.62f, 0.25f), _ => new Color(0.4f, 0.38f, 0.35f) };
        DrawCircle(Size / 2f, 6.5f, new Color(0, 0, 0, 0.7f));
        DrawCircle(Size / 2f, 5f, c);
    }
}

/// <summary>
/// "Invite friends": an in-game list of the Steam friends (this game first, then online, then away; offline behind a toggle) with an Invite button each.
/// Replaces the Steam overlay dialog, which only exists when Steam launched the process. One overlay for the whole game (lobby and pause menu both open it).
/// </summary>
public partial class FriendsOverlay : CanvasLayer
{
    public static FriendsOverlay I;
    Control _root;
    FriendsModel _model;
    LineEdit _filter;
    CheckBox _offline;
    VBoxContainer _rows;
    ScrollContainer _scroll;
    Label _status, _empty;
    Button _overlayLink;
    string _sig = "";
    bool _dirty;
    string _note = ""; double _noteUntil;

    public static bool IsOpen => I != null && I._root.Visible;

    public static void Open()
    {
        if (Net.I?.Friends == null) return;
        if (I == null || !IsInstanceValid(I)) { I = new FriendsOverlay { Name = "FriendsOverlay" }; ((SceneTree)Engine.GetMainLoop()).Root.AddChild(I); }
        I.Show(true);
    }

    public static void Close() { if (I != null && IsInstanceValid(I)) I.Show(false); }

    static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        Layer = 30;
        ProcessMode = ProcessModeEnum.Always;
        _model = new FriendsModel(Net.I.Friends, () => Now, () => Net.I.CurrentLobbyId);
        Net.I.Friends.Changed += () => _dirty = true;

        _root = UiTheme.Apply(new Control { Visible = false });
        UiKit.FullRect(_root);
        AddChild(_root);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.66f) };
        UiKit.FullRect(dim);
        dim.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true }) Show(false); };
        _root.AddChild(dim);
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(center);
        _root.AddChild(center);

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(600, 0) };
        panel.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Stone, UiTheme.Brass, 8, 2, 22, 18));   // opaque: it sits over the lobby or the pause menu
        center.AddChild(panel);
        var v = UiKit.VBox(8);
        panel.AddChild(v);
        v.AddChild(UiKit.MakeTitle("INVITE FRIENDS", 32));
        _status = UiKit.MakeLabel("", 15, UiTheme.Dim, HorizontalAlignment.Center, 3);
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        v.AddChild(_status);
        v.AddChild(UiKit.Rule());

        var top = UiKit.HBox(10);
        _filter = new LineEdit { PlaceholderText = "Filter by name", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, ClearButtonEnabled = true, CustomMinimumSize = new Vector2(0, 38) };
        _filter.TextChanged += t => { _model.Filter = t; Rebuild(true); };
        top.AddChild(_filter);
        _offline = new CheckBox { Text = "Show offline", FocusMode = Control.FocusModeEnum.None };
        _offline.AddThemeFontSizeOverride("font_size", 16);
        _offline.Toggled += on => { _model.ShowOffline = on; Rebuild(true); };
        top.AddChild(_offline);
        v.AddChild(top);

        _scroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        v.AddChild(_scroll);
        var inner = UiKit.VBox(0);
        inner.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _scroll.AddChild(inner);
        _rows = UiKit.VBox(3); _rows.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        inner.AddChild(_rows);
        _empty = UiKit.MakeLabel("", 17, UiTheme.Dim, HorizontalAlignment.Center, 3);
        _empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        inner.AddChild(_empty);

        v.AddChild(UiKit.Rule());
        var foot = UiKit.HBox(12);
        _overlayLink = new Button { Text = "Open Steam overlay", Flat = true, FocusMode = Control.FocusModeEnum.None, Visible = false, TooltipText = "Steam's own invite window (works when Steam started the game)" };
        _overlayLink.AddThemeFontSizeOverride("font_size", 15);
        _overlayLink.AddThemeColorOverride("font_color", UiTheme.Brass);
        _overlayLink.AddThemeColorOverride("font_hover_color", UiTheme.Gold);
        _overlayLink.Pressed += () => { if (Net.I.Friends.OpenOverlayInvite(Net.I.CurrentLobbyId)) Show(false); };
        foot.AddChild(_overlayLink);
        foot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        foot.AddChild(UiKit.MakeButton("Close", () => Show(false), 140, true));
        v.AddChild(foot);
    }

    public void Show(bool on)
    {
        if (on == _root.Visible) return;
        _root.Visible = on;
        if (on)
        {
            _filter.Text = ""; _model.Filter = ""; _note = "";
            _model.Refresh(); _sig = "";
            _overlayLink.Visible = Net.I.Friends.OverlayEnabled;
            Rebuild(false);
            _scroll.ScrollVertical = 0;
            if (DisplayServer.GetName() != "headless" && Input.MouseMode != Input.MouseModeEnum.Visible) Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        Sfx.Play(on ? SfxNames.UiOpen : SfxNames.UiClose);
    }

    public override void _Input(InputEvent e)
    {
        if (!_root.Visible) return;
        if (e.IsActionPressed("pause")) { Show(false); GetViewport().SetInputAsHandled(); }
    }

    public override void _Process(double delta)
    {
        if (!_root.Visible) return;
        bool due = _model.Tick();
        if (_dirty && !due) _model.Refresh();
        _dirty = false;
        if (_note != "" && Now > _noteUntil) _note = "";
        UpdateStatus();
        // rebuild only when something shown changed (friend data, or a button that counts down: "Invited" for 10 s)
        if (FriendsModel.Signature(_model.Rows()) != _sig) Rebuild(true);
    }

    void UpdateStatus()
    {
        if (Net.I.CurrentLobbyId == 0) { _status.Text = "The room is not ready yet. The Invite buttons work in a moment."; _status.AddThemeColorOverride("font_color", new Color(1f, 0.72f, 0.4f)); }
        else if (_note != "") { _status.Text = _note; _status.AddThemeColorOverride("font_color", UiTheme.Good); }
        else
        {
            _status.Text = "Your friend gets a Steam invite. They can accept it in Steam, or in the game: a banner offers Join.";
            _status.AddThemeColorOverride("font_color", UiTheme.Dim);
        }
    }

    void Rebuild(bool keepScroll)
    {
        int scroll = keepScroll ? _scroll.ScrollVertical : 0;
        var rows = _model.Rows();
        _sig = FriendsModel.Signature(rows);
        foreach (var c in _rows.GetChildren()) { _rows.RemoveChild(c); c.QueueFree(); }
        foreach (var r in rows)
        {
            var f = r.Friend;
            var h = UiKit.HBox(10);
            h.CustomMinimumSize = new Vector2(0, 38);
            h.AddChild(new StateDot(f.State));
            var n = UiKit.MakeLabel(f.Name, 20, f.State == FriendState.Offline ? UiTheme.Dim : UiTheme.Parchment, HorizontalAlignment.Left, 3);
            n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; n.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; n.ClipText = true; n.CustomMinimumSize = new Vector2(120, 0);
            h.AddChild(n);
            var d = UiKit.MakeLabel(f.Detail, 14, f.State == FriendState.InGame ? UiTheme.Gold : UiTheme.Dim, HorizontalAlignment.Right, 3);
            d.CustomMinimumSize = new Vector2(112, 0); d.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            h.AddChild(d);
            var b = new Button { FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(104, 32), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
            b.AddThemeFontSizeOverride("font_size", 16);
            switch (r.Button)
            {
                case FriendsModel.Btn.Invite: b.Text = "Invite"; ulong id = f.Id; string nm = f.Name; b.Pressed += () => OnInvite(id, nm); b.MouseEntered += () => Sfx.Play(SfxNames.UiHover); break;
                case FriendsModel.Btn.Invited: b.Text = "Invited"; b.Disabled = true; break;
                case FriendsModel.Btn.Failed: b.Text = "Failed"; b.Disabled = true; break;
                default: b.Text = "Invite"; b.Disabled = true; break;
            }
            h.AddChild(b);
            _rows.AddChild(h);
        }
        string empty = "";
        if (rows.Count == 0)
        {
            if (!Net.I.Friends.Available) empty = "Steam is not available.";
            else if (_model.Filter.Trim().Length > 0) empty = "Nobody matches that name.";
            else empty = _model.ShowOffline ? "No Steam friends found." : "No friends are online right now.";
        }
        if (!_model.ShowOffline && _model.HiddenOffline > 0) empty += (empty == "" ? "" : "\n") + $"{_model.HiddenOffline} offline friend{(_model.HiddenOffline == 1 ? "" : "s")} hidden (tick Show offline).";
        _empty.Text = empty; _empty.Visible = empty != "";
        _offline.Text = _model.HiddenOffline > 0 && !_model.ShowOffline ? $"Show offline ({_model.HiddenOffline})" : "Show offline";
        CallDeferred(nameof(RestoreScroll), scroll);
    }

    /// <summary>Screenshot staging.</summary>
    public void DebugFilter(string text, bool showOffline) { _filter.Text = text; _model.Filter = text; _offline.ButtonPressed = showOffline; _model.ShowOffline = showOffline; Rebuild(false); }

    void RestoreScroll(int v) { if (IsInstanceValid(_scroll)) _scroll.ScrollVertical = v; }

    void OnInvite(ulong id, string name)
    {
        bool ok = _model.Invite(id);
        _note = ok ? $"Invited {name}." : $"Could not invite {name}. Is Steam online?";
        _noteUntil = Now + 6;
        Rebuild(true);
    }
}

/// <summary>"Name invited you - Join / Dismiss": a small non-blocking banner for 30 s when a Steam invite arrives while the game is running (menu and lobby).
/// Join leaves the current room first (the banner's button is the confirmation).</summary>
public partial class InviteBanner : CanvasLayer
{
    InviteBannerModel _model;
    PanelContainer _panel; Label _text;
    static double Now => Time.GetTicksMsec() / 1000.0;

    public override void _Ready()
    {
        Layer = 12;
        ProcessMode = ProcessModeEnum.Always;
        _model = new InviteBannerModel(() => Now);
        Net.I.Friends.InviteReceived += OnInvite;
        var root = UiTheme.Apply(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
        UiKit.FullRect(root);
        AddChild(root);
        _panel = new PanelContainer { Visible = false };
        _panel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.07f, 0.06f, 0.05f, 0.96f), UiTheme.Gold, 7, 2, 16, 10));
        _panel.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _panel.GrowHorizontal = Control.GrowDirection.Begin;
        _panel.OffsetRight = -18; _panel.OffsetTop = 16;
        root.AddChild(_panel);
        var h = UiKit.HBox(12);
        _panel.AddChild(h);
        _text = UiKit.MakeLabel("", 19, UiTheme.Parchment, HorizontalAlignment.Left, 3);
        _text.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(_text);
        var join = UiKit.MakeButton("Join", Join, 80, true); join.CustomMinimumSize = new Vector2(80, 36);
        var dis = UiKit.MakeButton("Dismiss", () => _model.Dismiss(), 96); dis.CustomMinimumSize = new Vector2(96, 36);
        h.AddChild(join); h.AddChild(dis);
    }

    public override void _ExitTree() { if (Net.I?.Friends != null) Net.I.Friends.InviteReceived -= OnInvite; }

    void OnInvite(InviteInfo i)
    {
        if (!_model.OnInvite(i, Net.I.CurrentLobbyId)) return;
        Util.Log($"STEAM: invite from {i.FromName} to lobby {i.Lobby}");
        Sfx.Play(SfxNames.UiOpen);
    }

    void Join()
    {
        ulong lobby = _model.Join();
        if (lobby == 0) return;
        FriendsOverlay.Close();
        Net.I.JoinSteam(lobby, "");   // leaves a current room first; a password-protected room reports its message in the menu
    }

    public override void _Process(double delta)
    {
        bool show = _model.Active && Game.I.Phase == GamePhase.Lobby;
        _panel.Visible = show;
        if (show) _text.Text = $"{_model.Current.FromName} invited you";
    }
}

/// <summary>Screenshot staging: `--screen=friends | friends_filter | friends_pause | invitebanner` (use with --fakesteam; friends* also --host).</summary>
public partial class FriendsDebug : Node
{
    string _screen; double _t; bool _done;
    public override void _Ready() { _screen = Util.Arg("screen"); ProcessMode = ProcessModeEnum.Always; }
    public override void _Process(double delta)
    {
        _t += delta;
        if (_done) return;
        var g = Game.I;
        switch (_screen)
        {
            case "friends": case "friends_filter":
                if (_t < 0.8) return;
                Net.I.DebugSteamRoom(SteamLobbyCode.Encode(109775241012345678UL), false);
                FriendsOverlay.Open();
                if (_screen == "friends_filter") { FriendsOverlay.I.DebugFilter("o", true); }
                _done = true; break;
            case "friends_pause":
                if (g.Phase != GamePhase.Round || g.LocalMiner == null || _t < 1.5) return;
                Net.I.DebugSteamRoom(SteamLobbyCode.Encode(109775241012345678UL), false);
                PauseMenu.I.Open(true);
                FriendsOverlay.Open();
                _done = true; break;
            case "invitebanner":
                if (_t < 1.0) return;
                if (Net.I.Friends is FakeFriendsSource f) f.RaiseInvite(new InviteInfo { FromId = 1, FromName = "Brynhild", Lobby = 109775241012399999UL });
                _done = true; break;
        }
    }
}
