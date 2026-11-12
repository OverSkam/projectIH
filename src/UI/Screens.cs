using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Match-end screen with a staged reveal (the between-round ceremony is WeighingUi).</summary>
public partial class Screens : CanvasLayer
{
    public static Screens I;
    Control _root;
    PanelContainer _panel;
    VBoxContainer _box;

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
    }

    public void Refresh()
    {
        var g = Game.I;
        bool show = g.Phase == GamePhase.MatchEnd && g.LastMatchEnd != null;
        _root.Visible = show;
        foreach (var c in _box.GetChildren()) c.QueueFree();
        if (!show) return;
        BuildMatchEnd(g.LastMatchEnd);
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

    void BuildMatchEnd(MatchEndData m)
    {
        _delay = 0f;
        Stage(UiKit.MakeLabel("THE LAST MINER STANDING IS", 22, UiTheme.Dim, HorizontalAlignment.Center));
        int wp = Net.I.Names.FirstOrDefault(kv => kv.Value == m.Winner).Key;
        var w = UiKit.HBox(14); w.Alignment = BoxContainer.AlignmentMode.Center;
        w.AddChild(new HelmetIcon(PlayerColors.Of(wp), 64));
        w.AddChild(UiKit.MakeTitle(m.Winner.ToUpper(), 60));
        Stage(w);
        Stage(GoblinSays(GoblinLines.Pick("winner", "m" + m.Winner, m.Winner), "happy"));
        var board = UiKit.VBox(4);
        board.AddChild(UiKit.Rule());
        board.AddChild(UiKit.MakeLabel("PLACEMENTS", 15, UiTheme.Brass, HorizontalAlignment.Center));
        for (int i = 0; i < m.Names.Length; i++)
        {
            int peer = Net.I.Names.FirstOrDefault(kv => kv.Value == m.Names[i]).Key;
            var row = UiKit.HBox(12);
            row.AddChild(UiKit.MakeLabel($"{i + 1}.", 24, i == 0 ? UiTheme.Gold : UiTheme.Dim));
            row.AddChild(new HelmetIcon(PlayerColors.Of(peer), 32));
            var n = UiKit.MakeLabel(m.Names[i], 24, i == 0 ? UiTheme.Gold : UiTheme.Parchment); n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; row.AddChild(n);
            row.AddChild(UiKit.MakeLabel(m.ThrownRound[i] == 0 ? "last miner standing" : $"thrown in round {m.ThrownRound[i]}", 18, i == 0 ? UiTheme.Gold : UiTheme.Dim, HorizontalAlignment.Right));
            board.AddChild(row);
        }
        Stage(board);
        Stage(UiKit.MakeLabel("Returning to the lobby...", 16, UiTheme.Dim, HorizontalAlignment.Center));
    }
}
