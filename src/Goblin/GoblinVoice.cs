using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

public class GoblinSaying
{
    public string Text;
    public double Born;
    public string Mood = "smug"; // smug | happy | angry
}

/// <summary>
/// Turns public match events into Goblin dialogue and reactions. Every peer observes the same public events
/// (server cues, timer, weighing steps, announcements) and picks lines deterministically (GoblinLines.Pick), so all peers read the same text.
/// The HUD reads <see cref="Feed"/>; the 3D Goblin listens to GoblinRig.Reaction.
/// </summary>
public partial class GoblinVoice : Node
{
    public static GoblinVoice I;
    public static readonly List<GoblinSaying> Feed = new();
    public static event Action<GoblinSaying> Said;

    int _lastRound = -1;
    bool _w60, _w30, _w10;
    float _roundStartTime;

    public static void Ensure(Node parent)
    {
        if (I != null && IsInstanceValid(I)) return;
        I = new GoblinVoice { Name = "GoblinVoice" };
        parent.AddChild(I);
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var g = Game.I;
        g.FeedChanged += OnFeed;
        g.GoblinCue += OnCue;
        g.PhaseChanged += OnPhase;
        g.ReturnedToMenu += () => { Feed.Clear(); _lastRound = -1; };
    }

    public static void Say(string text, string mood = "smug")
    {
        if (string.IsNullOrEmpty(text)) return;
        var s = new GoblinSaying { Text = text, Born = Time.GetTicksMsec() / 1000.0, Mood = mood };
        Feed.Add(s);
        while (Feed.Count > 6) Feed.RemoveAt(0);
        Util.Log("GOBLIN: " + text);
        Said?.Invoke(s);
    }

    void OnFeed()
    {
        var f = Game.I.Feed;
        if (f.Count == 0) return;
        Say(f[f.Count - 1]);   // plain announcements (e.g. the Infinity Stein count)
    }

    static string MoodOf(string cat) => cat switch
    {
        "winner" or "reveal_first" => "happy",
        "reveal_last" or "reveal_zero" or "verdict" or "verdict_tie" or "throw" or "final_two" or "all_dead" or "weigh_start" or "downed" or "loiter" => "angry",
        _ => "smug",
    };

    void OnCue(string cat, string seed, string a0, string a1, string mood)
    {
        string text = GoblinLines.Pick(cat, seed, a0, a1);
        if (text == "") return;
        if (mood == "") mood = MoodOf(cat);
        Say(text, mood);
        if (mood == "happy" || mood == "angry") GoblinRig.React(mood);
    }

    void OnPhase()
    {
        var g = Game.I;
        switch (g.Phase)
        {
            case GamePhase.Lobby:
                _lastRound = -1; Feed.Clear(); break;
            case GamePhase.Round:
                if (g.Round != _lastRound)
                {
                    _lastRound = g.Round;
                    _w60 = _w30 = _w10 = false;
                    _roundStartTime = g.TimeLeft;
                    Say(GoblinLines.Pick("round_start", "r" + g.Round, g.Round.ToString()));
                    Sfx.Play(SfxNames.RoundStart);
                }
                break;
            case GamePhase.MatchEnd:
                var m = g.LastMatchEnd; if (m == null) break;
                Say(GoblinLines.Pick("winner", "m" + m.Winner, m.Winner), "happy");
                GoblinRig.React("happy");
                break;
        }
    }

    public override void _Process(double delta)
    {
        var g = Game.I;
        if (g.Phase != GamePhase.Round || g.TimeLeft <= 0) return;
        float t = g.TimeLeft;
        if (!_w60 && t <= 60f) { _w60 = true; if (_roundStartTime > 62f) Warn("warn60"); }
        if (!_w30 && t <= 30f) { _w30 = true; if (_roundStartTime > 32f) Warn("warn30"); }
        if (!_w10 && t <= 10f) { _w10 = true; if (_roundStartTime > 12f) Warn("warn10"); }
    }

    void Warn(string cat)
    {
        Say(GoblinLines.Pick(cat, "r" + Game.I.Round), cat == "warn10" ? "angry" : "smug");
        Sfx.Play(SfxNames.Warning);
    }
}
