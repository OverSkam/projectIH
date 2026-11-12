using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>In-round HUD. Reads only Game/Net state and events and only ever shows the local player's own private data.</summary>
public partial class Hud : CanvasLayer
{
    public static Hud I;

    Control _root;
    // top
    Label _roundLabel, _timer, _remaining;
    PanelContainer _topPanel;
    // bottom-left
    FillBar _hpBar, _oilBar, _holdBar;
    Label _tribute, _tributePop;
    Control _downedOverlay; Label _downedLabel;
    Label _deathTitle, _deathSub;
    Control _bl;
    // slots
    HBoxContainer _slotRow;
    readonly List<SlotView> _sack = new(), _steins = new(), _utils = new();
    HBoxContainer _sackBox, _steinBox, _utilBox;
    // centre
    Control _cross;
    PanelContainer _promptPanel; Label _promptKey, _promptText;
    VBoxContainer _feed;
    readonly List<(Control node, double born)> _feedNodes = new();
    // overlays
    ColorRect _vignette; ShaderMaterial _vigMat;
    DamageRing _ring;
    Control _deathOverlay;
    PanelContainer _scorePanel; VBoxContainer _scoreRows; double _scoreT;
    // items overlay (notices, key hints, owl tint, map)
    Label _notice, _hint;
    PanelContainer _steinInfo; VBoxContainer _steinInfoRows; string _steinInfoSig = "";
    ColorRect _tint;
    MapView _map;
    OmniLight3D _owl;

    float _prevHp = 100f, _flash; int _prevTribute, _lastSackCap = -1;
    double _timeNow;
    Vector3 _lastKnock; double _lastKnockT = -10;
    public bool ForceScoreboard;

    class SlotView
    {
        public PanelContainer Panel; public ItemIcon Icon; public Label Key, Caption; public StyleBoxFlat Style;
    }

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
        BuildTop();
        BuildCenter();
        BuildBottomLeft();
        BuildSlots();
        BuildFeed();
        BuildDeath();
        BuildDowned();
        BuildScoreboard();
        BuildItemOverlays();

        Miner.Knocked += v => { _lastKnock = v; _lastKnockT = Time.GetTicksMsec() / 1000.0; };
        GoblinVoice.Said += OnSaid;
        Game.I.PhaseChanged += OnPhase;
    }

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
    }

    void BuildTop()
    {
        _topPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _topPanel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.06f, 0.05f, 0.04f, 0.78f), UiTheme.BrassDark, 8, 2, 20, 8));
        _topPanel.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _topPanel.GrowHorizontal = Control.GrowDirection.Both;
        _topPanel.OffsetTop = 10;
        _root.AddChild(_topPanel);
        var v = UiKit.VBox(2);
        _topPanel.AddChild(v);
        _roundLabel = UiKit.MakeLabel("ROUND 1 / 5", 15, UiTheme.Dim, HorizontalAlignment.Center);
        _timer = UiKit.MakeLabel("6:00", 40, UiTheme.Parchment, HorizontalAlignment.Center, 8);
        _timer.AddThemeFontOverride("font", UiTheme.Chunky);
        _timer.CustomMinimumSize = new Vector2(150, 0);
        _timer.PivotOffset = new Vector2(75, 24);
        _remaining = UiKit.MakeLabel("3 MINERS REMAIN", 15, UiTheme.Gold, HorizontalAlignment.Center);
        v.AddChild(_roundLabel); v.AddChild(_timer); v.AddChild(_remaining);
        _timer.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter;
    }

    void BuildCenter()
    {
        _cross = new Crosshair(); _cross.SetAnchorsPreset(Control.LayoutPreset.Center);
        _cross.GrowHorizontal = Control.GrowDirection.Both; _cross.GrowVertical = Control.GrowDirection.Both;
        _cross.CustomMinimumSize = new Vector2(28, 28);
        _root.AddChild(_cross);

        _promptPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _promptPanel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.04f, 0.03f, 0.75f), UiTheme.BrassDark, 6, 1, 12, 6));
        _promptPanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _promptPanel.GrowHorizontal = Control.GrowDirection.Both;
        _promptPanel.OffsetTop = 44; _promptPanel.OffsetBottom = 44;
        _promptPanel.Visible = false;
        _root.AddChild(_promptPanel);
        var h = UiKit.HBox(10);
        _promptPanel.AddChild(h);
        _promptKey = UiKit.MakeLabel("E", 20, UiTheme.Gold, HorizontalAlignment.Center);
        var cap = new PanelContainer { CustomMinimumSize = new Vector2(34, 34), MouseFilter = Control.MouseFilterEnum.Ignore };
        cap.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.StoneLight, UiTheme.Gold, 5, 2, 8, 2));
        cap.AddChild(_promptKey);
        h.AddChild(cap);
        _promptText = UiKit.MakeLabel("", 20, UiTheme.Parchment);
        _promptText.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(_promptText);
        _holdBar = new FillBar { CustomMinimumSize = new Vector2(200, 12), Fill = UiTheme.Gold, Max = 1, ShowTrail = false, Visible = false, FontSize = 1 };
        _holdBar.SetAnchorsPreset(Control.LayoutPreset.Center);
        _holdBar.GrowHorizontal = Control.GrowDirection.Both;
        _holdBar.OffsetTop = 120; _holdBar.OffsetBottom = 132; _holdBar.OffsetLeft = -100; _holdBar.OffsetRight = 100;
        _root.AddChild(_holdBar);
    }

    void BuildBottomLeft()
    {
        _bl = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _bl.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _bl.OffsetLeft = 20; _bl.OffsetTop = -150; _bl.OffsetRight = 290; _bl.OffsetBottom = -18;
        _root.AddChild(_bl);
        var v = UiKit.VBox(6); UiKit.FullRect(v); _bl.AddChild(v);

        var hh = UiKit.HBox(8);
        v.AddChild(hh);
        hh.AddChild(new HeartIcon());
        _hpBar = new FillBar { CustomMinimumSize = new Vector2(185, 28), Fill = new Color(0.82f, 0.18f, 0.18f), FontSize = 18, Max = 100 };
        hh.AddChild(_hpBar);

        // lantern oil gauge (the oil level is private; others only see the light itself)
        var oh = UiKit.HBox(8);
        v.AddChild(oh);
        oh.AddChild(new OilIcon());
        _oilBar = new FillBar { CustomMinimumSize = new Vector2(185, 20), Fill = new Color(1f, 0.66f, 0.18f), FontSize = 14, Max = 100, ShowTrail = false };
        oh.AddChild(_oilBar);

        var mh = UiKit.HBox(8);
        v.AddChild(mh);
        mh.AddChild(new StoneIcon());
        var tv = UiKit.VBox(0);
        mh.AddChild(tv);
        tv.AddChild(UiKit.MakeLabel("YOUR TRIBUTE THIS ROUND", 12, UiTheme.Brass, HorizontalAlignment.Left, 3));
        _tribute = UiKit.MakeLabel("0", 30, UiTheme.Gold, HorizontalAlignment.Left, 8);
        _tribute.AddThemeFontOverride("font", UiTheme.Chunky);
        tv.AddChild(_tribute);
        _tributePop = UiKit.MakeLabel("", 24, UiTheme.Good, HorizontalAlignment.Left, 6);
        _tributePop.Position = new Vector2(190, 70);
        _bl.AddChild(_tributePop);
    }

    SlotView MakeSlot(HBoxContainer into, string key = null)
    {
        var sv = new SlotView();
        var p = new PanelContainer { CustomMinimumSize = new Vector2(48, 48), MouseFilter = Control.MouseFilterEnum.Ignore };
        sv.Style = UiTheme.Box(new Color(0.07f, 0.06f, 0.05f, 0.85f), UiTheme.BrassDark, 6, 2, 4, 4);
        p.AddThemeStyleboxOverride("panel", sv.Style);
        sv.Icon = new ItemIcon { CustomMinimumSize = new Vector2(38, 38) };
        sv.Icon.SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter; sv.Icon.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        p.AddChild(sv.Icon);
        if (key != null)
        {
            sv.Key = UiKit.MakeLabel(key, 13, UiTheme.Gold, HorizontalAlignment.Left, 4);
            sv.Key.Position = new Vector2(6, 0);
            p.AddChild(sv.Key);
            sv.Key.TopLevel = false;
        }
        sv.Panel = p;
        var col = UiKit.VBox(1);
        col.AddChild(p);
        sv.Caption = UiKit.MakeLabel("", 12, UiTheme.Dim, HorizontalAlignment.Center, 3);
        sv.Caption.CustomMinimumSize = new Vector2(64, 0); sv.Caption.ClipText = true;
        col.AddChild(sv.Caption);
        col.CustomMinimumSize = new Vector2(64, 0);
        into.AddChild(col);
        return sv;
    }

    void BuildSlots()
    {
        var anchor = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        anchor.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        anchor.GrowHorizontal = Control.GrowDirection.Both; anchor.GrowVertical = Control.GrowDirection.Begin;
        anchor.OffsetTop = -112; anchor.OffsetBottom = -14;
        _root.AddChild(anchor);
        _slotRow = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _slotRow.AddThemeConstantOverride("separation", 12);
        _slotRow.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _slotRow.GrowHorizontal = Control.GrowDirection.Both; _slotRow.GrowVertical = Control.GrowDirection.Begin;
        anchor.AddChild(_slotRow);

        _sackBox = Group("SACK", out _);
        _steinBox = Group("STEINS", out _);
        _utilBox = Group("UTILITY", out _);
        for (int i = 0; i < Inventory.SteinSlots; i++) _steins.Add(MakeSlot(_steinBox, ""));
        for (int i = 0; i < Inventory.UtilSlots; i++) _utils.Add(MakeSlot(_utilBox, (i + 1).ToString()));
    }

    void BuildItemOverlays()
    {
        _tint = new ColorRect { Color = new Color(0.4f, 0.6f, 1f, 0.07f), MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiKit.FullRect(_tint); _root.AddChild(_tint); _root.MoveChild(_tint, 0);

        // one compact context key-hint line, centred above the slot row
        _hint = UiKit.MakeLabel("", 14, new Color(0.85f, 0.88f, 0.95f, 0.9f), HorizontalAlignment.Center, 4);
        _hint.SetAnchorsPreset(Control.LayoutPreset.CenterBottom);
        _hint.GrowHorizontal = Control.GrowDirection.Both; _hint.GrowVertical = Control.GrowDirection.Begin;
        _hint.OffsetTop = -138; _hint.OffsetBottom = -116;
        _root.AddChild(_hint);

        // held steins: name (rarity colour) + one-line description, always visible, bottom right
        _steinInfo = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _steinInfo.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.045f, 0.04f, 0.62f), new Color(UiTheme.BrassDark, 0.6f), 8, 1, 8, 5));
        _steinInfo.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        _steinInfo.GrowHorizontal = Control.GrowDirection.Begin; _steinInfo.GrowVertical = Control.GrowDirection.Begin;
        _steinInfo.OffsetLeft = -256; _steinInfo.OffsetRight = -16; _steinInfo.OffsetTop = -150; _steinInfo.OffsetBottom = -14;
        _steinInfoRows = UiKit.VBox(4); _steinInfo.AddChild(_steinInfoRows);
        _root.AddChild(_steinInfo);

        // short notices from the item systems, just above the crosshair
        _notice = UiKit.MakeLabel("", 22, new Color(1f, 0.95f, 0.6f), HorizontalAlignment.Center, 6);
        _notice.SetAnchorsPreset(Control.LayoutPreset.Center);
        _notice.GrowHorizontal = Control.GrowDirection.Both; _notice.GrowVertical = Control.GrowDirection.Begin;
        _notice.OffsetTop = -130; _notice.OffsetBottom = -90;
        _notice.AutowrapMode = TextServer.AutowrapMode.WordSmart; _notice.CustomMinimumSize = new Vector2(720, 0);
        _root.AddChild(_notice);

        _map = new MapView { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _map.SetAnchorsPreset(Control.LayoutPreset.Center);
        _map.CustomMinimumSize = new Vector2(420, 420);
        _map.OffsetLeft = -210; _map.OffsetRight = 210; _map.OffsetTop = -210; _map.OffsetBottom = 210;
        _root.AddChild(_map);
    }

    HBoxContainer Group(string title, out Label caption)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.045f, 0.04f, 0.6f), new Color(UiTheme.BrassDark, 0.6f), 8, 1, 8, 4));
        var v = UiKit.VBox(0);
        p.AddChild(v);
        caption = UiKit.MakeLabel(title, 12, UiTheme.Brass, HorizontalAlignment.Center, 3);
        v.AddChild(caption);
        var h = UiKit.HBox(6);
        v.AddChild(h);
        _slotRow.AddChild(p);
        return h;
    }

    void BuildFeed()
    {
        _feed = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _feed.AddThemeConstantOverride("separation", 6);
        _feed.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _feed.OffsetLeft = -424; _feed.OffsetRight = -16; _feed.OffsetTop = 14;
        _feed.GrowHorizontal = Control.GrowDirection.Begin;
        _root.AddChild(_feed);
    }

    void BuildDeath()
    {
        _deathOverlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiKit.FullRect(_deathOverlay);
        _root.AddChild(_deathOverlay);
        var tint = new ColorRect { Color = new Color(0.05f, 0.08f, 0.2f, 0.28f), MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(tint); _deathOverlay.AddChild(tint);
        var box = UiKit.VBox(6);
        box.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        box.GrowHorizontal = Control.GrowDirection.Both;
        box.OffsetTop = 150;
        _deathOverlay.AddChild(box);
        _deathTitle = UiKit.MakeTitle("YOU DIED", 72, UiTheme.Danger);
        box.AddChild(_deathTitle);
        _deathSub = UiKit.MakeLabel("You will return next round.", 28, new Color(0.8f, 0.86f, 1f), HorizontalAlignment.Center, 6);
        box.AddChild(_deathSub);
        box.AddChild(UiKit.Spacer(6));
        box.AddChild(UiKit.MakeLabel("Ghost: fly with WASD / Space / Ctrl.  Right mouse: snuff a lantern.  E: knock a pebble.  Tribute already fed still counts.", 17, UiTheme.Dim, HorizontalAlignment.Center));
    }

    void BuildDowned()
    {
        _downedOverlay = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiKit.FullRect(_downedOverlay);
        _root.AddChild(_downedOverlay);
        var tint = new ColorRect { Color = new Color(0.35f, 0.02f, 0.02f, 0.32f), MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(tint); _downedOverlay.AddChild(tint);
        var box = UiKit.VBox(4);
        box.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        box.GrowHorizontal = Control.GrowDirection.Both;
        box.OffsetTop = 120;
        _downedOverlay.AddChild(box);
        box.AddChild(UiKit.MakeTitle("YOU ARE DOWN", 64, UiTheme.Danger));
        _downedLabel = UiKit.MakeLabel("Bleeding out in 30 s", 30, new Color(1f, 0.8f, 0.75f), HorizontalAlignment.Center, 6);
        box.AddChild(_downedLabel);
        box.AddChild(UiKit.MakeLabel("You can only crawl. Anyone may rob you. Any hit finishes you.", 17, UiTheme.Dim, HorizontalAlignment.Center));
    }

    void BuildScoreboard()
    {
        _scorePanel = new PanelContainer { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        _scorePanel.SetAnchorsPreset(Control.LayoutPreset.Center);
        _scorePanel.GrowHorizontal = Control.GrowDirection.Both; _scorePanel.GrowVertical = Control.GrowDirection.Both;
        _scorePanel.CustomMinimumSize = new Vector2(560, 0);
        _root.AddChild(_scorePanel);
        var v = UiKit.VBox(8);
        _scorePanel.AddChild(v);
        v.AddChild(UiKit.MakeTitle("MINERS", 30));
        v.AddChild(UiKit.Rule());
        _scoreRows = UiKit.VBox(6);
        v.AddChild(_scoreRows);
        v.AddChild(UiKit.Rule());
        v.AddChild(UiKit.MakeLabel("Nobody's tribute weight is public. Until the Scale.", 14, UiTheme.Dim, HorizontalAlignment.Center));
    }

    // ------------------------------------------------------------------ events
    void OnPhase()
    {
        if (Game.I.Phase == GamePhase.Round)
        {
            _prevHp = Game.I.OwnHealth; _hpBar.Target = Game.I.OwnHealth; _hpBar.Snap();
            _prevTribute = Game.I.OwnTribute;
        }
        if (Game.I.Phase != GamePhase.Round) ClearFeed();
    }

    void ClearFeed()
    {
        foreach (var (n, _) in _feedNodes) n.QueueFree();
        _feedNodes.Clear();
    }

    void OnSaid(GoblinSaying s)
    {
        var p = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(1, 1, 1, 0) };
        p.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.04f, 0.03f, 0.82f), s.Mood == "angry" ? UiTheme.Danger : UiTheme.Brass, 8, 2, 10, 8));
        var h = UiKit.HBox(10);
        p.AddChild(h);
        var por = new GoblinPortrait { Mood = s.Mood, CustomMinimumSize = new Vector2(54, 54) };
        por.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        h.AddChild(por);
        var v = UiKit.VBox(0);
        h.AddChild(v);
        v.AddChild(UiKit.MakeLabel("THE GOBLIN", 12, s.Mood == "angry" ? UiTheme.Danger : UiTheme.GoblinGreen, HorizontalAlignment.Left, 3));
        var l = UiKit.MakeLabel(s.Text, 17, UiTheme.Parchment, HorizontalAlignment.Left, 4);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        l.CustomMinimumSize = new Vector2(318, 0);
        v.AddChild(l);
        _feed.AddChild(p);
        _feedNodes.Add((p, Time.GetTicksMsec() / 1000.0));
        while (_feedNodes.Count > 4) { _feedNodes[0].node.QueueFree(); _feedNodes.RemoveAt(0); }
        var tw = CreateTween();
        tw.TweenProperty(p, "modulate:a", 1f, 0.25f);
    }

    // ------------------------------------------------------------------ per frame
    public override void _Process(double delta)
    {
        var g = Game.I;
        float dt = (float)delta;
        _timeNow = Time.GetTicksMsec() / 1000.0;
        bool show = g.Phase == GamePhase.Round;
        _root.Visible = show;
        if (!show) return;

        // top: round, timer (urgent in the last 30 s), miners remaining
        int t = Mathf.Max(0, Mathf.CeilToInt(g.TimeLeft));
        _roundLabel.Text = $"ROUND {g.Round} / {g.TotalRounds}";
        _timer.Text = $"{t / 60}:{t % 60:00}";
        bool urgent = g.TimeLeft <= 30f && g.TimeLeft > 0f;
        Color tc = urgent ? UiTheme.Danger : (g.TimeLeft <= 60f ? new Color(1f, 0.7f, 0.25f) : UiTheme.Parchment);
        _timer.AddThemeColorOverride("font_color", tc);
        float pulse = urgent ? 1f + 0.12f * Mathf.Abs(Mathf.Sin((float)_timeNow * (g.TimeLeft <= 10f ? 7f : 3.5f))) : 1f;
        _timer.Scale = Vector2.One * pulse;
        int rem = g.Remaining;
        _remaining.Text = rem == 1 ? "1 MINER REMAINS" : $"{rem} MINERS REMAIN";

        bool alive = g.LocalMiner != null;
        bool ghost = !alive && g.LocalGhost != null;

        // health: fills/empties, flashes on damage, vignette when low
        float hp = g.OwnHealth;
        _hpBar.Target = alive ? hp : 0f;
        _hpBar.Text = alive ? Mathf.CeilToInt(hp).ToString() : "0";
        if (alive && hp < _prevHp - 0.4f) OnDamaged(_prevHp - hp);
        _prevHp = hp;
        _hpBar.Pulsing = alive && hp <= 30f;
        _flash = Mathf.Max(0f, _flash - dt * 1.8f);
        float lowHp = alive ? Mathf.Clamp((45f - hp) / 45f, 0f, 1f) : 0f;
        float beat = lowHp > 0 ? 0.65f + 0.35f * Mathf.Abs(Mathf.Sin((float)_timeNow * (2f + 3f * lowHp))) : 0f;
        _vigMat.SetShaderParameter("strength", Mathf.Clamp(lowHp * 0.95f * beat + _flash * 0.8f, 0f, 1f));
        _bl.Visible = alive || ghost;

        // tribute: own weight this round only (pops on every feeding)
        int tribute = g.OwnTribute;
        if (tribute != _prevTribute)
        {
            int d = tribute - _prevTribute;
            if (d != 0) PopTribute(d);
            _prevTribute = tribute;
        }
        _tribute.Text = tribute.ToString();

        // lantern oil gauge: private level, low-oil warning
        float oilMax = Mathf.Max(1f, g.OwnOilMax);
        bool hasLantern = System.Array.IndexOf(g.OwnInv.Utils, "lantern") >= 0;
        _oilBar.Max = oilMax; _oilBar.Target = g.OwnOil;
        bool low = hasLantern && g.OwnOil <= oilMax * ItemsServer.OilLowFraction;
        bool on = g.LocalMiner != null && !g.LocalMiner.LanternOut;
        _oilBar.Fill = low ? UiTheme.Danger : new Color(1f, 0.66f, 0.18f);
        _oilBar.Pulsing = alive && low && g.OwnOil > 0f;
        _oilBar.Text = !hasLantern ? "NO LANTERN" : g.OwnOil <= 0f ? "OUT OF OIL" : $"OIL {Mathf.CeilToInt(g.OwnOil / oilMax * 100f)}%" + (on ? "" : "  (off)");
        _bl.Visible = alive || ghost;
        _oilBar.GetParent<Control>().Visible = alive;
        _tribute.GetParent().GetParent<Control>().Visible = true;

        UpdateSlots(g, alive);
        UpdateItemOverlays(g, alive);

        // prompt
        string prompt = alive && !PauseMenu.I.IsOpen ? g.LocalMiner.PromptText : "";
        _promptPanel.Visible = prompt != "";
        _cross.Visible = alive && !PauseMenu.I.IsOpen;
        if (prompt != "")
        {
            if (prompt.StartsWith("E ")) { _promptKey.Text = "E"; _promptText.Text = prompt.Substring(1).Trim(); _promptKey.GetParent<Control>().Visible = true; _promptText.Modulate = Colors.White; }
            else { _promptKey.GetParent<Control>().Visible = false; _promptText.Text = prompt; _promptText.Modulate = new Color(1, 1, 1, 0.75f); }
        }

        bool downed = alive && g.OwnDowned;
        _downedOverlay.Visible = downed;
        if (downed)
        {
            double left = System.Math.Max(0, g.OwnDownedUntil - _timeNow);
            _downedLabel.Text = $"Bleeding out in {Mathf.CeilToInt((float)left)} s";
        }
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

        // feed fade-out
        for (int i = _feedNodes.Count - 1; i >= 0; i--)
        {
            double age = _timeNow - _feedNodes[i].born;
            if (age > 11) { _feedNodes[i].node.QueueFree(); _feedNodes.RemoveAt(i); }
            else if (age > 9.5) _feedNodes[i].node.Modulate = new Color(1, 1, 1, (float)(11 - age) / 1.5f);
        }

        // scoreboard
        bool score = (Input.IsActionPressed("scoreboard") || ForceScoreboard) && !PauseMenu.I.IsOpen;
        _scorePanel.Visible = score;
        if (score && _timeNow - _scoreT > 0.2) { _scoreT = _timeNow; RebuildScoreboard(g); }
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

    void PopTribute(int d)
    {
        if (_tributePop == null) return;
        _tributePop.Text = (d > 0 ? "+" : "-") + Mathf.Abs(d);
        _tributePop.AddThemeColorOverride("font_color", d > 0 ? UiTheme.Good : UiTheme.Danger);
        _tributePop.Position = new Vector2(190, 70); _tributePop.Modulate = Colors.White;
        var tw = CreateTween();
        tw.SetParallel(true);
        tw.TweenProperty(_tributePop, "position:y", 30f, 1.1f).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tw.TweenProperty(_tributePop, "modulate:a", 0f, 0.5f).SetDelay(0.9f);
    }

    // ------------------------------------------------------------------ slots (data-driven from the inventory + Def registries)
    void UpdateSlots(Game g, bool alive)
    {
        _slotRow.GetParent<Control>().Visible = alive;
        if (!alive) return;
        var inv = g.OwnInv;
        if (g.OwnSackCap != _lastSackCap)
        {
            _lastSackCap = g.OwnSackCap;
            foreach (var c in _sackBox.GetChildren()) c.QueueFree();
            _sack.Clear();
            for (int i = 0; i < g.OwnSackCap; i++) _sack.Add(MakeSlot(_sackBox));
        }
        for (int i = 0; i < _sack.Count; i++)
        {
            string id = i < inv.Sack.Count ? inv.Sack[i] : "";
            var d = id == "" ? null : Ores.Get(id);
            Fill(_sack[i], "ore", id, d?.Color ?? UiTheme.BrassDark, id == "" ? "" : (d?.TributeWeight ?? 0).ToString(), false);
        }
        for (int i = 0; i < _steins.Count; i++)
        {
            string id = i < inv.Steins.Count ? inv.Steins[i] : "";
            var d = id == "" ? null : Steins.Get(id);
            Color border = d == null ? UiTheme.BrassDark : d.Rarity == Rarity.Legendary ? new Color(1f, 0.55f, 0.15f) : d.Rarity == Rarity.Rare ? new Color(0.35f, 0.65f, 1f) : new Color(0.75f, 0.7f, 0.6f);
            bool sel = inv.SteinSel == i && inv.Steins.Count > 1;
            string cap = d == null ? "" : d.Name.Replace(" Stein", "");
            string key = "";
            Color? capCol = null;
            if (d != null && d.Ability != null && d.Ability == g.OwnAbility)
            {
                double now = Time.GetTicksMsec() / 1000.0, cd = g.OwnAbilityReadyAt - now, act = g.OwnEtherealUntil - now;
                cap = act > 0 ? $"ACTIVE {act:0}s" : cd > 0 ? $"{Mathf.CeilToInt((float)cd)}s" : "READY";
                key = "Q";
                capCol = act > 0 ? UiTheme.Good : cd > 0 ? UiTheme.Dim : UiTheme.Gold;
            }
            Fill(_steins[i], "stein", id, border, cap, sel);
            if (_steins[i].Key != null) { _steins[i].Key.Text = key; _steins[i].Key.AddThemeColorOverride("font_color", UiTheme.Gold); }
            if (capCol.HasValue) _steins[i].Caption.AddThemeColorOverride("font_color", capCol.Value);
        }
        for (int i = 0; i < _utils.Count; i++)
        {
            string id = i < inv.Utils.Length ? inv.Utils[i] ?? "" : "";
            var d = Utilities.Get(id);
            bool sel = inv.Selected == i;
            Fill(_utils[i], "util", id, sel ? UiTheme.Gold : UiTheme.BrassDark, d?.Name ?? "", sel);
        }
    }

    void UpdateSteinInfo(Inventory inv, bool alive)
    {
        string sig = alive ? string.Join(",", inv.Steins) : "";
        _steinInfo.Visible = sig != "";
        if (sig == _steinInfoSig) return;
        _steinInfoSig = sig;
        foreach (var c in _steinInfoRows.GetChildren()) c.QueueFree();
        foreach (var id in inv.Steins)
        {
            var d = Steins.Get(id); if (d == null) continue;
            var col = ItemInfo.RarityColor(d.Rarity);
            _steinInfoRows.AddChild(UiKit.MakeLabel($"{d.Name}  ({ItemInfo.RarityName(d.Rarity)})", 14, col, HorizontalAlignment.Left, 3));
            var t = UiKit.MakeLabel(d.Description, 12, UiTheme.Parchment, HorizontalAlignment.Left, 2);
            t.AutowrapMode = TextServer.AutowrapMode.WordSmart; t.CustomMinimumSize = new Vector2(224, 0);
            _steinInfoRows.AddChild(t);
        }
    }

    void UpdateItemOverlays(Game g, bool alive)
    {
        double now = Time.GetTicksMsec() / 1000.0;
        var inv = g.OwnInv;
        var parts = new List<string>();
        if (alive)
        {
            var held = Utilities.Get(inv.Held);
            parts.Add("1/2 item");
            if (System.Array.IndexOf(inv.Utils, "lantern") >= 0) parts.Add("L lantern on/off");
            if (held != null && held.IsPickaxe) parts.Add("LMB mine / strike");
            else if (held != null && held.Id != "lantern") parts.Add("RMB use " + held.Name.ToLower());
            if (inv.Steins.Count > 1) parts.Add("F stein slot");
            if (g.OwnAbility != "") parts.Add("Q " + char.ToUpper(g.OwnAbility[0]) + g.OwnAbility.Substring(1));
            if (inv.Sack.Count > 0) parts.Add("T drop stone");
        }
        _hint.Text = string.Join("   |   ", parts);
        _hint.Visible = alive && !PauseMenu.I.IsOpen;
        _notice.Text = alive && now - g.ItemNoticeAt < 4 + g.ItemNotice.Length / 25.0 ? g.ItemNotice : "";
        UpdateSteinInfo(inv, alive);
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

    void Fill(SlotView s, string kind, string id, Color border, string caption, bool selected)
    {
        s.Icon.Set(kind, id);
        s.Caption.Text = caption;
        s.Caption.AddThemeColorOverride("font_color", selected ? UiTheme.Gold : UiTheme.Dim);
        s.Style.BorderColor = border;
        int bw = selected ? 3 : 2;
        s.Style.SetBorderWidthAll(bw);
        s.Style.BgColor = selected ? new Color(0.22f, 0.17f, 0.07f, 0.92f) : new Color(0.07f, 0.06f, 0.05f, 0.85f);
        if (s.Key != null) s.Key.AddThemeColorOverride("font_color", selected ? UiTheme.Gold : UiTheme.Dim);
    }

    // ------------------------------------------------------------------ scoreboard (only the local player's money is ever shown)
    void RebuildScoreboard(Game g)
    {
        foreach (var c in _scoreRows.GetChildren()) c.QueueFree();
        foreach (var kv in Net.I.Names)
        {
            bool me = kv.Key == Net.I.MyId;
            var status = g.Status.GetValueOrDefault(kv.Key);
            bool alive = status is MinerStatus.Alive or MinerStatus.Downed;
            var row = UiKit.HBox(12);
            row.CustomMinimumSize = new Vector2(0, 36);
            var hi = new HelmetIcon(PlayerColors.Of(kv.Key), 34) { Dead = status != MinerStatus.Alive };
            hi.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(hi);
            var name = UiKit.MakeLabel(kv.Value + (me ? "  (you)" : "") + (kv.Key == 1 ? "  [host]" : ""), 22, me ? UiTheme.Gold : (status == MinerStatus.Eliminated ? UiTheme.Dim : UiTheme.Parchment));
            name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(name);
            string stText = status switch { MinerStatus.Alive => "ALIVE", MinerStatus.Downed => "DOWNED", MinerStatus.Dead => "DEAD", _ => "ELIMINATED" };
            var stCol = status switch { MinerStatus.Alive => UiTheme.Good, MinerStatus.Downed => new Color(1f, 0.6f, 0.2f), MinerStatus.Dead => UiTheme.Danger, _ => UiTheme.Dim };
            var st = UiKit.MakeLabel(stText, 17, stCol, HorizontalAlignment.Center);
            st.CustomMinimumSize = new Vector2(120, 0); st.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(st);
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
