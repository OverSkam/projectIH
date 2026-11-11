using System.Linq;
using System.Text;
using Godot;

namespace AppStein;

/// <summary>Round summary and match winner overlays.</summary>
public partial class Screens : CanvasLayer
{
    PanelContainer _panel;
    Label _text;

    public override void _Ready()
    {
        Layer = 8;
        var center = new CenterContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(center);
        AddChild(center);
        _panel = UiKit.MakePanel(new Color(0.05f, 0.04f, 0.03f, 0.92f));
        _text = UiKit.MakeLabel("", 24, Colors.White, HorizontalAlignment.Center);
        _text.CustomMinimumSize = new Vector2(560, 0);
        _panel.AddChild(_text);
        center.AddChild(_panel);
        _panel.Visible = false;
        Game.I.PhaseChanged += Refresh;
    }

    void Refresh()
    {
        var g = Game.I;
        _panel.Visible = g.Phase == GamePhase.Summary || g.Phase == GamePhase.MatchEnd;
        if (g.Phase == GamePhase.Summary && g.LastSummary != null)
        {
            var s = g.LastSummary;
            var sb = new StringBuilder();
            sb.Append($"ROUND {s.Round} COMPLETE\n\n");
            sb.Append(s.QuotaMet ? $"Quota met: {s.Delivered}/{s.Quota}\n" : $"Quota MISSED: {s.Delivered}/{s.Quota}\nEvery miner loses 20% of their money!\n");
            if (s.Poorest != "") sb.Append($"\nThe Goblin names {s.Poorest} the poorest miner.\nCursed: starts the next round at 60% health.\n");
            if (s.Survivor != "") sb.Append($"\n{s.Survivor} is the sole survivor: +{s.Bonus} bonus.\n");
            sb.Append($"\nYour money: {g.OwnMoney}\n\n{(s.Last ? "Final tally coming..." : "Next round starting...")}");
            _text.Text = sb.ToString();
        }
        else if (g.Phase == GamePhase.MatchEnd && g.LastMatchEnd != null)
        {
            var m = g.LastMatchEnd;
            var sb = new StringBuilder($"{m.Winner.ToUpper()} WINS THE MATCH\n\n");
            for (int i = 0; i < m.Names.Length; i++) sb.Append($"{i + 1}. {m.Names[i]}   {m.Money[i]}\n");
            sb.Append("\nReturning to the lobby...");
            _text.Text = sb.ToString();
        }
        if (_panel.Visible && DisplayServer.GetName() != "headless") Input.MouseMode = Input.MouseModeEnum.Visible;
    }
}
