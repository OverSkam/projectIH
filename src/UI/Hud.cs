using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace AppStein;

/// <summary>In-round HUD: only ever shows the local player's own private data.</summary>
public partial class Hud : CanvasLayer
{
    Label _top, _health, _money, _slots, _prompt, _cross, _feed, _death, _score;
    ProgressBar _hpBar;
    PanelContainer _scorePanel, _pausePanel;
    Control _root;

    public override void _Ready()
    {
        Layer = 5;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(_root);
        AddChild(_root);

        _top = UiKit.MakeLabel("", 22, Colors.White, HorizontalAlignment.Center);
        _top.SetAnchorsPreset(Control.LayoutPreset.TopWide); _top.OffsetTop = 8;
        _root.AddChild(_top);

        _cross = UiKit.MakeLabel("+", 24, new Color(1, 1, 1, 0.8f), HorizontalAlignment.Center);
        _cross.SetAnchorsPreset(Control.LayoutPreset.Center); _cross.GrowHorizontal = Control.GrowDirection.Both; _cross.GrowVertical = Control.GrowDirection.Both;
        _root.AddChild(_cross);

        _prompt = UiKit.MakeLabel("", 20, new Color(1f, 0.9f, 0.5f), HorizontalAlignment.Center);
        _prompt.SetAnchorsPreset(Control.LayoutPreset.CenterBottom); _prompt.OffsetTop = -200; _prompt.OffsetBottom = -170;
        _prompt.GrowHorizontal = Control.GrowDirection.Both;
        _root.AddChild(_prompt);

        var bl = new VBoxContainer();
        bl.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        bl.OffsetLeft = 20; bl.OffsetTop = -110; bl.OffsetBottom = -20; bl.GrowVertical = Control.GrowDirection.Begin;
        _root.AddChild(bl);
        _health = UiKit.MakeLabel("", 22, new Color(1f, 0.4f, 0.4f));
        _hpBar = new ProgressBar { MaxValue = 100, CustomMinimumSize = new Vector2(220, 16), ShowPercentage = false };
        _money = UiKit.MakeLabel("", 22, new Color(1f, 0.85f, 0.2f));
        bl.AddChild(_health); bl.AddChild(_hpBar); bl.AddChild(_money);

        _slots = UiKit.MakeLabel("", 18, Colors.White, HorizontalAlignment.Center);
        _slots.SetAnchorsPreset(Control.LayoutPreset.BottomWide); _slots.OffsetTop = -120; _slots.OffsetBottom = -10; _slots.GrowVertical = Control.GrowDirection.Begin;
        _slots.HorizontalAlignment = HorizontalAlignment.Center;
        _root.AddChild(_slots);

        _feed = UiKit.MakeLabel("", 16, new Color(0.7f, 1f, 0.7f), HorizontalAlignment.Right);
        _feed.SetAnchorsPreset(Control.LayoutPreset.TopRight); _feed.OffsetLeft = -460; _feed.OffsetTop = 50; _feed.OffsetRight = -16;
        _feed.GrowHorizontal = Control.GrowDirection.Begin;
        _root.AddChild(_feed);

        _death = UiKit.MakeLabel("", 30, new Color(0.7f, 0.8f, 1f), HorizontalAlignment.Center);
        _death.SetAnchorsPreset(Control.LayoutPreset.CenterTop); _death.OffsetTop = 120; _death.GrowHorizontal = Control.GrowDirection.Both;
        _root.AddChild(_death);

        _scorePanel = UiKit.MakePanel();
        _scorePanel.SetAnchorsPreset(Control.LayoutPreset.Center); _scorePanel.GrowHorizontal = Control.GrowDirection.Both; _scorePanel.GrowVertical = Control.GrowDirection.Both;
        _score = UiKit.MakeLabel("", 20);
        _scorePanel.AddChild(_score);
        _scorePanel.Visible = false;
        _root.AddChild(_scorePanel);

        _pausePanel = UiKit.MakePanel();
        _pausePanel.SetAnchorsPreset(Control.LayoutPreset.Center); _pausePanel.GrowHorizontal = Control.GrowDirection.Both; _pausePanel.GrowVertical = Control.GrowDirection.Both;
        var pv = new VBoxContainer();
        pv.AddChild(UiKit.MakeLabel("Paused (the game keeps running)", 20));
        pv.AddChild(UiKit.MakeButton("Resume", () => SetPause(false)));
        pv.AddChild(UiKit.MakeButton("Leave game", () => { SetPause(false); Game.I.LeaveToMenu(); }));
        _pausePanel.AddChild(pv);
        _pausePanel.Visible = false;
        _root.AddChild(_pausePanel);

        Game.I.PhaseChanged += () => { if (_pausePanel.Visible && Game.I.Phase == GamePhase.Lobby) _pausePanel.Visible = false; };
    }

    void SetPause(bool on)
    {
        _pausePanel.Visible = on;
        Input.MouseMode = on ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    public override void _Input(InputEvent e)
    {
        if (!_root.Visible) return;
        if (e.IsActionPressed("pause") && Game.I.Phase == GamePhase.Round && DisplayServer.GetName() != "headless")
            SetPause(!_pausePanel.Visible);
    }

    public override void _Process(double delta)
    {
        var g = Game.I;
        bool show = g.Phase == GamePhase.Round || g.Phase == GamePhase.Summary;
        _root.Visible = show;
        if (!show) return;
        int t = Mathf.Max(0, Mathf.CeilToInt(g.TimeLeft));
        _top.Text = $"Round {g.Round}/{g.TotalRounds}    Time {t / 60}:{t % 60:00}    Group quota {g.Delivered}/{g.Quota}";
        _feed.Text = string.Join("\n", g.Feed.Select(f => "Goblin: " + f));

        bool alive = g.LocalMiner != null;
        _health.Text = alive ? $"HP {Mathf.CeilToInt(g.OwnHealth)}" : "";
        _hpBar.Visible = alive; _hpBar.Value = g.OwnHealth;
        _money.Text = $"Money: {g.OwnMoney}";
        _prompt.Text = alive ? g.LocalMiner.PromptText : "";
        _cross.Visible = alive;
        _death.Text = (!alive && g.LocalGhost != null) ? "YOU ARE A GHOST\nFly with WASD / Space / Ctrl.  Respawn next round." : "";

        var sb = new StringBuilder();
        var inv = g.OwnInv;
        sb.Append("SACK   ");
        for (int i = 0; i < g.OwnSackCap; i++) sb.Append(i < inv.Sack.Count ? $"[{Ores.Get(inv.Sack[i])?.Name}] " : "[ ] ");
        sb.Append("\nSTEIN  ");
        for (int i = 0; i < Inventory.SteinSlots; i++) sb.Append(i < inv.Steins.Count ? $"[{Steins.Get(inv.Steins[i])?.Name}] " : "[ ] ");
        sb.Append("\nUTILITY  ");
        for (int i = 0; i < Inventory.UtilSlots; i++)
        {
            var d = Utilities.Get(inv.Utils[i]);
            sb.Append($"{i + 1}:{(d != null ? d.Name : "-")}{(inv.Selected == i ? "*" : "")}   ");
        }
        _slots.Text = alive ? sb.ToString() : "";

        _scorePanel.Visible = Input.IsActionPressed("scoreboard");
        if (_scorePanel.Visible)
        {
            var s = new StringBuilder($"Round {g.Round}/{g.TotalRounds}\nYour money: {g.OwnMoney}\n\n");
            foreach (var kv in Net.I.Names) s.Append($"{kv.Value}   {(g.Alive.GetValueOrDefault(kv.Key) ? "alive" : "dead / ghost")}\n");
            s.Append("\n(Other miners' money is secret.)");
            _score.Text = s.ToString();
        }
    }
}
