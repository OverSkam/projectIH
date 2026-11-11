using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Round summary and match-end screens with a staged reveal.</summary>
public partial class Screens : CanvasLayer
{
    public static Screens I;
    Control _root;
    PanelContainer _panel;
    VBoxContainer _box;
    int _roundStartMoney, _deliveredThisRound, _prevMoney;
    int _summaryMoneyAtStart;
    Label _totalLabel; float _shownTotal; bool _countTotal;
    readonly System.Collections.Generic.List<(Label l, int target, float shown)> _counters = new();

    public override void _Ready()
    {
        I = this;
        Layer = 8;
        _root = UiTheme.Apply(new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false });
        UiKit.FullRect(_root);
        AddChild(_root);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(dim); _root.AddChild(dim);
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(center); _root.AddChild(center);
        _panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(620, 0) };
        center.AddChild(_panel);
        _box = UiKit.VBox(10);
        _panel.AddChild(_box);
        Game.I.PhaseChanged += Refresh;
        Game.I.OwnChanged += OnOwn;
    }

    void OnOwn()
    {
        var g = Game.I;
        if (g.Phase == GamePhase.Round && g.OwnMoney > _prevMoney) _deliveredThisRound += g.OwnMoney - _prevMoney;
        _prevMoney = g.OwnMoney;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        for (int i = 0; i < _counters.Count; i++)
        {
            var c = _counters[i];
            c.shown = Mathf.MoveToward(c.shown, c.target, Mathf.Max(60f, Mathf.Abs(c.target - c.shown) * 1.6f) * dt);
            c.l.Text = UiKit.Money(Mathf.RoundToInt(c.shown));
            _counters[i] = c;
        }
        if (_countTotal && _totalLabel != null)
        {
            int live = Game.I.OwnMoney;
            _shownTotal = Mathf.MoveToward(_shownTotal, live, Mathf.Max(60f, Mathf.Abs(live - _shownTotal) * 1.6f) * dt);
            _totalLabel.Text = UiKit.Money(Mathf.RoundToInt(_shownTotal));
        }
    }

    public void Refresh()
    {
        var g = Game.I;
        if (g.Phase == GamePhase.Round)
        {
            _roundStartMoney = g.OwnMoney; _deliveredThisRound = 0; _prevMoney = g.OwnMoney;
        }
        bool show = g.Phase == GamePhase.Summary || g.Phase == GamePhase.MatchEnd;
        _root.Visible = show;
        foreach (var c in _box.GetChildren()) c.QueueFree();
        _counters.Clear(); _countTotal = false; _totalLabel = null;
        if (!show) return;
        if (g.Phase == GamePhase.Summary && g.LastSummary != null) BuildSummary(g.LastSummary);
        else if (g.Phase == GamePhase.MatchEnd && g.LastMatchEnd != null) BuildMatchEnd(g.LastMatchEnd);
        if (DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    float _delay;
    Control Stage(Control c)
    {
        c.Modulate = new Color(1, 1, 1, 0);
        _box.AddChild(c);
        var tw = CreateTween();
        tw.TweenProperty(c, "modulate:a", 1f, 0.35f).SetDelay(_delay);
        _delay += 0.7f;
        return c;
    }

    Control GoblinSays(string text, string mood)
    {
        var p = new PanelContainer();
        p.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.04f, 0.03f, 0.85f), mood == "angry" ? UiTheme.Danger : UiTheme.Brass, 8, 2, 12, 10));
        var h = UiKit.HBox(14);
        p.AddChild(h);
        h.AddChild(new GoblinPortrait { Mood = mood, CustomMinimumSize = new Vector2(76, 76) });
        var l = UiKit.MakeLabel("“" + text + "”", 20, UiTheme.Parchment);
        l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(440, 0); l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        h.AddChild(l);
        return p;
    }

    Control Line(string left, string right, Color rc, int size = 22, string tag = null)
    {
        var h = UiKit.HBox(10);
        var l = UiKit.MakeLabel(left, size, UiTheme.Parchment); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var r = UiKit.MakeLabel(right, size, rc, HorizontalAlignment.Right);
        h.AddChild(l); h.AddChild(r);
        return h;
    }

    void BuildSummary(SummaryData s)
    {
        var g = Game.I; _delay = 0f;
        Stage(UiKit.MakeTitle($"ROUND {s.Round} COMPLETE", 40));
        Stage(UiKit.Rule(UiTheme.Brass));

        var q = UiKit.VBox(6);
        q.AddChild(UiKit.MakeTitle(s.QuotaMet ? "QUOTA MET" : "QUOTA MISSED", 46, s.QuotaMet ? UiTheme.Good : UiTheme.Danger));
        var bar = new FillBar { CustomMinimumSize = new Vector2(0, 28), Max = Mathf.Max(1, s.Quota), Target = s.Delivered, Fill = s.QuotaMet ? UiTheme.Good : UiTheme.Danger, ShowTrail = false, FontSize = 17, Text = $"{s.Delivered} / {s.Quota} delivered" };
        bar.Snap();
        q.AddChild(bar);
        if (!s.QuotaMet) q.AddChild(UiKit.MakeLabel("Every miner loses 20% of their money.", 18, UiTheme.Danger, HorizontalAlignment.Center));
        Stage(q);

        if (s.Poorest != "")
        {
            var v = UiKit.VBox(4);
            v.AddChild(GoblinSays(GoblinLines.Pick("poorest", "r" + s.Round + s.Poorest, s.Poorest), "angry"));
            v.AddChild(UiKit.MakeLabel($"{s.Poorest} is cursed: starts the next round at 60% health.", 16, UiTheme.Dim, HorizontalAlignment.Center));
            Stage(v);
        }
        if (s.Survivor != "")
            Stage(GoblinSays(GoblinLines.Pick("survivor", "r" + s.Round + s.Survivor, s.Survivor, UiKit.Money(s.Bonus)), "smug"));

        // own earnings (private)
        var e = UiKit.VBox(4);
        e.AddChild(UiKit.Rule());
        e.AddChild(Line("You delivered this round", UiKit.Money(_deliveredThisRound), UiTheme.Gold));
        if (!s.QuotaMet) e.AddChild(Line("Quota penalty (20%)", "-" + UiKit.Money((int)(g.OwnMoney * g.Config.QuotaMissMoneyLoss)), UiTheme.Danger, 20));
        if (s.Survivor != "" && s.Survivor == Net.I.NameOf(Net.I.MyId)) e.AddChild(Line("Survivor bonus", "+" + UiKit.Money(s.Bonus), UiTheme.Good, 20));
        var tot = UiKit.HBox(10);
        var tl = UiKit.MakeLabel("Your money", 26, UiTheme.Parchment); tl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _totalLabel = UiKit.MakeLabel(UiKit.Money(_roundStartMoney), 30, UiTheme.Gold, HorizontalAlignment.Right, 6);
        _totalLabel.AddThemeFontOverride("font", UiTheme.Chunky);
        tot.AddChild(tl); tot.AddChild(_totalLabel);
        e.AddChild(tot);
        Stage(e);
        _shownTotal = _roundStartMoney; _countTotal = true;
        Stage(UiKit.MakeLabel(s.Last ? "The Goblin tallies the final count..." : "Next round starting...", 16, UiTheme.Dim, HorizontalAlignment.Center));
    }

    void BuildMatchEnd(MatchEndData m)
    {
        _delay = 0f;
        Stage(UiKit.MakeLabel("THE RICHEST MINER IS", 22, UiTheme.Dim, HorizontalAlignment.Center));
        int wp = Net.I.Names.FirstOrDefault(kv => kv.Value == m.Winner).Key;
        var w = UiKit.HBox(14); w.Alignment = BoxContainer.AlignmentMode.Center;
        w.AddChild(new HelmetIcon(PlayerColors.Of(wp), 64));
        w.AddChild(UiKit.MakeTitle(m.Winner.ToUpper(), 60));
        Stage(w);
        Stage(GoblinSays(GoblinLines.Pick("winner", "m" + m.Winner, m.Winner), "happy"));
        var board = UiKit.VBox(4);
        board.AddChild(UiKit.Rule());
        board.AddChild(UiKit.MakeLabel("FINAL MONEY  (the only time it is public)", 15, UiTheme.Brass, HorizontalAlignment.Center));
        for (int i = 0; i < m.Names.Length; i++)
        {
            int peer = Net.I.Names.FirstOrDefault(kv => kv.Value == m.Names[i]).Key;
            var row = UiKit.HBox(12);
            row.AddChild(UiKit.MakeLabel($"{i + 1}.", 24, i == 0 ? UiTheme.Gold : UiTheme.Dim));
            row.AddChild(new HelmetIcon(PlayerColors.Of(peer), 32));
            var n = UiKit.MakeLabel(m.Names[i], 24, i == 0 ? UiTheme.Gold : UiTheme.Parchment); n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(n);
            var ml = UiKit.MakeLabel("$0", 24, i == 0 ? UiTheme.Gold : UiTheme.Parchment, HorizontalAlignment.Right);
            row.AddChild(ml);
            board.AddChild(row);
            _counters.Add((ml, m.Money[i], 0f));
        }
        Stage(board);
        Stage(UiKit.MakeLabel("Returning to the lobby...", 16, UiTheme.Dim, HorizontalAlignment.Center));
    }
}
