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
/// (quota total, timer, round summary, announcements) and picks lines deterministically (GoblinLines.Pick), so all peers read the same text.
/// The HUD reads <see cref="Feed"/>; the 3D Goblin listens to GoblinRig.Reaction.
/// </summary>
public partial class GoblinVoice : Node
{
    public static GoblinVoice I;
    public static readonly List<GoblinSaying> Feed = new();
    public static event Action<GoblinSaying> Said;

    int _lastRound = -1, _lastDelivered;
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
        g.OwnChanged += OnOwnChanged;
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
        string msg = f[f.Count - 1];
        int round = Game.I.Round;
        if (msg.StartsWith("The Goblin takes ore")) return; // replaced by deposit lines (see OnOwnChanged)
        if (msg.StartsWith("The Goblin collapses"))
        {
            Say(GoblinLines.Pick("collapse", "r" + round), "angry");
            GoblinRig.React("angry");
            return;
        }
        if (msg.EndsWith(" has perished."))
        {
            string name = msg.Substring(0, msg.Length - " has perished.".Length);
            Say(GoblinLines.Pick("death", round + name, name));
            return;
        }
        if (msg.StartsWith("Every miner is dead")) { Say(GoblinLines.Pick("all_dead", "r" + round), "angry"); GoblinRig.React("angry"); return; }
        Say(msg);
    }

    void OnOwnChanged()
    {
        var g = Game.I;
        if (g.Phase != GamePhase.Round) { _lastDelivered = g.Delivered; return; }
        int d = g.Delivered - _lastDelivered;
        _lastDelivered = g.Delivered;
        if (d <= 0) return;
        bool large = d >= 120;
        Say(GoblinLines.Pick(large ? "deposit_large" : "deposit_small", $"{g.Round}:{g.Delivered}"), "happy");
        GoblinRig.React("happy");
        Sfx.Play(SfxNames.Deposit);
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
                    _lastRound = g.Round; _lastDelivered = 0;
                    _w60 = _w30 = _w10 = false;
                    _roundStartTime = g.TimeLeft;
                    Say(GoblinLines.Pick("round_start", "r" + g.Round, g.Round.ToString()));
                    Sfx.Play(SfxNames.RoundStart);
                }
                break;
            case GamePhase.Summary:
                var s = g.LastSummary; if (s == null) break;
                if (s.QuotaMet) { Say(GoblinLines.Pick("quota_met", "r" + s.Round), "happy"); GoblinRig.React("happy"); }
                else { Say(GoblinLines.Pick("quota_missed", "r" + s.Round), "angry"); GoblinRig.React("angry"); }
                if (s.Poorest != "") Say(GoblinLines.Pick("poorest", "r" + s.Round + s.Poorest, s.Poorest), "angry");
                if (s.Survivor != "") Say(GoblinLines.Pick("survivor", "r" + s.Round + s.Survivor, s.Survivor, UiKit.Money(s.Bonus)));
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
