using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// In-round HUD (v2.2: minimal, quiet). Nothing is on screen that is not needed right now: small dark translucent elements that fade out when nothing about them changed for a few seconds and
/// fade back in on change. Bottom-left thin health / oil / run bars; bottom-centre the two stein and two tool icons (name + one line appear for 3 s on select / pickup / round start);
/// bottom-right the sack icon (bulges with burden) with the owner-only tribute number and a "+10 RUBY" toast; top-centre a digit-free pocket-watch dial, replaced by a burning rope fuse in the last minute;
/// interaction prompts stay in the centre. Key help lives in the Guide and in the hold-Tab overlay. Reads only Game/Net state and events and only ever shows the local player's own private data.
/// </summary>
public partial class Hud : CanvasLayer
{
    public static Hud I;

    Control _root;
    // top: timer
    DialClock _dial; FuseBar _fuse; Control _timerBox;
    Label _bannerA, _bannerB; double _bannerAt = -100;
    // bottom-left: status
    Control _status; ThinBar _hpBar, _oilBar, _staminaBar;
    Control _hpRow, _oilRow, _stamRow;
    // bottom-centre: slots + tip
    Control _slotsBox;
    readonly List<SlotView> _steins = new(), _utils = new();
    VBoxContainer _tipBox; double _tipUntil = -100;
    // bottom-right: sack
    Control _sackBox; SackIcon _sackIcon; Label _tribute, _toast;
    // centre
    Control _cross, _promptBox;
    PanelContainer _promptPanel; Label _promptKey, _promptText; HBoxContainer _promptHBox; VBoxContainer _promptRowsBox; string _promptRowsKey = "";
    Label _downedWho; bool _twitching;
    ThinBar _holdBar;
    VBoxContainer _feed;
    readonly List<(Control node, double born)> _feedNodes = new();
    // overlays
    ColorRect _vignette; ShaderMaterial _vigMat;
    ColorRect _edge; ShaderMaterial _edgeMat;
    DamageRing _ring;
    Control _deathOverlay, _downedOverlay; Label _deathTitle, _deathSub, _downedLabel;
    PanelContainer _scorePanel; VBoxContainer _scoreRows; Label _scoreRemain; double _scoreT;
    Label _notice, _toastHint; double _toastHintUntil = -100;
    ColorRect _tint;
    MapView _map;
    OmniLight3D _owl;

    float _prevHp = 100f, _flash;
    double _timeNow;
    Vector3 _lastKnock; double _lastKnockT = -10;
    public bool ForceScoreboard;
    /// <summary>Screenshot staging: pretend Wicks are drinking the oil.</summary>
    public bool DebugDrain;

    // quiet-fade bookkeeping (time of the last change per group)
    double _chgStatus, _chgSlots, _chgSack;
    float _oilBase = -1f;
    int _prevSack = -1, _prevTribute; int _prevSteinSel = -1, _prevUtilSel = -1;
    List<string> _prevSteins = new(); string[] _prevUtils = new string[0];
    bool _roundTipPending;
    readonly Queue<(double t, float oil)> _oilHist = new();
    bool _draining; double _drainFlashAt;
    string _fuseKey = "hud_fuse"; bool _fuseOn; float _fuseA, _dialA = 1f;
    float _burnPulse;

    class SlotView { public PanelContainer Panel; public ItemIcon Icon; public Label Key; public StyleBoxFlat Style; }

    public override void _Ready()
    {
        I = this;
        Layer = 5;
        GoblinVoice.Ensure(this);
        AddChild(new PauseMenu { Name = "Pause" });
        UiDebug.Ensure(this);

        _root = UiTheme.Apply(new Control { MouseFilter = Control.MouseFilterEnum.Ignore });
        UiKit.FullRect(_root);
        AddChild(_root);

        BuildVignette();
        _ring = new DamageRing(); UiKit.FullRect(_ring); _root.AddChild(_ring);
        BuildTimer();
        BuildCenter();
        BuildStatus();
        BuildSlots();
        BuildSack();
        BuildFeed();
        BuildDeath();
        BuildDowned();
        BuildScoreboard();
        BuildOverlays();

        Miner.Knocked += v => { _lastKnock = v; _lastKnockT = Time.GetTicksMsec() / 1000.0; };
        GoblinVoice.Said += OnSaid;
        Game.I.PhaseChanged += OnPhase;
    }

    public override void _ExitTree() { if (Sfx.IsLooping(_fuseKey)) Sfx.StopLoop(_fuseKey); }

    // ------------------------------------------------------------------ construction
    void BuildVignette()
    {
        _vigMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = @"shader_type canvas_item;
uniform float strength = 0.0;
void fragment(){
  vec2 uv = UV - 0.5;
  float d = length(uv * vec2(1.0, 0.85));
  float v = smoothstep(0.2, 0.72, d);
  COLOR = vec4(0.7, 0.02, 0.02, v * strength);
}"
            }
        };
        _vignette = new ColorRect { Color = Colors.White, Material = _vigMat, MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(_vignette);
        _root.AddChild(_vignette);
        // the last 10 seconds: the screen edges flicker orange, like a flame burning close
        _edgeMat = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = @"shader_type canvas_item;
uniform float strength = 0.0;
uniform float t = 0.0;
void fragment(){
  vec2 uv = UV - 0.5;
  float d = length(uv * vec2(1.0, 0.9));
  float v = smoothstep(0.28, 0.78, d);
  float n = 0.55 + 0.45 * sin(t * 37.0 + uv.x * 9.0) * sin(t * 23.0 - uv.y * 7.0) + 0.25 * sin(t * 61.0);
  COLOR = vec4(1.0, 0.40, 0.07, clamp(v * strength * n, 0.0, 0.8));
}"
            }
        };
        _edge = new ColorRect { Color = Colors.White, Material = _edgeMat, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiKit.FullRect(_edge);
        _root.AddChild(_edge);
    }

    void BuildTimer()
    {
        _timerBox = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _timerBox.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _timerBox.GrowHorizontal = Control.GrowDirection.Both;
        _timerBox.OffsetLeft = -380; _timerBox.OffsetRight = 380; _timerBox.OffsetTop = 6; _timerBox.OffsetBottom = 100;
        _root.AddChild(_timerBox);
        _dial = new DialClock(); _dial.SetAnchorsPreset(Control.LayoutPreset.CenterTop); _dial.GrowHorizontal = Control.GrowDirection.Both; _timerBox.AddChild(_dial);
        _dial.OffsetLeft = -42; _dial.OffsetRight = 42;
        _fuse = new FuseBar(); _fuse.SetAnchorsPreset(Control.LayoutPreset.CenterTop); _fuse.GrowHorizontal = Control.GrowDirection.Both; _timerBox.AddChild(_fuse);
        _fuse.OffsetLeft = -351; _fuse.OffsetRight = 351; _fuse.OffsetTop = 4; _fuse.OffsetBottom = 78; _fuse.Visible = false;
        // two-line round banner (round start, 4 s)
        var bv = UiKit.VBox(0);
        bv.SetAnchorsPreset(Control.LayoutPreset.CenterTop); bv.GrowHorizontal = Control.GrowDirection.Both; bv.OffsetTop = 110;
        _root.AddChild(bv);
        _bannerA = UiKit.MakeLabel("", 26, UiTheme.Gold, HorizontalAlignment.Center, 6); _bannerA.AddThemeFontOverride("font", UiTheme.Chunky);
        _bannerB = UiKit.MakeLabel("", 16, UiTheme.Parchment, HorizontalAlignment.Center, 4);
        bv.AddChild(_bannerA); bv.AddChild(_bannerB);
        _bannerA.Modulate = _bannerB.Modulate = new Color(1, 1, 1, 0);
    }

    void BuildCenter()
    {
        _cross = new Crosshair(); _cross.SetAnchorsPreset(Control.LayoutPreset.Center);
        _cross.GrowHorizontal = Control.GrowDirection.Both; _cross.GrowVertical = Control.GrowDirection.Both;
        _cross.CustomMinimumSize = new Vector2(28, 28);
        _root.AddChild(_cross);

        _promptPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _promptPanel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.02f, 0.02f, 0.02f, 0.5f), null, 5, 0, 10, 4));
        _promptPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _promptPanel.GrowHorizontal = Control.GrowDirection.Both;
        _promptPanel.OffsetTop = 40; _promptPanel.OffsetBottom = 40;
        _promptPanel.Visible = false;
        _root.AddChild(_promptPanel);
        var h = UiKit.HBox(8);
        _promptPanel.AddChild(h);
        _promptHBox = h;
        _promptRowsBox = UiKit.VBox(4);
        _promptPanel.AddChild(_promptRowsBox);
        _promptKey = UiKit.MakeLabel("E", 15, UiTheme.Gold, HorizontalAlignment.Center, 3);
        var cap = new PanelContainer { CustomMinimumSize = new Vector2(26, 26), MouseFilter = Control.MouseFilterEnum.Ignore };
        cap.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.12f, 0.1f, 0.08f, 0.8f), new Color(UiTheme.Gold, 0.8f), 4, 1, 6, 1));
        cap.AddChild(_promptKey);
        h.AddChild(cap);
        _promptText = UiKit.MakeLabel("", 16, UiTheme.Parchment, HorizontalAlignment.Left, 4);
        _promptText.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(_promptText);
        _holdBar = new ThinBar(150, 5) { Fill = UiTheme.Gold, Max = 1, Visible = false };
        _holdBar.SetAnchorsPreset(Control.LayoutPreset.Center);
        _holdBar.GrowHorizontal = Control.GrowDirection.Both;
        _holdBar.OffsetTop = 84; _holdBar.OffsetBottom = 89; _holdBar.OffsetLeft = -75; _holdBar.OffsetRight = 75;
        _root.AddChild(_holdBar);
    }

    Control IconRow(Control parent, Control icon, ThinBar bar, int barW)
    {
        var row = UiKit.HBox(6);
        icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(icon); row.AddChild(bar);
        parent.AddChild(row);
        return row;
    }

    void BuildStatus()
    {
        _status = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _status.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _status.OffsetLeft = 18; _status.OffsetTop = -62; _status.OffsetRight = 250; _status.OffsetBottom = -16;
        _root.AddChild(_status);
        var v = UiKit.VBox(5); UiKit.FullRect(v); _status.AddChild(v);
        _hpBar = new ThinBar(150, 7) { Fill = new Color(0.86f, 0.2f, 0.2f), Max = 100 };
        _hpRow = IconRow(v, new HeartIcon { CustomMinimumSize = new Vector2(15, 14) }, _hpBar, 150);
        _oilBar = new ThinBar(150, 6) { Fill = new Color(1f, 0.66f, 0.18f), Max = 100 };
        _oilRow = IconRow(v, new OilIcon { CustomMinimumSize = new Vector2(15, 14) }, _oilBar, 150);
        _staminaBar = new ThinBar(150, 4) { Fill = new Color(0.55f, 0.8f, 0.45f), Max = 1 };
        _stamRow = IconRow(v, new Control { CustomMinimumSize = new Vector2(15, 4), MouseFilter = Control.MouseFilterEnum.Ignore }, _staminaBar, 150);
    }

    SlotView MakeSlot(HBoxContainer into, bool util)
    {
        var sv = new SlotView();
        var p = new PanelContainer { CustomMinimumSize = new Vector2(34, 34), MouseFilter = Control.MouseFilterEnum.Ignore };
        sv.Style = UiTheme.Box(new Color(0.03f, 0.03f, 0.03f, 0.5f), new Color(0.5f, 0.45f, 0.38f, 0.5f), 5, 1, 3, 3);
        p.AddThemeStyleboxOverride("panel", sv.Style);
        sv.Icon = new ItemIcon { CustomMinimumSize = new Vector2(26, 26) };
        sv.Icon.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; sv.Icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        p.AddChild(sv.Icon);
        sv.Key = UiKit.MakeLabel("", 10, UiTheme.Gold, HorizontalAlignment.Right, 3);
        sv.Key.SetAnchorsPreset(Control.LayoutPreset.BottomRight); sv.Key.GrowHorizontal = Control.GrowDirection.Begin; sv.Key.GrowVertical = Control.GrowDirection.Begin;
        sv.Key.OffsetRight = -2; sv.Key.OffsetBottom = 0;
        p.AddChild(sv.Key);
        sv.Panel = p;
        into.AddChild(p);
        return sv;
    }

    void BuildSlots()
    {
        _slotsBox = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _slotsBox.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _slotsBox.GrowHorizontal = Control.GrowDirection.Both; _slotsBox.GrowVertical = Control.GrowDirection.Begin;
        _slotsBox.OffsetTop = -52; _slotsBox.OffsetBottom = -14;
        _root.AddChild(_slotsBox);
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 5);
        row.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        row.GrowHorizontal = Control.GrowDirection.Both; row.GrowVertical = Control.GrowDirection.Begin;
        _slotsBox.AddChild(row);
        for (int i = 0; i < Inventory.SteinSlots; i++) _steins.Add(MakeSlot(row, false));
        row.AddChild(new Control { CustomMinimumSize = new Vector2(14, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        for (int i = 0; i < Inventory.UtilSlots; i++) _utils.Add(MakeSlot(row, true));

        // the name + one line of the item just selected / picked up (3 s)
        _tipBox = UiKit.VBox(3);
        _tipBox.MouseFilter = Control.MouseFilterEnum.Ignore;
        _tipBox.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _tipBox.GrowHorizontal = Control.GrowDirection.Both; _tipBox.GrowVertical = Control.GrowDirection.Begin;
        _tipBox.OffsetTop = -128; _tipBox.OffsetBottom = -60;
        _tipBox.Modulate = new Color(1, 1, 1, 0);
        _root.AddChild(_tipBox);
    }

    void BuildSack()
    {
        _sackBox = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _sackBox.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _sackBox.GrowHorizontal = Control.GrowDirection.Begin; _sackBox.GrowVertical = Control.GrowDirection.Begin;
        _sackBox.OffsetLeft = -150; _sackBox.OffsetRight = -16; _sackBox.OffsetTop = -74; _sackBox.OffsetBottom = -10;
        _root.AddChild(_sackBox);
        var h = UiKit.HBox(6); h.Alignment = BoxContainer.AlignmentMode.End; UiKit.FullRect(h); _sackBox.AddChild(h);
        _tribute = UiKit.MakeLabel("0", 26, UiTheme.Gold, HorizontalAlignment.Right, 6);
        _tribute.AddThemeFontOverride("font", UiTheme.Chunky);
        _tribute.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(_tribute);
        _sackIcon = new SackIcon { SizeFlagsVertical = Control.SizeFlags.ShrinkEnd };
        h.AddChild(_sackIcon);
        _toast = UiKit.MakeLabel("", 20, UiTheme.Good, HorizontalAlignment.Right, 6);
        _toast.AddThemeFontOverride("font", UiTheme.Chunky);
        _toast.SetAnchorsPreset(Control.LayoutPreset.BottomRight); _toast.GrowHorizontal = Control.GrowDirection.Begin; _toast.GrowVertical = Control.GrowDirection.Begin;
        _toast.OffsetRight = -16; _toast.OffsetBottom = -86; _toast.Modulate = new Color(1, 1, 1, 0);
        _root.AddChild(_toast);
    }

    void BuildOverlays()
    {
        _tint = new ColorRect { Color = new Color(0.4f, 0.6f, 1f, 0.07f), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiKit.FullRect(_tint); _root.AddChild(_tint); _root.MoveChild(_tint, 0);

        // short notices from the item systems, just above the crosshair
        _notice = UiKit.MakeLabel("", 17, new Color(1f, 0.95f, 0.6f), HorizontalAlignment.Center, 5);
        _notice.SetAnchorsPreset(Control.LayoutPreset.Center);
        _notice.GrowHorizontal = Control.GrowDirection.Both; _notice.GrowVertical = Control.GrowDirection.Begin;
        _notice.OffsetTop = -120; _notice.OffsetBottom = -90;
        _notice.AutowrapMode = TextServer.AutowrapMode.WordSmart; _notice.CustomMinimumSize = new Vector2(620, 0);
        _root.AddChild(_notice);
        // one-off hints (e.g. the first time Wicks drink your oil)
        _toastHint = UiKit.MakeLabel("", 18, new Color(1f, 0.82f, 0.5f), HorizontalAlignment.Center, 5);
        _toastHint.SetAnchorsPreset(Control.LayoutPreset.Center);
        _toastHint.GrowHorizontal = Control.GrowDirection.Both; _toastHint.GrowVertical = Control.GrowDirection.Begin;
        _toastHint.OffsetTop = -84; _toastHint.OffsetBottom = -60; _toastHint.Modulate = new Color(1, 1, 1, 0);
        _root.AddChild(_toastHint);

        _map = new MapView { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _map.SetAnchorsPreset(Control.LayoutPreset.Center);
        _map.CustomMinimumSize = new Vector2(420, 420);
        _map.OffsetLeft = -210; _map.OffsetRight = 210; _map.OffsetTop = -210; _map.OffsetBottom = 210;
        _root.AddChild(_map);
    }

    void BuildFeed()
    {
        _feed = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _feed.AddThemeConstantOverride("separation", 4);
        _feed.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _feed.OffsetLeft = 14; _feed.OffsetRight = 320; _feed.OffsetTop = 12;
        _root.AddChild(_feed);
    }

    void BuildDeath()
    {
        _deathOverlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiKit.FullRect(_deathOverlay);
        _root.AddChild(_deathOverlay);
        var tint = new ColorRect { Color = new Color(0.05f, 0.08f, 0.2f, 0.22f), MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(tint); _deathOverlay.AddChild(tint);
        var box = UiKit.VBox(4);
        box.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        box.GrowHorizontal = Control.GrowDirection.Both;
        box.OffsetTop = 130;
        _deathOverlay.AddChild(box);
        _deathTitle = UiKit.MakeTitle("YOU DIED", 48, UiTheme.Danger);
        box.AddChild(_deathTitle);
        _deathSub = UiKit.MakeLabel("You will return next round.", 20, new Color(0.8f, 0.86f, 1f), HorizontalAlignment.Center, 5);
        box.AddChild(_deathSub);
        box.AddChild(UiKit.MakeLabel("Ghost: WASD / Space / Ctrl fly  -  RMB snuff a lantern  -  E knock a pebble  -  your sack lies where you died", 14, UiTheme.Dim, HorizontalAlignment.Center, 4));
    }

    void BuildDowned()
    {
        _downedOverlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiKit.FullRect(_downedOverlay);
        _root.AddChild(_downedOverlay);
        var tint = new ColorRect { Color = new Color(0.35f, 0.02f, 0.02f, 0.2f), MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(tint); _downedOverlay.AddChild(tint);
        var box = UiKit.VBox(2);
        box.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        box.GrowHorizontal = Control.GrowDirection.Both;
        box.OffsetTop = 130;
        _downedOverlay.AddChild(box);
        box.AddChild(UiKit.MakeTitle("YOU ARE DOWN", 40, UiTheme.Danger));
        _downedLabel = UiKit.MakeLabel("Bleeding out in 30 s", 20, new Color(1f, 0.8f, 0.75f), HorizontalAlignment.Center, 5);
        box.AddChild(_downedLabel);
        _downedWho = UiKit.MakeLabel("", 20, new Color(1f, 0.85f, 0.5f), HorizontalAlignment.Center, 5);
        box.AddChild(_downedWho);
        box.AddChild(UiKit.MakeLabel("You can only crawl. Anyone may help you up, rob you or drag you. Any hit finishes you.", 14, UiTheme.Dim, HorizontalAlignment.Center, 4));
    }

    static readonly (string key, string what)[] Controls =
    {
        ("W A S D", "move"), ("Shift", "run (tires you)"), ("Ctrl", "crouch"), ("Space", "jump"),
        ("E", "take / open / use"), ("LMB", "mine / strike"), ("RMB", "use the tool in hand"), ("1  2", "choose tool"),
        ("L", "lantern on / off"), ("F", "choose stein slot"), ("Q", "stein power"), ("G", "drop tool  (hold: stuff stein into sack)"),
        ("R", "shove (back: spills)"), ("Crouch+hold E", "behind: pick sack"),
        ("hold E", "help up downed"), ("C", "rob a downed miner"), ("hold RMB", "drag a downed miner"), ("", ""),
        ("Tab", "this screen (hold)"), ("Esc", "menu, guide, settings"),
    };

    void BuildScoreboard()
    {
        _scorePanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _scorePanel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.03f, 0.03f, 0.03f, 0.82f), new Color(UiTheme.BrassDark, 0.7f), 8, 1, 22, 16));
        _scorePanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _scorePanel.GrowHorizontal = Control.GrowDirection.Both; _scorePanel.GrowVertical = Control.GrowDirection.Both;
        _scorePanel.CustomMinimumSize = new Vector2(620, 0);
        _root.AddChild(_scorePanel);
        var v = UiKit.VBox(8);
        _scorePanel.AddChild(v);
        v.AddChild(UiKit.MakeTitle("MINERS", 26));
        _scoreRemain = UiKit.MakeLabel("", 16, UiTheme.Gold, HorizontalAlignment.Center, 4);
        v.AddChild(_scoreRemain);
        v.AddChild(UiKit.Rule());
        _scoreRows = UiKit.VBox(4);
        v.AddChild(_scoreRows);
        v.AddChild(UiKit.Rule());
        v.AddChild(UiKit.MakeLabel("CONTROLS", 14, UiTheme.Brass, HorizontalAlignment.Center, 3));
        var grid = new GridContainer { Columns = 4, MouseFilter = Control.MouseFilterEnum.Ignore };
        grid.AddThemeConstantOverride("h_separation", 12); grid.AddThemeConstantOverride("v_separation", 3);
        foreach (var (k, w) in Controls)
        {
            var kl = UiKit.MakeLabel(k, 14, UiTheme.Gold, HorizontalAlignment.Right, 3); kl.CustomMinimumSize = new Vector2(64, 0);
            var wl = UiKit.MakeLabel(w, 14, UiTheme.Parchment, HorizontalAlignment.Left, 3); wl.CustomMinimumSize = new Vector2(190, 0);
            grid.AddChild(kl); grid.AddChild(wl);
        }
        v.AddChild(grid);
        v.AddChild(UiKit.MakeLabel("Nobody's tribute is public (only the size of a sack shows). Until the weighing.", 13, UiTheme.Dim, HorizontalAlignment.Center, 3));
    }

    // ------------------------------------------------------------------ events
    void OnPhase()
    {
        if (Game.I.Phase == GamePhase.Round)
        {
            _prevHp = Game.I.OwnHealth; _hpBar.Target = Game.I.OwnHealth; _hpBar.Snap();
            _prevTribute = Game.I.OwnTribute; _prevSack = -1; _prevSteinSel = _prevUtilSel = -1; _oilBase = -1f;
            _oilHist.Clear();
            _bannerAt = Time.GetTicksMsec() / 1000.0;
            _roundTipPending = true;
            _chgStatus = _chgSlots = _chgSack = _bannerAt;
        }
        if (Game.I.Phase != GamePhase.Round) { ClearFeed(); if (Sfx.IsLooping(_fuseKey)) Sfx.StopLoop(_fuseKey); _fuseOn = false; }
    }

    void ClearFeed()
    {
        foreach (var (n, _) in _feedNodes) n.QueueFree();
        _feedNodes.Clear();
    }

    /// <summary>The Goblin's lines: a small portrait bubble at the top left that fades by itself.</summary>
    void OnSaid(GoblinSaying s)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        p.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.03f, 0.03f, 0.03f, 0.55f), new Color(s.Mood == "angry" ? UiTheme.Danger : UiTheme.BrassDark, 0.6f), 7, 1, 7, 5));
        var h = UiKit.HBox(8);
        p.AddChild(h);
        var por = new GoblinPortrait { Mood = s.Mood, CustomMinimumSize = new Vector2(38, 38) };
        por.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        h.AddChild(por);
        var l = UiKit.MakeLabel(s.Text, 14, UiTheme.Parchment, HorizontalAlignment.Left, 3);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(236, 0);
        h.AddChild(l);
        _feed.AddChild(p);
        _feedNodes.Add((p, Time.GetTicksMsec() / 1000.0));
        while (_feedNodes.Count > 2) { _feedNodes[0].node.QueueFree(); _feedNodes.RemoveAt(0); }
        var tw = CreateTween();
        tw.TweenProperty(p, "modulate:a", 1f, 0.25f);
    }

    /// <summary>A one-line hint in the middle of the screen (4 s).</summary>
    public void ShowHint(string text, double seconds = 5)
    {
        _toastHint.Text = text; _toastHintUntil = Time.GetTicksMsec() / 1000.0 + seconds;
    }

    // ------------------------------------------------------------------ per frame
    static float Fade(double since, float hold, float fade, float min) => Mathf.Clamp(1f - (float)(since - hold) / fade, min, 1f);

    public override void _Process(double delta)
    {
        var g = Game.I;
        float dt = (float)delta;
        _timeNow = Time.GetTicksMsec() / 1000.0;
        bool show = g.Phase == GamePhase.Round;
        _root.Visible = show;
        if (!show) return;

        bool alive = g.LocalMiner != null;
        bool ghost = !alive && g.LocalGhost != null;

        UpdateTimer(g, dt);
        UpdateStatus(g, dt, alive, ghost);
        UpdateSlots(g, alive);
        UpdateSack(g, alive, ghost);
        UpdateItemOverlays(g, alive);

        // prompt (interaction prompts stay in the centre; near your own cart a quiet reminder)
        string prompt = alive && !PauseMenu.I.IsOpen ? g.LocalMiner.PromptText : "";
        if (prompt == "" && alive && !PauseMenu.I.IsOpen && HallCeremony.I != null && HallCeremony.I.HasCart(Net.I.MyId))
        {
            var cart = HallCeremony.I.CartPos(Net.I.MyId); var me = g.LocalMiner.GlobalPosition;
            if (new Vector2(cart.X - me.X, cart.Z - me.Z).Length() < 2.6f && g.Phase == GamePhase.Round) prompt = "Your cart - be here when the fuse burns out";
        }
        var rows = alive && !PauseMenu.I.IsOpen ? g.LocalMiner.PromptRows : System.Array.Empty<string>();
        _promptPanel.Visible = prompt != "" || rows.Length > 0;
        _promptHBox.Visible = rows.Length == 0; _promptRowsBox.Visible = rows.Length > 0;
        _cross.Visible = alive && !PauseMenu.I.IsOpen;
        if (rows.Length > 0) UpdatePromptRows(rows);
        else if (prompt != "")
        {
            if (prompt.StartsWith("E ")) { _promptKey.Text = "E"; _promptText.Text = prompt.Substring(1).Trim(); _promptKey.GetParent<Control>().Visible = true; _promptText.Modulate = Colors.White; }
            else { _promptKey.GetParent<Control>().Visible = false; _promptText.Text = prompt; _promptText.Modulate = new Color(1, 1, 1, 0.8f); }
        }

        bool downed = alive && g.OwnDowned;
        _downedOverlay.Visible = downed;
        if (downed)
        {
            double left = System.Math.Max(0, g.OwnDownedUntil - _timeNow);
            _downedLabel.Text = $"Bleeding out in {Mathf.CeilToInt((float)left)} s";
            _downedWho.Text = DownedActorsText(g);
        }
        UpdateOtherMinersHints(g, alive);
        _holdBar.Visible = alive && g.LocalMiner.HoldProgress > 0f;
        if (_holdBar.Visible) { _holdBar.Target = g.LocalMiner.HoldProgress; _holdBar.Snap(); }
        _deathOverlay.Visible = ghost;
        if (ghost)
        {
            bool elim = g.Status.GetValueOrDefault(Net.I.MyId) == MinerStatus.Eliminated;
            _deathTitle.Text = elim ? "THROWN" : "YOU DIED";
            _deathTitle.AddThemeColorOverride("font_color", elim ? UiTheme.Gold : UiTheme.Danger);
            _deathSub.Text = elim ? "You haunt the mine for the rest of the match." : "You will return next round.";
        }

        // goblin bubble fade-out
        for (int i = _feedNodes.Count - 1; i >= 0; i--)
        {
            double age = _timeNow - _feedNodes[i].born;
            if (age > 7) { _feedNodes[i].node.QueueFree(); _feedNodes.RemoveAt(i); }
            else if (age > 5.5) _feedNodes[i].node.Modulate = new Color(1, 1, 1, (float)(7 - age) / 1.5f);
        }
        // round banner (2 lines, 4 s) and the one-off hint
        float ba = Mathf.Clamp((float)(4.0 - (_timeNow - _bannerAt)) / 1.2f, 0f, 1f) * Mathf.Clamp((float)(_timeNow - _bannerAt) / 0.3f, 0f, 1f);
        _bannerA.Text = $"ROUND {g.Round} / {g.TotalRounds}";
        _bannerB.Text = g.Remaining == 1 ? "1 miner remains" : $"{g.Remaining} miners remain";
        _bannerA.Modulate = _bannerB.Modulate = new Color(1, 1, 1, ba);
        _toastHint.Modulate = new Color(1, 1, 1, Mathf.Clamp((float)(_toastHintUntil - _timeNow) / 0.8f, 0f, 1f));

        // hold-Tab overlay (miners, miners remaining, key help)
        bool score = (Input.IsActionPressed("scoreboard") || ForceScoreboard) && !PauseMenu.I.IsOpen;
        _scorePanel.Visible = score;
        if (score && _timeNow - _scoreT > 0.2 && !VoiceHud.CursorOver(_scorePanel)) { _scoreT = _timeNow; RebuildScoreboard(g); }
    }

    // ---- timer: dial (digit-free), then the burning fuse in the last minute
    void UpdateTimer(Game g, float dt)
    {
        float frac = g.RoundTotal > 1f ? Mathf.Clamp(g.TimeLeft / g.RoundTotal, 0f, 1f) : 1f;
        bool fuseMode = g.TimeLeft <= 60f;
        _dial.Frac = frac; _dial.Round = g.Round; _dial.TotalRounds = g.TotalRounds;
        _fuseA = Mathf.MoveToward(_fuseA, fuseMode ? 1f : 0f, dt * 2f);
        _dialA = Mathf.MoveToward(_dialA, fuseMode ? 0f : 1f, dt * 2f);
        _dial.Visible = _dialA > 0.01f; _dial.Modulate = new Color(1, 1, 1, 0.9f * _dialA);
        _fuse.Visible = _fuseA > 0.01f; _fuse.Modulate = new Color(1, 1, 1, _fuseA);
        float left = Mathf.Max(0f, g.TimeLeft);
        _fuse.Frac = Mathf.Clamp(left / 60f, 0f, 1f);
        float urgent = fuseMode && left > 0f ? Mathf.Clamp(1f - left / 10f, 0f, 1f) : 0f;
        _fuse.Urgent = urgent;
        _burnPulse += dt;
        // screen edge flicker in the last 10 seconds
        _edge.Visible = urgent > 0f;
        if (urgent > 0f) { _edgeMat.SetShaderParameter("strength", 0.25f + 0.55f * urgent); _edgeMat.SetShaderParameter("t", _burnPulse); }
        // the hiss: steady in the last minute, rising in pitch and volume in the last 10 s
        if (fuseMode && left > 0f && g.LocalMiner != null || fuseMode && left > 0f && g.LocalGhost != null)
        {
            if (!Sfx.IsLooping(_fuseKey)) Sfx.Loop(_fuseKey, SfxNames.RoundFuse, null, -2f);
            Sfx.TuneLoop(_fuseKey, 0.85f + 0.9f * urgent * urgent + 0.1f * (1f - left / 60f), -9f + 11f * urgent + 3f * (1f - left / 60f));
            _fuseOn = true;
        }
        else if (_fuseOn || Sfx.IsLooping(_fuseKey)) { Sfx.StopLoop(_fuseKey); _fuseOn = false; }
    }

    // ---- bottom-left: thin health / oil / run bars that fade when nothing changes
    void UpdateStatus(Game g, float dt, bool alive, bool ghost)
    {
        float hp = g.OwnHealth;
        _hpBar.Target = alive ? hp : 0f;
        if (alive && hp < _prevHp - 0.4f) { OnDamaged(_prevHp - hp); _chgStatus = _timeNow; }
        else if (alive && hp > _prevHp + 0.4f) _chgStatus = _timeNow;
        _prevHp = hp;
        bool lowHp = alive && hp <= 30f;
        _hpBar.Pulsing = lowHp; _hpBar.Text = lowHp ? Mathf.CeilToInt(hp).ToString() : "";
        _flash = Mathf.Max(0f, _flash - dt * 1.8f);
        float lowV = alive ? Mathf.Clamp((45f - hp) / 45f, 0f, 1f) : 0f;
        float beat = lowV > 0 ? 0.65f + 0.35f * Mathf.Abs(Mathf.Sin((float)_timeNow * (2f + 3f * lowV))) : 0f;
        _vigMat.SetShaderParameter("strength", Mathf.Clamp(lowV * 0.95f * beat + _flash * 0.8f, 0f, 1f));

        // oil: private level; the bar wakes on a 3% change, when low, and flashes while Wicks drink it
        float oilMax = Mathf.Max(1f, g.OwnOilMax);
        bool hasLantern = System.Array.IndexOf(g.OwnInv.Utils, "lantern") >= 0;
        _oilBar.Max = oilMax; _oilBar.Target = g.OwnOil;
        bool lowOil = hasLantern && g.OwnOil <= oilMax * ItemsServer.OilLowFraction;
        _oilBar.Fill = lowOil ? UiTheme.Danger : new Color(1f, 0.66f, 0.18f);
        _oilBar.Pulsing = alive && lowOil && g.OwnOil > 0f;
        _oilBar.Text = alive && hasLantern && lowOil ? (g.OwnOil <= 0f ? "OUT" : $"{Mathf.CeilToInt(g.OwnOil / oilMax * 100f)}%") : "";
        if (_oilBase < 0 || Mathf.Abs(g.OwnOil - _oilBase) > oilMax * 0.03f) { if (_oilBase >= 0) _chgStatus = _timeNow; _oilBase = g.OwnOil; }
        // Wicks drinking the oil: it drops many times faster than it burns
        _oilHist.Enqueue((_timeNow, g.OwnOil));
        while (_oilHist.Count > 1 && _timeNow - _oilHist.Peek().t > 0.7) _oilHist.Dequeue();
        bool fastDrop = _oilHist.Count > 1 && _timeNow - _oilHist.Peek().t > 0.3 && _oilHist.Peek().oil - g.OwnOil > 7f;
        _draining = alive && (DebugDrain || (fastDrop && WicksNear(g, 9f)));
        if (_draining)
        {
            _chgStatus = _timeNow;
            if (_timeNow - _drainFlashAt > 0.35) { _drainFlashAt = _timeNow; _oilBar.Flash(); }
            if (!UserSettings.SeenWicksHint) { UserSettings.SeenWicksHint = true; UserSettings.Save(); ShowHint("Wicks drink lamp oil. Put the light out (L)."); }
        }
        // run bar: only while it is not full
        float st = g.LocalMiner != null ? g.LocalMiner.Stamina : 1f;
        _staminaBar.Target = st; _staminaBar.Fill = st < 0.2f ? UiTheme.Danger : new Color(0.55f, 0.8f, 0.45f);
        bool stamShown = alive && st < 0.995f;
        if (stamShown) _chgStatus = _timeNow;
        _stamRow.Visible = stamShown;
        _oilRow.Visible = alive && hasLantern;
        _hpRow.Visible = alive;
        bool always = lowHp || (alive && lowOil && g.OwnOil > 0f);
        float a = always ? 1f : Fade(_timeNow - _chgStatus, 3.5f, 1.2f, 0f);
        _status.Visible = alive; _status.Modulate = new Color(1, 1, 1, a);
    }

    bool WicksNear(Game g, float r)
    {
        var me = g.LocalMiner; if (me == null || g.World == null) return false;
        foreach (var o in g.World.Objects.Values) if (o is WicksObj w && w.GlobalPosition.DistanceTo(me.GlobalPosition) < r) return true;
        return false;
    }

    // ---- bottom-right: the sack
    void UpdateSack(Game g, bool alive, bool ghost)
    {
        var sack = g.OwnInv.Sack;
        float burden = g.OwnBurden; float spd = Burden.SpeedMul(burden);
        _sackIcon.Burden = burden; _sackIcon.Speed = spd; _sackIcon.Stones = sack.Count;
        _sackIcon.QueueRedraw();
        int tribute = g.OwnTribute;
        _tribute.Text = tribute.ToString();
        _tribute.AddThemeColorOverride("font_color", spd > 0.6f ? UiTheme.Gold : new Color(1f, 0.6f, 0.4f));
        if (_prevSack >= 0 && (sack.Count != _prevSack || tribute != _prevTribute))
        {
            _chgSack = _timeNow;
            int d = tribute - _prevTribute;
            if (sack.Count > _prevSack)
            {
                string last = sack[^1]; var od = Ores.Get(last);
                string nm = sack.Count - _prevSack == 1 && od != null ? od.Name.ToUpper() : "STONES";
                PopToast(d > 0 ? $"+{d} {nm}" : $"+{nm}", true);
            }
            else if (sack.Count < _prevSack && _timeNow - g.StolenAt < 1.5 && g.StolenOre != "")
            {
                var sd = Ores.Get(g.StolenOre);
                PopToast($"\u2212{sd?.TributeWeight ?? -d} {(sd?.Name ?? "STONE").ToUpper()}", false);   // "−10 RUBY" in red; the sack icon twitches
                g.StolenAt = -100;
                Twitch();
                if (g.ShovesFelt > 0 && !UserSettings.SeenSpillHint)   // hint 2 of 3: the first time a shove knocks a stone out of your sack
                { UserSettings.SeenSpillHint = true; UserSettings.Save(); ShowHint("Shoved from behind, a stone spills out of your sack. Watch your back."); }
            }
            else if (d != 0) PopToast((d > 0 ? "+" : "") + d, d > 0);
        }
        else if (_prevSack >= 0 && tribute != _prevTribute) { _chgSack = _timeNow; PopToast((tribute - _prevTribute > 0 ? "+" : "") + (tribute - _prevTribute), tribute > _prevTribute); }
        _prevSack = sack.Count; _prevTribute = tribute;
        bool slow = spd < 0.85f;
        float a = slow ? Mathf.Max(0.55f, Fade(_timeNow - _chgSack, 4f, 1.5f, 0.35f)) : Fade(_timeNow - _chgSack, 4f, 1.5f, 0.32f);
        _sackBox.Visible = alive; _sackBox.Modulate = new Color(1, 1, 1, a);
    }

    void Twitch()
    {
        if (_twitching) return;
        _twitching = true;
        var tw = CreateTween();
        var baseOff = _sackBox.Position;
        for (int i = 0; i < 4; i++) { tw.TweenProperty(_sackBox, "position", baseOff + new Vector2(i % 2 == 0 ? 7f : -7f, i % 2 == 0 ? -4f : 3f), 0.045f); }
        tw.TweenProperty(_sackBox, "position", baseOff, 0.06f);
        tw.TweenCallback(Callable.From(() => _twitching = false));
        _chgSack = _timeNow;
    }

    /// <summary>Rows "KEY\ttext" in the centre prompt (a downed miner under the crosshair: help up / rob / drag).</summary>
    void UpdatePromptRows(string[] rows)
    {
        string key = string.Join("|", rows);
        if (key == _promptRowsKey) return;
        _promptRowsKey = key;
        foreach (var c in _promptRowsBox.GetChildren()) c.QueueFree();
        for (int i = 0; i < rows.Length; i++)
        {
            var parts = rows[i].Split('\t');
            var row = UiKit.HBox(8);
            var cap = new PanelContainer { CustomMinimumSize = new Vector2(26, 26), MouseFilter = Control.MouseFilterEnum.Ignore };
            cap.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.12f, 0.1f, 0.08f, 0.8f), new Color(UiTheme.Gold, 0.8f), 4, 1, 6, 1));
            cap.AddChild(UiKit.MakeLabel(parts[0], 15, UiTheme.Gold, HorizontalAlignment.Center, 3));
            row.AddChild(cap);
            var t = UiKit.MakeLabel(parts.Length > 1 ? parts[1] : "", 16, i == 2 ? new Color(1, 1, 1, 0.7f) : UiTheme.Parchment, HorizontalAlignment.Left, 4);
            t.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(t);
            _promptRowsBox.AddChild(row);
        }
    }

    /// <summary>"Anna is helping you up" / "Ben is robbing you" in the downed overlay.</summary>
    string DownedActorsText(Game g)
    {
        var lines = new List<string>();
        foreach (var kv in g.DownedActors)
        {
            if (kv.Value.until < _timeNow) continue;
            string n = Net.I.NameOf(kv.Key);
            lines.Add(kv.Value.mode switch { 1 => $"{n} is helping you up", 2 => $"{n} is robbing you", _ => $"{n} is dragging you" });
        }
        return string.Join("\n", lines);
    }

    /// <summary>At most three one-time hints for the other-miner actions (like the Wicks hint).</summary>
    void UpdateOtherMinersHints(Game g, bool alive)
    {
        var m = g.LocalMiner; if (!alive || m == null) return;
        if (!UserSettings.SeenPickHint && m.PickReachable && !m.IsCrouching && !m.IsDowned)
        { UserSettings.SeenPickHint = true; UserSettings.Save(); ShowHint("Crouch (Ctrl), then hold E to pick their sack. R shoves."); }
        else if (!UserSettings.SeenDownedHint && m.DownedTarget != null)
        { UserSettings.SeenDownedHint = true; UserSettings.Save(); ShowHint("Hold E helps them up, C robs them, hold right mouse drags them."); }
    }

    void PopToast(string text, bool good)
    {
        _toast.Text = text;
        _toast.AddThemeColorOverride("font_color", good ? UiTheme.Good : UiTheme.Danger);
        _toast.OffsetBottom = -80; _toast.Modulate = Colors.White;
        var tw = CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(_toast, "offset_bottom", -108f, 1.3f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(_toast, "modulate:a", 0f, 0.5f).SetDelay(1.0f);
    }

    void OnDamaged(float amount)
    {
        _flash = Mathf.Clamp(0.35f + amount / 40f, 0.35f, 1f);
        _hpBar.Flash();
        Sfx.Play(SfxNames.Hurt);
        var g = Game.I;
        if (g.LocalMiner == null) return;
        Vector3? dir = null;
        if (_timeNow - _lastKnockT < 0.7 && (_lastKnock with { Y = 0 }).LengthSquared() > 0.01f) dir = -(_lastKnock with { Y = 0 }).Normalized();
        else if (g.World?.MinersRoot != null)
        {
            float best = 8f;
            foreach (var m in g.World.Miners())
            {
                if (m == g.LocalMiner) continue;
                float d = m.GlobalPosition.DistanceTo(g.LocalMiner.GlobalPosition);
                if (d < best) { best = d; dir = (m.GlobalPosition - g.LocalMiner.GlobalPosition) with { Y = 0 }; }
            }
            if (dir.HasValue && dir.Value.LengthSquared() > 0.01f) dir = dir.Value.Normalized(); else dir = null;
        }
        if (dir.HasValue) _ring.Add(dir.Value);
    }

    // ------------------------------------------------------------------ slots: two steins, two tools, tiny; name + one line for 3 s when selected / picked up / round start
    void UpdateSlots(Game g, bool alive)
    {
        _slotsBox.Visible = alive;
        if (!alive) { _tipBox.Modulate = new Color(1, 1, 1, 0); return; }
        var inv = g.OwnInv;
        double now = _timeNow;
        // change detection -> tip + wake
        bool changed = false; var tips = new List<(string kind, string id)>();
        if (_prevUtilSel >= 0)
        {
            if (inv.Selected != _prevUtilSel) { changed = true; var id = inv.Held; if (id != "") tips.Add(("util", id)); }
            else if (!inv.Utils.SequenceEqual(_prevUtils)) { changed = true; for (int i = 0; i < inv.Utils.Length; i++) if ((inv.Utils[i] ?? "") != "" && (i >= _prevUtils.Length || _prevUtils[i] != inv.Utils[i])) tips.Add(("util", inv.Utils[i])); }
            var added = inv.Steins.Where(s => !_prevSteins.Contains(s)).ToList();
            if (added.Count > 0) { changed = true; foreach (var s in added) tips.Add(("stein", s)); }
            else if (inv.SteinSel != _prevSteinSel && inv.Steins.Count > 1 && inv.SteinSel < inv.Steins.Count) { changed = true; tips.Add(("stein", inv.Steins[inv.SteinSel])); }
            else if (!inv.Steins.SequenceEqual(_prevSteins)) changed = true;
        }
        if (_roundTipPending && alive && !g.OwnDowned)
        {
            _roundTipPending = false; changed = true; tips.Clear();
            if (inv.Steins.Count > 0) tips.Add(("stein", inv.Steins[Mathf.Clamp(inv.SteinSel, 0, inv.Steins.Count - 1)]));
            if (inv.Held != "") tips.Add(("util", inv.Held));
        }
        _prevUtilSel = inv.Selected; _prevSteinSel = inv.SteinSel; _prevSteins = new List<string>(inv.Steins); _prevUtils = (string[])inv.Utils.Clone();
        if (changed) _chgSlots = now;
        if (tips.Count > 0) ShowTips(tips.Take(2).ToList());

        for (int i = 0; i < _steins.Count; i++)
        {
            string id = i < inv.Steins.Count ? inv.Steins[i] : "";
            var d = id == "" ? null : Steins.Get(id);
            Color border = d == null ? new Color(0.5f, 0.45f, 0.38f, 0.3f) : d.Rarity == Rarity.Legendary ? new Color(1f, 0.55f, 0.15f) : d.Rarity == Rarity.Rare ? new Color(0.35f, 0.65f, 1f) : new Color(0.75f, 0.7f, 0.6f, 0.85f);
            bool sel = inv.SteinSel == i && inv.Steins.Count > 1;
            string key = ""; Color keyCol = UiTheme.Gold;
            if (d != null && d.Ability != null && d.Ability == g.OwnAbility)
            {
                double cd = g.OwnAbilityReadyAt - now, act = g.OwnEtherealUntil - now;
                key = act > 0 ? $"{act:0}" : cd > 0 ? $"{Mathf.CeilToInt((float)cd)}" : "Q";
                keyCol = act > 0 ? UiTheme.Good : cd > 0 ? UiTheme.Dim : UiTheme.Gold;
                if (act > 0 || cd <= 0 != _abilityReadyPrev) { _chgSlots = now; }
                _abilityReadyPrev = cd <= 0;
            }
            FillSlot(_steins[i], "stein", id, border, sel, key, keyCol);
        }
        for (int i = 0; i < _utils.Count; i++)
        {
            string id = i < inv.Utils.Length ? inv.Utils[i] ?? "" : "";
            bool sel = inv.Selected == i;
            FillSlot(_utils[i], "util", id, sel ? UiTheme.Gold : new Color(0.5f, 0.45f, 0.38f, 0.45f), sel, (i + 1).ToString(), sel ? UiTheme.Gold : UiTheme.Dim);
        }
        _slotsBox.Modulate = new Color(1, 1, 1, Fade(now - _chgSlots, 4f, 1.5f, 0.3f));
        _tipBox.Modulate = new Color(1, 1, 1, Mathf.Clamp((float)(_tipUntil - now) / 0.7f, 0f, 1f));
    }
    bool _abilityReadyPrev;

    void ShowTips(List<(string kind, string id)> tips)
    {
        foreach (var c in _tipBox.GetChildren()) c.QueueFree();
        foreach (var (kind, id) in tips)
        {
            string name, desc; Color col;
            if (kind == "stein") { var d = Steins.Get(id); if (d == null) continue; name = d.Name; desc = d.Description; col = ItemInfo.RarityColor(d.Rarity); }
            else { var d = Utilities.Get(id); if (d == null) continue; name = d.Name; desc = d.Description; col = UiTheme.Parchment; }
            int dot = desc.IndexOf(". "); if (dot > 0 && desc.Length > 90) desc = desc.Substring(0, dot + 1);   // one line
            var nl = UiKit.MakeLabel(name, 15, col, HorizontalAlignment.Center, 4);
            var dl = UiKit.MakeLabel(desc, 12, new Color(UiTheme.Parchment, 0.85f), HorizontalAlignment.Center, 3);
            dl.AutowrapMode = TextServer.AutowrapMode.WordSmart; dl.CustomMinimumSize = new Vector2(420, 0);
            _tipBox.AddChild(nl); _tipBox.AddChild(dl);
        }
        _tipUntil = _timeNow + 3.2;
    }

    void FillSlot(SlotView s, string kind, string id, Color border, bool selected, string key, Color keyCol)
    {
        s.Icon.Set(kind, id);
        s.Icon.Modulate = id == "" ? new Color(1, 1, 1, 0) : Colors.White;
        s.Style.BorderColor = border;
        s.Style.SetBorderWidthAll(selected ? 2 : 1);
        s.Style.BgColor = selected ? new Color(0.2f, 0.15f, 0.05f, 0.7f) : new Color(0.03f, 0.03f, 0.03f, id == "" ? 0.3f : 0.5f);
        s.Key.Text = id == "" ? "" : key;
        s.Key.AddThemeColorOverride("font_color", keyCol);
    }

    void UpdateItemOverlays(Game g, bool alive)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        var inv = g.OwnInv;
        _notice.Text = alive && !g.OwnDowned && now - g.ItemNoticeAt < 4 + g.ItemNotice.Length / 25.0 ? g.ItemNotice : "";
        _tint.Visible = false;   // night vision is only a faint light on the miner, never a screen-wide wash
        _map.Visible = alive && inv.Held == "map" && Input.IsActionPressed("use_utility");

        // Owl stein: faint light that only exists on the local miner (never replicated).
        var m = g.LocalMiner;
        if (alive && g.OwnDark)
        {
            if (_owl == null || !IsInstanceValid(_owl) || _owl.GetParent() != m)
            {
                _owl = new OmniLight3D { LightColor = new Color(0.45f, 0.62f, 1f), LightEnergy = 0.4f, OmniRange = 24f, OmniAttenuation = 1.4f, ShadowEnabled = false, Position = new Vector3(0, 1.6f, 0) };
                m.AddChild(_owl);
            }
            _owl.LightEnergy = g.World.Cave.InHall(m.GlobalPosition) ? 0f : 0.4f;   // lit areas (the camp) stay exactly as they are
        }
        else if (_owl != null) { if (IsInstanceValid(_owl)) _owl.QueueFree(); _owl = null; }
    }

    // ------------------------------------------------------------------ hold-Tab overlay (names and status only, never any weights)
    void RebuildScoreboard(Game g)
    {
        _scoreRemain.Text = g.Remaining == 1 ? "1 miner remains" : $"{g.Remaining} miners remain";
        foreach (var c in _scoreRows.GetChildren()) c.QueueFree();
        foreach (var kv in Net.I.Names)
        {
            bool me = kv.Key == Net.I.MyId;
            var status = g.Status.GetValueOrDefault(kv.Key);
            var row = UiKit.HBox(12);
            row.CustomMinimumSize = new Vector2(0, 30);
            var hi = new HelmetIcon(PlayerColors.Of(kv.Key), 28) { Dead = status != MinerStatus.Alive };
            hi.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(hi);
            var name = UiKit.MakeLabel(kv.Value + (me ? "  (you)" : "") + (kv.Key == 1 ? "  [host]" : ""), 19, me ? UiTheme.Gold : (status == MinerStatus.Eliminated ? UiTheme.Dim : UiTheme.Parchment), HorizontalAlignment.Left, 4);
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(name);
            string stText = status switch { MinerStatus.Alive => "ALIVE", MinerStatus.Downed => "DOWNED", MinerStatus.Dead => "DEAD", _ => "ELIMINATED" };
            var stCol = status switch { MinerStatus.Alive => UiTheme.Good, MinerStatus.Downed => new Color(1f, 0.6f, 0.2f), MinerStatus.Dead => UiTheme.Danger, _ => UiTheme.Dim };
            var st = UiKit.MakeLabel(stText, 15, stCol, HorizontalAlignment.Center, 3);
            st.CustomMinimumSize = new Vector2(110, 0); st.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(st);
            VoiceUi.ScoreExtras(row, kv.Key);
            _scoreRows.AddChild(row);
        }
    }
}

// ---------------------------------------------------------------------- small HUD pieces
public partial class Crosshair : Control
{
    public Crosshair() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        var c = Size / 2f;
        DrawCircle(c, 3f, new Color(0, 0, 0, 0.6f));
        DrawCircle(c, 2f, new Color(1f, 0.95f, 0.8f, 0.95f));
        foreach (var d in new[] { Vector2.Up, Vector2.Down, Vector2.Left, Vector2.Right })
            DrawLine(c + d * 7f, c + d * 11f, new Color(1f, 0.95f, 0.8f, 0.7f), 2f);
    }
}

public partial class HeartIcon : Control
{
    public HeartIcon() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(30, 28); }
    public override void _Draw()
    {
        var c = new Color(0.9f, 0.2f, 0.22f);
        float w = Size.X, h = Size.Y;
        DrawCircle(new Vector2(w * 0.30f, h * 0.35f), w * 0.24f, c);
        DrawCircle(new Vector2(w * 0.70f, h * 0.35f), w * 0.24f, c);
        DrawColoredPolygon(new[] { new Vector2(w * 0.07f, h * 0.45f), new Vector2(w * 0.93f, h * 0.45f), new Vector2(w * 0.5f, h * 0.95f) }, c);
        DrawCircle(new Vector2(w * 0.26f, h * 0.28f), w * 0.07f, new Color(1, 1, 1, 0.55f));
    }
}

public partial class StoneIcon : Control
{
    public StoneIcon() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(30, 34); }
    public override void _Draw()
    {
        float w = Size.X, h = Size.Y;
        DrawColoredPolygon(new[] { new Vector2(w * 0.08f, h * 0.78f), new Vector2(w * 0.2f, h * 0.35f), new Vector2(w * 0.55f, h * 0.18f), new Vector2(w * 0.92f, h * 0.45f), new Vector2(w * 0.85f, h * 0.82f) }, new Color(0.42f, 0.4f, 0.44f));
        DrawColoredPolygon(new[] { new Vector2(w * 0.2f, h * 0.35f), new Vector2(w * 0.55f, h * 0.18f), new Vector2(w * 0.6f, h * 0.45f), new Vector2(w * 0.3f, h * 0.55f) }, new Color(0.6f, 0.58f, 0.62f));
        DrawColoredPolygon(new[] { new Vector2(w * 0.6f, h * 0.45f), new Vector2(w * 0.92f, h * 0.45f), new Vector2(w * 0.85f, h * 0.82f), new Vector2(w * 0.5f, h * 0.8f) }, new Color(0.3f, 0.28f, 0.32f));
    }
}

public partial class OilIcon : Control
{
    public OilIcon() { MouseFilter = MouseFilterEnum.Ignore; CustomMinimumSize = new Vector2(30, 20); }
    public override void _Draw()
    {
        var c = new Color(1f, 0.7f, 0.2f);
        float w = Size.X, h = Size.Y;
        DrawColoredPolygon(new[] { new Vector2(w * 0.5f, h * 0.02f), new Vector2(w * 0.8f, h * 0.6f), new Vector2(w * 0.5f, h * 0.98f), new Vector2(w * 0.2f, h * 0.6f) }, c);
        DrawColoredPolygon(new[] { new Vector2(w * 0.5f, h * 0.3f), new Vector2(w * 0.65f, h * 0.62f), new Vector2(w * 0.5f, h * 0.85f), new Vector2(w * 0.38f, h * 0.62f) }, new Color(1f, 0.92f, 0.5f));
    }
}

/// <summary>Red arcs around the screen centre pointing at whatever just hit you.</summary>
public partial class DamageRing : Control
{
    class Ind { public Vector3 Dir; public float Life = 1.6f; }
    readonly List<Ind> _list = new();

    public DamageRing() { MouseFilter = MouseFilterEnum.Ignore; }
    public void Add(Vector3 worldDir) { _list.Add(new Ind { Dir = worldDir }); QueueRedraw(); }

    public override void _Process(double delta)
    {
        if (_list.Count == 0) return;
        foreach (var i in _list) i.Life -= (float)delta;
        _list.RemoveAll(i => i.Life <= 0);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var m = Game.I?.LocalMiner;
        if (m == null) return;
        float yaw = m.Rotation.Y;
        var f = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw)); var r = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));
        var c = Size / 2f; float rad = Mathf.Min(Size.X, Size.Y) * 0.30f;
        foreach (var i in _list)
        {
            var d = new Vector2(i.Dir.X, i.Dir.Z);
            float bearing = Mathf.Atan2(d.Dot(r), d.Dot(f));
            float a = bearing - Mathf.Pi / 2f;
            float alpha = Mathf.Clamp(i.Life / 1.0f, 0f, 1f);
            DrawArc(c, rad, a - 0.32f, a + 0.32f, 16, new Color(0.9f, 0.1f, 0.08f, alpha * 0.9f), 16f, true);
            DrawArc(c, rad + 14f, a - 0.22f, a + 0.22f, 12, new Color(1f, 0.4f, 0.3f, alpha * 0.6f), 5f, true);
        }
    }
}
