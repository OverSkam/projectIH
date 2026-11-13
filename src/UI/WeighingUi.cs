using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Overlay of the weighing ceremony (phase Weighing). Reads Game.Weigh (replicated, public by then): the sack in the Goblin's palm with a count-up,
/// the list of sacks weighed so far, the verdict, the throw and the "N miners remain" card. The Goblin's lines show at the bottom.
/// The 3D side (rings, sacks, the number above the palm) is HallCeremony in the world.
/// </summary>
public partial class WeighingUi : CanvasLayer
{
    public static WeighingUi I;
    Control _root;
    Label _title, _sub;
    PanelContainer _card; VBoxContainer _cardBox;
    Label _cardName, _cardNum, _cardNote;
    PanelContainer _listPanel; VBoxContainer _listRows; int _listCount = -1;
    PanelContainer _remainPanel; VBoxContainer _remainBox; string _remainSig = "";
    PanelContainer _speech; Label _speechText; GoblinPortrait _speechFace; double _speechAt = -100;
    Control _victim; Label _victimLabel;
    ColorRect _dim;

    public override void _Ready()
    {
        I = this;
        Layer = 7;
        _root = UiTheme.Apply(new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false });
        UiKit.FullRect(_root);
        AddChild(_root);

        _dim = new ColorRect { Color = new Color(0, 0, 0, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        UiKit.FullRect(_dim); _root.AddChild(_dim);

        // banner
        var top = UiKit.VBox(0);
        top.SetAnchorsPreset(Control.LayoutPreset.CenterTop); top.GrowHorizontal = Control.GrowDirection.Both; top.OffsetTop = 14;
        _root.AddChild(top);
        _title = UiKit.MakeTitle("THE WEIGHING", 44);
        _sub = UiKit.MakeLabel("", 20, UiTheme.Parchment, HorizontalAlignment.Center, 6);
        top.AddChild(_title); top.AddChild(_sub);

        // reveal card (upper centre: the Goblin's palm with the sack stays visible in the middle of the screen)
        _card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.04f, 0.03f, 0.8f), UiTheme.Brass, 10, 2, 28, 10));
        _card.SetAnchorsPreset(Control.LayoutPreset.CenterTop); _card.GrowHorizontal = Control.GrowDirection.Both; _card.OffsetTop = 100;
        _card.CustomMinimumSize = new Vector2(380, 0);
        _root.AddChild(_card);
        _cardBox = UiKit.VBox(0); _card.AddChild(_cardBox);
        _cardName = UiKit.MakeTitle("", 36, UiTheme.Parchment);
        _cardNum = UiKit.MakeTitle("0", 88, UiTheme.Gold);
        _cardNote = UiKit.MakeLabel("", 18, UiTheme.Dim, HorizontalAlignment.Center, 4);
        _cardBox.AddChild(_cardName); _cardBox.AddChild(_cardNum); _cardBox.AddChild(_cardNote);

        // revealed so far (left)
        _listPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _listPanel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.04f, 0.03f, 0.72f), UiTheme.BrassDark, 8, 2, 14, 10));
        _listPanel.SetAnchorsPreset(Control.LayoutPreset.CenterLeft); _listPanel.GrowVertical = Control.GrowDirection.Both; _listPanel.OffsetLeft = 18;
        _listPanel.CustomMinimumSize = new Vector2(240, 0);
        _root.AddChild(_listPanel);
        var lv = UiKit.VBox(4); _listPanel.AddChild(lv);
        lv.AddChild(UiKit.MakeLabel("WEIGHED SO FAR", 14, UiTheme.Brass, HorizontalAlignment.Center, 3));
        _listRows = UiKit.VBox(3); lv.AddChild(_listRows);

        // miners remain card (centre)
        _remainPanel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _remainPanel.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.04f, 0.03f, 0.9f), UiTheme.Gold, 12, 3, 40, 24));
        _remainPanel.SetAnchorsPreset(Control.LayoutPreset.Center); _remainPanel.GrowHorizontal = Control.GrowDirection.Both; _remainPanel.GrowVertical = Control.GrowDirection.Both;
        _remainPanel.CustomMinimumSize = new Vector2(520, 0);
        _root.AddChild(_remainPanel);
        _remainBox = UiKit.VBox(8); _remainPanel.AddChild(_remainBox);

        // goblin speech (bottom)
        _speech = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _speech.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(0.05f, 0.04f, 0.03f, 0.88f), UiTheme.Brass, 10, 2, 16, 10));
        _speech.SetAnchorsPreset(Control.LayoutPreset.CenterBottom); _speech.GrowHorizontal = Control.GrowDirection.Both; _speech.GrowVertical = Control.GrowDirection.Begin; _speech.OffsetBottom = -26;
        _speech.CustomMinimumSize = new Vector2(760, 0);
        _root.AddChild(_speech);
        var sh = UiKit.HBox(14); _speech.AddChild(sh);
        _speechFace = new GoblinPortrait { CustomMinimumSize = new Vector2(70, 70) };
        _speechFace.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        sh.AddChild(_speechFace);
        var sv = UiKit.VBox(0); sh.AddChild(sv);
        sv.AddChild(UiKit.MakeLabel("THE GOBLIN", 13, UiTheme.GoblinGreen, HorizontalAlignment.Left, 3));
        _speechText = UiKit.MakeLabel("", 22, UiTheme.Parchment, HorizontalAlignment.Left, 5);
        _speechText.AutowrapMode = TextServer.AutowrapMode.WordSmart; _speechText.CustomMinimumSize = new Vector2(620, 0);
        sv.AddChild(_speechText);

        // "you are thrown" overlay for the victim
        _victim = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        UiKit.FullRect(_victim); _root.AddChild(_victim);
        _victimLabel = UiKit.MakeTitle("YOU ARE THROWN", 80, UiTheme.Danger);
        _victimLabel.SetAnchorsPreset(Control.LayoutPreset.CenterTop); _victimLabel.GrowHorizontal = Control.GrowDirection.Both; _victimLabel.OffsetTop = 340;
        _victim.AddChild(_victimLabel);

        GoblinVoice.Said += OnSaid;
        Game.I.WeighChanged += () => { _listCount = -1; };
    }

    void OnSaid(GoblinSaying s)
    {
        if (Game.I.Phase != GamePhase.Weighing) return;
        _speechText.Text = "“" + s.Text + "”"; _speechFace.Mood = s.Mood; _speechAt = Time.GetTicksMsec() / 1000.0;
    }

    string N(int peer) => Net.I.NameOf(peer);

    public override void _Process(double delta)
    {
        var g = Game.I; var w = g.Weigh;
        bool show = g.Phase == GamePhase.Weighing && w != null;
        _root.Visible = show;
        if (!show) { _speechAt = -100; return; }
        double now = Time.GetTicksMsec() / 1000.0;
        _title.Text = $"THE WEIGHING  -  ROUND {w.Round}";
        _sub.Text = w.Stage switch
        {
            "intro" => "The lift has left. Everyone stands on their ring; the sacks drop.",
            "reveal" => "Sacks are weighed heaviest first. The lightest comes last.",
            "verdict" => w.FinalTwo ? "The final weighing." : "The Goblin has spoken.",
            "throw" => "Down the shaft.",
            _ => "",
        };

        // the sack in the palm
        bool revealing = w.Stage == "reveal" && w.CurPeer >= 0;
        _card.Visible = revealing || w.Stage == "verdict" || w.Stage == "throw";
        if (revealing)
        {
            _cardName.Text = N(w.CurPeer);
            _cardNum.Text = w.ShownWeight(now).ToString();
            bool counting = now - w.CurAt < w.CountSeconds;
            _cardNum.AddThemeColorOverride("font_color", counting ? UiTheme.Gold : (w.CurWeight == 0 ? UiTheme.Danger : UiTheme.Parchment));
            _cardNote.Text = counting ? "weighing..." : (w.CurWeight == 0 ? "nothing at all" : "");
        }
        else if (w.Stage == "verdict" || w.Stage == "throw")
        {
            string names = string.Join(" and ", w.Thrown.Select(N));
            _cardName.Text = w.FinalTwo ? $"{N(w.Winner).ToUpper()} OUTWEIGHS {names.ToUpper()}" : w.Thrown.Length > 1 ? $"{names.ToUpper()}" : names.ToUpper();
            _cardName.AddThemeFontSizeOverride("font_size", 34);
            _cardNum.Text = w.Stage == "verdict" ? (w.FinalTwo ? "WINS" : "LIGHTEST") : "THROWN";
            _cardNum.AddThemeColorOverride("font_color", w.Stage == "verdict" && w.FinalTwo ? UiTheme.Gold : UiTheme.Danger);
            _cardNum.AddThemeFontSizeOverride("font_size", 60);
            _cardNote.Text = w.RandomTie ? "A tie. The Goblin picked." : "";
        }
        else { _cardName.AddThemeFontSizeOverride("font_size", 36); _cardNum.AddThemeFontSizeOverride("font_size", 88); }

        // list of revealed piles (names + weights; this is the moment they become public)
        _listPanel.Visible = w.Revealed.Count > 0 && w.Stage != "remain";
        if (_listCount != w.Revealed.Count)
        {
            _listCount = w.Revealed.Count;
            foreach (var c in _listRows.GetChildren()) c.QueueFree();
            int i = 1;
            foreach (var (peer, weight) in w.Revealed)
            {
                bool thrown = w.Thrown.Contains(peer) && (w.Stage == "verdict" || w.Stage == "throw");
                var row = UiKit.HBox(8);
                row.AddChild(new HelmetIcon(PlayerColors.Of(peer), 26));
                var n = UiKit.MakeLabel(N(peer), 18, thrown ? UiTheme.Danger : UiTheme.Parchment); n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(n);
                row.AddChild(UiKit.MakeLabel(weight.ToString(), 20, thrown ? UiTheme.Danger : UiTheme.Gold, HorizontalAlignment.Right));
                _listRows.AddChild(row);
                i++;
            }
        }

        // miners remain
        bool remain = w.Stage == "remain";
        _remainPanel.Visible = remain;
        if (remain)
        {
            string sig = w.Remaining + ":" + g.Round;
            if (sig != _remainSig)
            {
                _remainSig = sig;
                foreach (var c in _remainBox.GetChildren()) c.QueueFree();
                _remainBox.AddChild(UiKit.MakeTitle(w.Remaining == 1 ? "1 MINER REMAINS" : $"{w.Remaining} MINERS REMAIN", 54));
                var names = UiKit.HBox(14); names.Alignment = BoxContainer.AlignmentMode.Center;
                foreach (var kv in Net.I.Names.Where(kv => g.Status.GetValueOrDefault(kv.Key) != MinerStatus.Eliminated))
                {
                    var col = UiKit.VBox(2);
                    var hi = new HelmetIcon(PlayerColors.Of(kv.Key), 44) { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
                    col.AddChild(hi);
                    col.AddChild(UiKit.MakeLabel(kv.Value, 16, UiTheme.Parchment, HorizontalAlignment.Center, 4));
                    names.AddChild(col);
                }
                _remainBox.AddChild(names);
                _remainBox.AddChild(UiKit.MakeLabel("The next round begins shortly. The Goblin is still hungry.", 16, UiTheme.Dim, HorizontalAlignment.Center));
            }
        }
        else _remainSig = "";

        // victim overlay: I am being thrown
        bool me = w.Stage == "throw" && w.Thrown.Contains(Net.I.MyId);
        _victim.Visible = me;
        _dim.Color = new Color(0, 0, 0, me ? 0.25f : (remain ? 0.45f : 0f));

        // goblin speech fades after a while
        bool say = now - _speechAt < 7.5;
        _speech.Visible = say;
        if (say) _speech.Modulate = new Color(1, 1, 1, Mathf.Clamp((float)(7.5 - (now - _speechAt)) / 1.0f, 0f, 1f));
    }
}
