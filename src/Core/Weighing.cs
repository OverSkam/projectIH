using System;
using System.Collections.Generic;
using System.Linq;

namespace AppStein;

/// <summary>One miner's pile on the Scale (server only). Weight starts as the miner's tribute and may be changed by weighing hooks.</summary>
public class WeighPile
{
    public int Peer;
    public string Name = "";
    public int Weight;
    /// <summary>Why the weight was changed (hooks append here; logged, not shown).</summary>
    public readonly List<string> Notes = new();
    public bool Revealed;
}

/// <summary>
/// The pile-modification API handed to weighing hooks (server only). Later steps (Liar swap, gifting, Pact stone, Glutton, Parasite, Hatchling...)
/// subscribe to <see cref="Weighing.Prepare"/> / <see cref="Weighing.BeforeReveal"/> and change piles ONLY through these methods.
/// </summary>
public class WeighingContext
{
    public int Round;
    public readonly List<WeighPile> Piles = new();

    public WeighPile Pile(int peer) => Piles.FirstOrDefault(p => p.Peer == peer);

    public void Add(int peer, int delta, string reason)
    {
        var p = Pile(peer); if (p == null) return;
        p.Weight = Math.Max(0, p.Weight + delta);
        p.Notes.Add($"{reason}:{delta:+#;-#;0}");
    }

    public void Set(int peer, int weight, string reason)
    {
        var p = Pile(peer); if (p == null) return;
        p.Notes.Add($"{reason}:set {p.Weight}->{weight}");
        p.Weight = Math.Max(0, weight);
    }

    /// <summary>Exchange the contents (weights) of two piles. Owners and names stay where they are.</summary>
    public void Swap(int peerA, int peerB, string reason)
    {
        var a = Pile(peerA); var b = Pile(peerB);
        if (a == null || b == null || a == b) return;
        (a.Weight, b.Weight) = (b.Weight, a.Weight);
        a.Notes.Add($"{reason}:swapped with {b.Peer}"); b.Notes.Add($"{reason}:swapped with {a.Peer}");
    }
}

public class WeighVerdict
{
    public readonly List<int> Thrown = new();
    /// <summary>Set when exactly one miner is left after the throw (the match ends with them). -1 otherwise.</summary>
    public int Winner = -1;
    public bool FinalTwo, RandomTie;
}

/// <summary>Weighing rules (pure functions) and the hook points of the ceremony.</summary>
public static class Weighing
{
    /// <summary>Server: once per weighing, after the cave lock-out and RoundEnd events, before the first reveal. Change piles here (swaps, gifts, pacts).</summary>
    public static event Action<WeighingContext> Prepare;
    /// <summary>Server: per miner right before their pile is revealed (suspense order: heaviest first). The pile and all unrevealed piles may still change.</summary>
    public static event Action<WeighingContext, WeighPile> BeforeReveal;

    public static void RaisePrepare(WeighingContext c) => Prepare?.Invoke(c);
    public static void RaiseBeforeReveal(WeighingContext c, WeighPile p) => BeforeReveal?.Invoke(c, p);

    /// <summary>
    /// Elimination rules. Final two: the heavier pile wins the match (tie: random). Otherwise the lightest miner is thrown; all tied lightest miners
    /// are thrown, unless that would leave nobody (everyone tied): then one of them, at random. <paramref name="pick"/>(n) returns a random index in [0, n).
    /// </summary>
    public static WeighVerdict Resolve(IReadOnlyList<(int peer, int weight)> piles, Func<int, int> pick)
    {
        var v = new WeighVerdict();
        var list = piles.OrderBy(p => p.peer).ToList();
        int c = list.Count;
        if (c <= 1) { if (c == 1) v.Winner = list[0].peer; return v; }
        if (c == 2)
        {
            v.FinalTwo = true;
            var (a, b) = (list[0], list[1]);
            if (a.weight == b.weight) { v.RandomTie = true; if (pick(2) == 0) (a, b) = (b, a); /* a is now the loser */ }
            else if (a.weight > b.weight) (a, b) = (b, a);
            // a = lighter (or random loser on a tie), b = winner
            v.Thrown.Add(a.peer); v.Winner = b.peer;
            return v;
        }
        int min = list.Min(p => p.weight);
        var tied = list.Where(p => p.weight == min).ToList();
        if (tied.Count == c) { v.RandomTie = true; v.Thrown.Add(tied[pick(tied.Count)].peer); }
        else { v.RandomTie = tied.Count > 1; foreach (var t in tied) v.Thrown.Add(t.peer); }
        var left = list.Where(p => !v.Thrown.Contains(p.peer)).ToList();
        if (left.Count == 1) v.Winner = left[0].peer;
        return v;
    }

    /// <summary>Headless self-check of the elimination rules: <c>godot --headless --path . -- --weighcheck</c>.</summary>
    public static int SelfCheck()
    {
        int bad = 0;
        void Chk(string name, bool ok) { if (ok) Util.Pass("weighcheck_" + name); else { Util.Fail("weighcheck_" + name); bad++; } }
        var rnd = new Random(5);
        Func<int, int> pk = n => rnd.Next(n);
        var v = Resolve(new[] { (1, 10), (2, 4), (3, 7), (4, 9) }, pk);
        Chk("lightest_thrown", v.Thrown.SequenceEqual(new[] { 2 }) && v.Winner == -1 && !v.FinalTwo);
        v = Resolve(new[] { (1, 10), (2, 4), (3, 4), (4, 9) }, pk);
        Chk("tied_lightest_all_thrown", v.Thrown.OrderBy(x => x).SequenceEqual(new[] { 2, 3 }) && v.RandomTie && v.Winner == -1);
        v = Resolve(new[] { (1, 10), (2, 4), (3, 4) }, pk);
        Chk("tie_leaving_one_ends_match", v.Thrown.Count == 2 && v.Winner == 1);
        v = Resolve(new[] { (1, 0), (2, 0), (3, 0) }, pk);
        Chk("all_tied_one_random", v.Thrown.Count == 1 && v.RandomTie && v.Winner == -1);
        v = Resolve(new[] { (1, 3), (2, 8) }, pk);
        Chk("final_two_heavier_wins", v.FinalTwo && v.Winner == 2 && v.Thrown.SequenceEqual(new[] { 1 }));
        v = Resolve(new[] { (1, 8), (2, 3) }, pk);
        Chk("final_two_heavier_wins_b", v.FinalTwo && v.Winner == 1 && v.Thrown.SequenceEqual(new[] { 2 }));
        int w1 = 0; for (int i = 0; i < 40; i++) { v = Resolve(new[] { (1, 5), (2, 5) }, pk); if (v.Winner == 1) w1++; if (v.Thrown.Count != 1 || v.Thrown[0] == v.Winner) bad++; }
        Chk("final_two_tie_random", w1 > 3 && w1 < 37 && bad == 0);
        v = Resolve(new[] { (7, 1) }, pk);
        Chk("single_miner_wins", v.Winner == 7 && v.Thrown.Count == 0);
        var ctx = new WeighingContext(); ctx.Piles.Add(new WeighPile { Peer = 1, Weight = 5 }); ctx.Piles.Add(new WeighPile { Peer = 2, Weight = 9 });
        ctx.Swap(1, 2, "liar"); ctx.Add(1, -20, "x");
        Chk("pile_api", ctx.Pile(1).Weight == 0 && ctx.Pile(2).Weight == 5);
        return bad;
    }
}

/// <summary>`-- --weighcheck`: runs <see cref="Weighing.SelfCheck"/> headless and exits non-zero on failure.</summary>
[ContentModule]
public static class WeighTools
{
    public static void Register()
    {
        if (!Util.HasArg("weighcheck")) return;
        int bad = Weighing.SelfCheck();
        var tree = Godot.Engine.GetMainLoop() as Godot.SceneTree;
        if (tree != null) tree.ProcessFrame += () => tree.Quit(bad == 0 ? 0 : 1);
    }
}
