using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Client-side replicated state of the weighing ceremony (everything here is public information once it is sent).</summary>
public class WeighView
{
    public int Round;
    public int[] Peers = Array.Empty<int>();
    public string Stage = "intro";           // intro | reveal | verdict | throw | remain
    public double StageAt;
    public readonly List<(int peer, int weight)> Revealed = new();
    public int CurPeer = -1, CurWeight;       // the pile on the Scale right now
    public float CountSeconds = 1.5f;
    /// <summary>Seconds after the reveal starts until the Goblin's hand has the sack (reach + lift) and the number starts to count; GrabDelay is when the sack leaves the floor.</summary>
    public float CountDelay = 1.5f, GrabDelay = 0.8f, RevealSeconds = 4f;
    public double CurAt;
    public int[] Thrown = Array.Empty<int>();
    public bool FinalTwo, RandomTie;
    public int Winner = -1;
    public int Remaining;
    public float ThrowSeconds = 4f;
    /// <summary>Count-up value of the current pile at this moment.</summary>
    public int ShownWeight(double now) => CountSeconds <= 0.01f ? CurWeight : (int)Math.Round(CurWeight * Math.Clamp((now - CurAt - CountDelay) / CountSeconds, 0.0, 1.0));
}

enum WeighStage { Intro, Reveal, Verdict, Throw, Remain }

/// <summary>
/// The weighing ceremony: server-driven, replicated sequence between two rounds (phase Weighing).
/// Intro (miners on their tribute rings, sacks drop) -> Reveal x N (the Goblin lifts each sack to his palm and the number counts up; heaviest first, lightest last) -> Verdict -> Throw -> Remain card -> next round (or match end).
/// Extension points: <see cref="Weighing.Prepare"/>, <see cref="Weighing.BeforeReveal"/>, <see cref="WeighingContext"/> (pile API), <see cref="EliminatedRole"/>.
/// </summary>
public partial class Game
{
    // ------------------------------------------------------------ client state
    public WeighView Weigh;
    public event Action WeighChanged;
    /// <summary>Raised on every peer when the Goblin should say something: (category, seed, arg0, arg1, mood).</summary>
    public event Action<string, string, string, string, string> GoblinCue;

    // ------------------------------------------------------------ server state
    class WeighRun
    {
        public WeighStage Stage;
        public WeighingContext Ctx;
        public WeighVerdict Verdict;
        public int Index;
    }
    WeighRun _weigh;

    /// <summary>Test/debug: ends the round now (as if the timer ran out).</summary>
    public void DebugEndRound() { if (Net.I.IsServer && Phase == GamePhase.Round) ServerBeginWeighing(); }

    // ============================================================ SERVER
    /// <summary>The timer ended (or everyone died): the lift leaves, the Goblin weighs.</summary>
    void ServerBeginWeighing()
    {
        if (Phase != GamePhase.Round) return;
        Phase = GamePhase.Weighing;
        var contenders = Contenders();
        // the tribute of a miner is the content of their sack at timer end IF they are inside the Hall. Outside: zero, and the sack is emptied (lost).
        foreach (var p in contenders)
        {
            if (!p.Alive || World.Cave.InHall(p.Pos)) continue;
            int lostWeight = p.TributeWeight, lostStones = p.Inv.Sack.Count;
            p.Inv.Sack.Clear(); p.TributeAdj = 0; p.Dirty = true;
            Util.Pass("locked_out", $"name={p.Name} lost_weight={lostWeight} lost_stones={lostStones}");
            ServerNotice(p, "The lift left without you. Your sack is lost and you weigh nothing.");
        }
        foreach (var p in contenders)
            Util.Pass("tribute_counted", $"name={p.Name} weight={p.TributeWeight} stones={p.Inv.Sack.Count} burden={p.Burden:0.#} in_hall={p.Alive && World.Cave.InHall(p.Pos)} alive={p.Alive}");
        foreach (var p in contenders) p.Fire(new SteinEvent { Kind = SteinEventKind.RoundEnd });
        Util.Log($"ROUND: {Round} end");
        if (ForcedWinner != null) { ServerEndMatch(); return; }

        var ctx = new WeighingContext { Round = Round };
        foreach (var p in contenders) ctx.Piles.Add(new WeighPile { Peer = p.Peer, Name = p.Name, Weight = p.TributeWeight });
        Weighing.RaisePrepare(ctx);

        // everyone who is still in the match stands on their tribute ring in the Hall; the sack drops in front of it
        var spots = new Vector3[contenders.Count];
        var sizes = new int[contenders.Count];
        for (int i = 0; i < contenders.Count; i++)
        {
            var p = contenders[i];
            int rank = Array.IndexOf(_roundPeers, p.Peer); if (rank < 0) rank = i;
            spots[i] = World.Cave.RingSpot(rank, Math.Max(1, _roundPeers.Length));
            sizes[i] = SizeClass(ctx.Pile(p.Peer)?.Weight ?? 0);
            p.Pos = spots[i]; p.Alive = true; p.Downed = false; p.Health = ServerPlayer.MaxHealth; p.Dirty = true;
        }
        _weigh = new WeighRun { Stage = WeighStage.Intro, Ctx = ctx };
        _phaseTimer = Config.WeighIntroSeconds;
        Rpc(nameof(ClientWeighBegin), Round, contenders.Select(p => p.Peer).ToArray(), spots, sizes);
        ServerCue("weigh_start", "r" + Round, Round.ToString());
        foreach (var p in Players.Values) SendOwnState(p);
        Util.Log($"WEIGH: begin round={Round} piles={string.Join(",", ctx.Piles.Select(x => x.Name + "=" + x.Weight))}");
    }

    /// <summary>The only thing about a sack that is visible before its reveal: a vague size class 0..4 (the model scales "a bit" with the tribute).</summary>
    static int SizeClass(int weight) => weight <= 0 ? 0 : weight <= 8 ? 1 : weight <= 20 ? 2 : weight <= 40 ? 3 : 4;

    void ServerWeighTick(float dt)
    {
        if (_weigh == null) return;
        _phaseTimer -= dt;
        if (_phaseTimer > 0) return;
        AdvanceWeighing();
    }

    void AdvanceWeighing()
    {
        var w = _weigh;
        // miners who left meanwhile are gone from the Scale
        w.Ctx.Piles.RemoveAll(p => !Players.ContainsKey(p.Peer) || Players[p.Peer].Eliminated);
        switch (w.Stage)
        {
            case WeighStage.Intro:
            case WeighStage.Reveal:
            {
                var next = w.Ctx.Piles.Where(p => !p.Revealed).OrderByDescending(p => p.Weight).ThenBy(p => p.Peer).FirstOrDefault();
                if (next != null)
                {
                    Weighing.RaiseBeforeReveal(w.Ctx, next);
                    next.Revealed = true;
                    w.Stage = WeighStage.Reveal;
                    int left = w.Ctx.Piles.Count(p => !p.Revealed);
                    Util.Log($"WEIGH: reveal #{w.Index} {next.Name} weight={next.Weight} notes={string.Join("|", next.Notes)}");
                    Rpc(nameof(ClientWeighReveal), w.Index++, next.Peer, next.Weight, Config.RevealCountSeconds, left, Config.RevealSeconds);
                    _phaseTimer = Config.RevealSeconds;
                    break;
                }
                // all revealed: the verdict
                var v = Weighing.Resolve(w.Ctx.Piles.Select(p => (p.Peer, p.Weight)).ToList(), n => (int)(_rng.Randi() % (uint)n));
                w.Verdict = v; w.Stage = WeighStage.Verdict;
                Util.Log($"WEIGH: verdict thrown={string.Join(",", v.Thrown.Select(NameOfPeer))} finalTwo={v.FinalTwo} tie={v.RandomTie} winner={(v.Winner >= 0 ? NameOfPeer(v.Winner) : "-")}");
                Rpc(nameof(ClientWeighVerdict), v.Thrown.ToArray(), v.FinalTwo, v.Winner, v.RandomTie);
                _phaseTimer = Config.VerdictSeconds;
                break;
            }
            case WeighStage.Verdict:
            {
                var v = w.Verdict;
                w.Stage = WeighStage.Throw;
                foreach (var peer in v.Thrown)
                {
                    if (!Players.TryGetValue(peer, out var p)) continue;
                    p.Eliminated = true; p.EliminatedRound = Round; p.EliminationSeq = ++_elimSeq;
                    p.Participant = false; p.Alive = false; p.Downed = false;
                    if (p.Inv.Steins.Contains("infinity")) { InfinityTracker.Reset(); InfinityTracker.Spawned = false; }
                    p.Inv.Clear(); p.TributeAdj = 0; p.RecalcStats(); p.Dirty = true;
                    Util.Pass("miner_eliminated", $"name={p.Name} round={Round}");
                    EliminatedRole.OnEliminated(this, p);
                }
                Rpc(nameof(ClientWeighThrow), v.Thrown.ToArray(), Config.ThrowSeconds);
                _phaseTimer = Config.ThrowSeconds + 0.4f;
                break;
            }
            case WeighStage.Throw:
            {
                // every sack is emptied after the weighing (tribute is per round)
                foreach (var p in Players.Values) { p.Inv.Sack.Clear(); p.TributeAdj = 0; p.Dirty = true; }
                int remaining = Contenders().Count;
                if (remaining <= 1) { Util.Pass("final_weighing", $"round={Round}"); ServerEndMatch(); break; }
                w.Stage = WeighStage.Remain;
                Rpc(nameof(ClientWeighRemain), remaining);
                _phaseTimer = Config.RemainSeconds;
                break;
            }
            case WeighStage.Remain:
                _weigh = null;
                ServerStartRound();
                break;
        }
    }

    string NameOfPeer(int peer) => Players.TryGetValue(peer, out var p) ? p.Name : "peer" + peer;

    // ============================================================ CLIENT handlers (all peers, including the host)
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientWeighBegin(int round, int[] peers, Vector3[] spots, int[] sizes)
    {
        if (World == null) return;
        Phase = GamePhase.Weighing;
        Weigh = new WeighView { Round = round, Peers = peers, StageAt = ClientNow };
        var facing = World.Cave.GoblinSeat + new Vector3(0, 0, 4f);   // everyone on a ring looks up at the Goblin
        for (int i = 0; i < peers.Length; i++)
        {
            var d = facing - spots[i];
            var m = SpawnMinerNode(peers[i], spots[i], Net.I.NameOf(peers[i]), "");
            m?.FaceYaw(Mathf.Atan2(-d.X, -d.Z));
            Status[peers[i]] = MinerStatus.Alive; Alive[peers[i]] = true;
        }
        OwnDowned = false;
        HallCeremony.I?.BeginSacks(peers, spots, sizes);
        PhaseChanged?.Invoke(); WeighChanged?.Invoke();
        Util.Pass("weigh_begin", $"round={round} miners={peers.Length} sacks={peers.Length}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientWeighReveal(int index, int peer, int weight, float countSeconds, int left, float revealSeconds)
    {
        if (Weigh == null) return;
        Weigh.Stage = "reveal"; Weigh.StageAt = ClientNow;
        Weigh.Revealed.Add((peer, weight));
        Weigh.CurPeer = peer; Weigh.CurWeight = weight; Weigh.CountSeconds = countSeconds; Weigh.CurAt = ClientNow;
        // the hand reaches for the sack (first quarter), lifts it to the palm (up to 45%), then the number counts up
        Weigh.RevealSeconds = revealSeconds; Weigh.GrabDelay = revealSeconds * 0.25f; Weigh.CountDelay = revealSeconds * 0.45f;
        string name = Net.I.NameOf(peer);
        string cat = weight == 0 ? "reveal_zero" : left == 0 ? "reveal_last" : index == 0 ? "reveal_first" : "reveal_mid";
        Cue(cat, $"{Weigh.Round}:{index}", name, weight.ToString());
        Util.Pass("weigh_reveal", $"index={index} name={name} weight={weight}");
        WeighChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientWeighVerdict(int[] thrown, bool finalTwo, int winner, bool randomTie)
    {
        if (Weigh == null) return;
        Weigh.Stage = "verdict"; Weigh.StageAt = ClientNow;
        Weigh.Thrown = thrown; Weigh.FinalTwo = finalTwo; Weigh.Winner = winner; Weigh.RandomTie = randomTie;
        string names = string.Join(" and ", thrown.Select(Net.I.NameOf));
        if (finalTwo) Cue("final_two", $"{Weigh.Round}", Net.I.NameOf(winner), names);
        else if (thrown.Length > 1) Cue("verdict_tie", $"{Weigh.Round}", names);
        else Cue("verdict", $"{Weigh.Round}", names);
        Util.Pass("weigh_verdict", $"thrown={names} finalTwo={finalTwo} tie={randomTie}");
        WeighChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientWeighThrow(int[] thrown, float seconds)
    {
        if (Weigh == null || World == null) return;
        Weigh.Stage = "throw"; Weigh.StageAt = ClientNow; Weigh.ThrowSeconds = seconds;
        foreach (var peer in thrown)
        {
            World.GetMiner(peer)?.StartThrow(World.Cave.PalmPos, World.Cave.ShaftPos, seconds);
            Status[peer] = MinerStatus.Eliminated; Alive[peer] = false;
        }
        Cue("throw", $"{Weigh.Round}", string.Join(" and ", thrown.Select(Net.I.NameOf)));
        Util.Pass("weigh_throw", $"thrown={string.Join(" and ", thrown.Select(Net.I.NameOf))}");
        WeighChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientWeighRemain(int remaining)
    {
        if (Weigh == null) return;
        Weigh.Stage = "remain"; Weigh.StageAt = ClientNow; Weigh.Remaining = remaining;
        Util.Pass("weigh_remain", $"remaining={remaining}");
        WeighChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientGoblinCue(string category, string seed, string a0, string a1, string mood) => Cue(category, seed, a0, a1, mood);

    void Cue(string category, string seed, string a0 = "", string a1 = "", string mood = "") => GoblinCue?.Invoke(category, seed, a0, a1, mood);

    /// <summary>Called by a thrown Miner when its fall animation ends. The local victim becomes whatever <see cref="EliminatedRole"/> says.</summary>
    public void NotifyThrownDone(int peer)
    {
        if (peer == MyId && World != null) EliminatedRole.ClientEnter(this, World.Cave.ScalePos + new Vector3(0, 4f, 3f));
    }
}
