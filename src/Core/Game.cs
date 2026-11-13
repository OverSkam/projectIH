using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

public enum GamePhase { Lobby, Round, Weighing, MatchEnd }

/// <summary>Public per-miner status (scoreboard, HUD counts).</summary>
public enum MinerStatus { Alive, Downed, Dead, Eliminated }

public class MatchEndData
{
    /// <summary>Placement order: winner first, then the last miner thrown, ... Names of everyone still connected.</summary>
    public string[] Names; public int[] ThrownRound; public string Winner;
}

/// <summary>
/// Autoload: match + round state machine (server-authoritative) and all in-round RPCs.
/// Conventions: Request*/Report* = client -> server (AnyPeer, validated). Client* = server -> clients (Authority).
/// Private per-miner data (tribute, sack, burden, inventory, oil) only ever leaves the server via RpcId(owner, ClientOwnState).
/// </summary>
public partial class Game : Node
{
    public static Game I;

    // ------------------------------------------------------------ shared / client-visible state
    public MatchConfig Config = MatchConfig.Default;
    public GamePhase Phase = GamePhase.Lobby;
    public World World;
    public int Round, TotalRounds;
    public float TimeLeft;
    /// <summary>Length of this round in seconds (the HUD dial / fuse and the Hall candle show TimeLeft / RoundTotal).</summary>
    public float RoundTotal = 1f;
    /// <summary>Public: whether a miner is up (alive or downed) this round.</summary>
    public readonly Dictionary<int, bool> Alive = new();
    /// <summary>Public: alive / downed / dead / eliminated per peer.</summary>
    public readonly Dictionary<int, MinerStatus> Status = new();
    public MatchEndData LastMatchEnd;
    /// <summary>Miners still in the match (not eliminated).</summary>
    public int Remaining { get { int n = 0; foreach (var id in Net.I.Names.Keys) if (Status.GetValueOrDefault(id) != MinerStatus.Eliminated) n++; return n; } }

    // private (own) state, only filled from ClientOwnState
    public int OwnTribute;
    /// <summary>Own burden (effective, Packmule applied): drives the movement speed multiplier, sprint drain and the HUD sack readout.</summary>
    public float OwnBurden;
    public float OwnOil, OwnOilMax = 240f;
    /// <summary>Public lantern state of the local miner mirrored for the HUD (the oil level itself is private).</summary>
    public bool OwnDowned; public double OwnDownedUntil;
    public float OwnHealth = 100f;
    public Inventory OwnInv = new();
    public Miner LocalMiner;
    /// <summary>Public: the miners of this round in peer order (one tribute ring each; the cave is sized by their number).</summary>
    public int[] RoundPeers = Array.Empty<int>();
    public Ghost LocalGhost;
    public bool LocalWasKilled;

    public readonly List<string> Feed = new();

    public event Action PhaseChanged, OwnChanged, FeedChanged, ReturnedToMenu;

    // test counters
    int _ownStates, _foreignStates;
    bool _burdenSeen;

    // ------------------------------------------------------------ server-only state
    public readonly Dictionary<int, ServerPlayer> Players = new();
    readonly RandomNumberGenerator _rng = new();
    double _roundLeft, _phaseTimer, _syncTimer;
    int _seed;
    bool _autoQuitScheduled;
    int _elimSeq;
    /// <summary>The miners of this round in peer order (tribute rings, cave size). Set at round start, sent to late joiners with the snapshot.</summary>
    int[] _roundPeers = Array.Empty<int>();

    static double Now => Time.GetTicksMsec() / 1000.0;
    int MyId => Net.I.MyId;

    public override void _Ready()
    {
        I = this;
        LoadContentModules();
        _rng.Randomize();
        Net.I.PeerRegistered += OnPeerRegistered;
        Net.I.PeerLeft += OnPeerLeft;
        Net.I.ServerLost += OnServerLost;
        ProcessMode = ProcessModeEnum.Always;
    }

    // ============================================================ SERVER: match flow
    ServerPlayer GetPlayer(int peer)
    {
        if (!Players.TryGetValue(peer, out var p)) Players[peer] = p = new ServerPlayer { Peer = peer, Name = Net.I.NameOf(peer) };
        p.Name = Net.I.NameOf(peer);
        return p;
    }

    public bool CanStart => Net.I.IsServer && Phase == GamePhase.Lobby && (Net.I.Names.Count >= 2 || PresetConfig().AllowSolo);

    MatchConfig PresetConfig()
    {
        var c = MatchConfig.FromName(Util.Arg("preset", "default"));
        if (float.TryParse(Util.Arg("roundsecs", ""), out var rs) && rs > 0) { c.BaseRoundSeconds = rs; c.RoundSecondsGrowth = 0; }   // test/screenshot only
        return c;
    }

    /// <summary>Test only: --tribute=Alice=40,Host=6 sets the tribute at every round start (what the miner then collects adds to it).</summary>
    void ApplyDebugTribute(ServerPlayer p)
    {
        foreach (var tok in Util.Arg("tribute", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = tok.IndexOf('=');
            if (eq > 0 && tok.Substring(0, eq) == p.Name && int.TryParse(tok.Substring(eq + 1), out var w)) p.TributeWeight = w;
        }
    }

    /// <summary>Miners still in the match (not eliminated, still connected), by peer id.</summary>
    public List<ServerPlayer> Contenders() =>
        Players.Values.Where(p => !p.Eliminated && Net.I.Names.ContainsKey(p.Peer)).OrderBy(p => p.Peer).ToList();

    public void ServerStartMatch()
    {
        if (!CanStart) return;
        Config = PresetConfig();
        Util.Log($"MATCH: start preset={Config.Name} players={Net.I.Names.Count}");
        Players.Clear();
        foreach (var id in Net.I.Names.Keys)
        {
            var p = GetPlayer(id);
            p.Inv.Sack.Clear(); p.TributeAdj = 0; p.Eliminated = false; p.Initialized = false; p.EliminatedRound = 0;
        }
        Round = 0; ForcedWinner = null; _elimSeq = 0; InfinityTracker.NewMatch();
        TotalRounds = MatchConfig.RoundsFor(Net.I.Names.Count);
        Rpc(nameof(ClientMatchStart), TotalRounds);
        ServerStartRound();
    }

    void ServerStartRound()
    {
        var parts = Contenders();
        bool soloTest = Config.AllowSolo && Round == 0 && parts.Count == 1;   // debug presets may run a one-miner match (screenshots)
        if (parts.Count <= 1 && !soloTest) { ServerEndMatch(); return; }
        Round++;
        Phase = GamePhase.Round;
        _roundLeft = Config.RoundSeconds(Round);
        _syncTimer = 0;
        _seed = (int)(_rng.Randi() & 0x7fffffff);
        if (int.TryParse(Util.Arg("seedbase", ""), out var sb)) _seed = sb * 131 + Round * 17; // test: reproducible caves
        foreach (var p in Players.Values) p.Participant = false;
        var fresh = new List<ServerPlayer>();
        foreach (var p in parts)
        {
            p.Participant = true; p.Alive = true; p.Downed = false;
            if (!p.Initialized)
            {
                // Match start: start kit + one random Common stein.
                p.Initialized = true;
                p.Inv.Clear(); p.Inv.GiveStartKit();
                p.Inv.Steins.Add(Steins.RandomCommon(_rng));
                fresh.Add(p);
            }
            else if (p.NeedsKit) { p.Inv.Clear(); p.Inv.GiveStartKit(); }   // died or was locked out: back with only pickaxe + lantern
            p.NeedsKit = false;
            p.Health = ServerPlayer.MaxHealth;
            p.Oil = Config.LanternOilSeconds; p.LanternOn = true;
            p.Inv.Sack.Clear(); p.TributeAdj = 0; p.KillsThisRound = 0;
            ApplyDebugTribute(p);
            p.RecalcStats();
        }
        var elim = Players.Values.Where(p => p.Eliminated).Select(p => p.Peer).ToArray();
        _roundPeers = parts.Select(p => p.Peer).ToArray();
        _hallTime.Clear();
        Rpc(nameof(ClientBeginRound), Round, _seed, (float)_roundLeft, elim, TotalRounds, _roundPeers);
        PopulateWorld();
        int i = 0;
        foreach (var p in parts)
        {
            var pos = World.Cave.MinerSpawns[i++ % World.Cave.MinerSpawns.Count];
            p.Pos = pos;
            Rpc(nameof(ClientSpawnMiner), p.Peer, pos, p.Name, p.Inv.Held);
            p.Fire(new SteinEvent { Kind = SteinEventKind.RoundStart });
            p.Dirty = true;
        }
        foreach (var p in Players.Values.Where(p => p.Eliminated && Net.I.Names.ContainsKey(p.Peer))) EliminatedRole.OnRoundStart(this, p);
        // (the starting stein is announced by the HUD tip at round start)
        Util.Log($"ROUND: {Round} start miners={parts.Count} time={_roundLeft}");
    }

    void PopulateWorld()
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)_seed };
        ServerSpawnObject("goblin", World.Cave.GoblinPos, "");
        foreach (var room in World.Cave.Rooms)
            foreach (var m in room.Markers)
            {
                if (m.Kind == "vein")
                {
                    string ore = Ores.Pick(rng, room.Depth, Round);
                    float hp = Ores.WeightOf(ore) >= 7 ? 4 : 3;
                    ServerSpawnObject("vein", m.Pos, VeinObj.MakeData(ore, hp, hp));
                }
                else if (m.Kind == "ore")
                    ServerSpawnObject("pickup", m.Pos + new Vector3(0, 0.4f, 0), "ore:" + Ores.Pick(rng, room.Depth, Round));
            }
        RunMarkerSpawners(rng);
    }

    void ServerEndMatch()
    {
        Phase = GamePhase.MatchEnd;
        _weigh = null;
        var present = Players.Values.Where(p => Net.I.Names.ContainsKey(p.Peer)).ToList();
        var alive = present.Where(p => !p.Eliminated).ToList();
        string winner = ForcedWinner ?? (alive.Count > 0 ? alive.OrderBy(p => p.Peer).First().Name : "");
        // placement: the winner, then the most recently thrown first
        var list = present.OrderByDescending(p => p.Name == winner).ThenByDescending(p => p.EliminatedRound).ThenByDescending(p => p.EliminationSeq).ToList();
        foreach (var p in list) Util.Log($"FINAL: peer={p.Peer} name={p.Name} thrown_round={(p.Name == winner ? 0 : p.EliminatedRound)}");
        Util.Log($"WINNER={winner}");
        if (ForcedWinner != null) Util.Pass("infinity_match_end", $"winner={winner}");
        else if (winner != "") Util.Pass("winner_server", $"winner={winner} rounds={Round}");
        _phaseTimer = Config.MatchEndSeconds;
        Rpc(nameof(ClientMatchEnd), list.Select(p => p.Name).ToArray(), list.Select(p => p.Name == winner ? 0 : p.EliminatedRound).ToArray(), winner);
    }

    void ServerReturnToLobby()
    {
        Phase = GamePhase.Lobby;
        Players.Clear();
        Rpc(nameof(ClientReturnLobby));
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        if (!Net.I.IsServer) return;
        switch (Phase)
        {
            case GamePhase.Round:
                _roundLeft -= dt;
                _syncTimer -= dt;
                if (_syncTimer <= 0) { _syncTimer = 1; Rpc(nameof(ClientRoundTime), (float)Math.Max(0, _roundLeft)); }
                foreach (var p in Players.Values.ToArray())
                {
                    if (!p.Participant) continue;
                    p.Tick(dt);
                    if (p.Dirty && Now - p.LastOwnSend > 0.1) SendOwnState(p);
                }
                TickLoiter(dt);
                if (_roundLeft <= 0) ServerBeginWeighing();
                break;
            case GamePhase.Weighing:
                foreach (var p in Players.Values) if (p.Dirty && Now - p.LastOwnSend > 0.1) SendOwnState(p);
                ServerWeighTick(dt);
                break;
            case GamePhase.MatchEnd:
                _phaseTimer -= dt;
                if (_phaseTimer <= 0) ServerReturnToLobby();
                break;
        }
    }

    // The Goblin barks at miners who hang around in the Hall while the round runs (public cue, every peer picks the same line).
    readonly Dictionary<int, float> _hallTime = new();
    double _loiterCool; int _loiterSeq;
    public const float LoiterSeconds = 28f;
    void TickLoiter(float dt)
    {
        if (World?.Cave == null || _roundLeft < 75) return;
        foreach (var p in Players.Values)
        {
            if (!p.Participant || !p.Alive || p.Downed) { _hallTime.Remove(p.Peer); continue; }
            float t = _hallTime.GetValueOrDefault(p.Peer);
            t = World.Cave.InHall(p.Pos) ? t + dt : 0f;
            if (t >= LoiterSeconds && Now > _loiterCool)
            {
                _loiterCool = Now + 14; t = -20f;
                ServerCue("loiter", $"{Round}:{p.Peer}:{++_loiterSeq}", p.Name);
                Util.Pass("goblin_loiter_bark", $"name={p.Name} round={Round}");
            }
            _hallTime[p.Peer] = t;
        }
    }

    /// <summary>Every miner still in the cave is dead: the Goblin ends the round early.</summary>
    void CheckAllDead()
    {
        if (Phase != GamePhase.Round) return;
        if (!Players.Values.Any(p => p.Participant && p.Alive))
        {
            ServerCue("all_dead", "r" + Round);
            ServerAnnounce("Every miner is dead. The Goblin ends the round.");
            ServerBeginWeighing();
        }
    }

    void OnPeerRegistered(int id)
    {
        var p = GetPlayer(id);
        if (Phase == GamePhase.Lobby) return;
        // Mid-match joiner: never was a miner. Spectates as an eliminated player for the rest of the match.
        p.Participant = false; p.Alive = false; p.Eliminated = true; p.EliminatedRound = Round;
        p.EliminationSeq = ++_elimSeq;
        RpcId(id, nameof(ClientAnnounce), "A match is in progress. You watch as a ghost.");
        RpcId(id, nameof(ClientMatchStart), TotalRounds);
        if (Phase == GamePhase.Round) ServerSendSnapshot(id);
    }

    void ServerSendSnapshot(int peer)
    {
        var elim = Players.Values.Where(p => p.Eliminated).Select(p => p.Peer).ToArray();
        RpcId(peer, nameof(ClientBeginRound), Round, _seed, (float)_roundLeft, elim, TotalRounds, _roundPeers);
        foreach (var o in World.Objects.Values) RpcId(peer, nameof(ClientSpawnObject), o.Id, o.Kind, o.Position, o.Data);
        foreach (var p in Players.Values)
            if (p.Participant && p.Alive && p.Peer != peer)
            {
                RpcId(peer, nameof(ClientSpawnMiner), p.Peer, p.Pos, p.Name, p.Inv.Held);
                if (p.Downed) RpcId(peer, nameof(ClientDowned), p.Peer, true, p.DownedLeft);
            }
        foreach (var p in Players.Values) if (p.Participant && !p.Alive) RpcId(peer, nameof(ClientStatus), p.Peer, (int)MinerStatus.Dead);
    }

    void OnPeerLeft(int id)
    {
        if (!Players.TryGetValue(id, out var p)) return;
        Util.Log($"GAME: peer {id} ({p.Name}) left");
        bool wasContender = !p.Eliminated && Phase is GamePhase.Round or GamePhase.Weighing;
        if (p.Alive && Phase == GamePhase.Round) KillMiner(p, null, "left");   // drops everything in a lootable bag, Death event resets the Infinity counter
        else if (p.Inv.Steins.Contains("infinity")) { InfinityTracker.Reset(); InfinityTracker.Spawned = false; }   // left between rounds: the stein is gone, a new one may appear
        p.Participant = false;
        if (!p.Eliminated) { p.Eliminated = true; p.EliminatedRound = Round; p.EliminationSeq = ++_elimSeq; }
        Players.Remove(id);
        Rpc(nameof(ClientRemoveMiner), id);
        if (wasContender)
        {
            Util.Pass("disconnect_eliminated", $"peer={id} name={p.Name} round={Round}");
            ServerAnnounce($"{p.Name} has left the mine for good.");
            Rpc(nameof(ClientStatus), id, (int)MinerStatus.Eliminated);
            CheckMatchOver();
        }
        CheckAllDead();
    }

    /// <summary>If at most one miner remains at any moment, the match ends with them as the winner.</summary>
    void CheckMatchOver()
    {
        if (Phase != GamePhase.Round && Phase != GamePhase.Weighing) return;
        if (Contenders().Count <= 1)
        {
            Util.Pass("last_miner_standing", $"round={Round}");
            ServerEndMatch();
        }
    }

    // ============================================================ SERVER: world mutation helpers
    public int ServerSpawnObject(string kind, Vector3 pos, string data)
    {
        int id = World.NextObjectId++;
        Rpc(nameof(ClientSpawnObject), id, kind, pos, data);
        return id;
    }
    public void ServerSetObjectData(int id, string data) => Rpc(nameof(ClientObjectData), id, data);
    public void ServerDespawnObject(int id) => Rpc(nameof(ClientDespawnObject), id);

    /// <summary>A miner dies in the cave (not an elimination): everything they carry drops in a bag (or becomes a Midas statue), they haunt as a ghost until next round.</summary>
    public void KillMiner(ServerPlayer v, ServerPlayer killer, string cause)
    {
        if (!v.Alive) return;
        bool wasDowned = v.Downed;
        v.Alive = false; v.Downed = false; v.Health = 0; v.NeedsKit = true;
        v.Fire(new SteinEvent { Kind = SteinEventKind.Death, Other = killer });
        if (killer != null) { killer.KillsThisRound++; killer.Fire(new SteinEvent { Kind = SteinEventKind.Kill, Other = v }); }
        var items = v.Inv.ToItemList();
        bool statue = killer != null && killer != v && killer.Inv.Steins.Contains("midas");
        if (statue)
        {
            ServerSpawnObject("pickup", v.Pos + new Vector3(0, 0.4f, 0), "ore:statue");
            Util.Pass("midas_statue", $"victim={v.Name} killer={killer.Name} dropped_bag=0");
        }
        else if (items.Count > 0)
        {
            int id = ServerSpawnObject("bag", v.Pos, "");
            if (World.Objects.TryGetValue(id, out var o) && o is BagObj bag) bag.Items = items;
        }
        v.Inv.Clear(); v.RecalcStats();
        Util.Pass("death_bag_dropped", $"victim={v.Name} cause={cause} items={items.Count} downed={wasDowned}");
        Rpc(nameof(ClientMinerDied), v.Peer);
        ServerCue("death", $"{Round}{v.Name}", v.Name);
        SendOwnState(v);
        CheckAllDead();
    }

    /// <summary>A power stein is stuffed into the sack (hold G on the selected stein slot): the power is gone for good, it counts as tribute (Common 6, Rare 12, Legendary 20, burden 1).
    /// The Infinity Stein cannot be stuffed.</summary>
    public bool ServerStuffStein(ServerPlayer p, int slot)
    {
        if (slot < 0 || slot >= p.Inv.Steins.Count) return false;
        string id = p.Inv.Steins[slot];
        var def = Steins.Get(id);
        if (def == null) return false;
        if (id == "infinity") { ServerNotice(p, "The Infinity Stein cannot be stuffed into a sack."); return false; }
        p.Inv.Steins.RemoveAt(slot);
        if (p.Inv.SteinSel >= p.Inv.Steins.Count) p.Inv.SteinSel = 0;
        ItemsServer.OnSteinLeft(p, id);
        string stuffed = Ores.StuffedId(def.Rarity);
        p.Inv.Sack.Add(stuffed);
        p.RecalcStats();
        p.Fire(new SteinEvent { Kind = SteinEventKind.Fed, Amount = Ores.WeightOf(stuffed) });
        Util.Pass("stein_stuffed", $"peer={p.Peer} name={p.Name} stein={id} tribute_added={Ores.WeightOf(stuffed)} tribute={p.TributeWeight} burden={p.Burden:0.#}");
        ServerNotice(p, $"You stuff the {def.Name} into your sack. Its power is gone; it counts {Ores.WeightOf(stuffed)}.");
        SendOwnState(p);
        return true;
    }

    void SendOwnState(ServerPlayer p)
    {
        p.Dirty = false; p.LastOwnSend = Now;
        if (!Net.I.Names.ContainsKey(p.Peer)) return;
        // HIDDEN INFORMATION: only the owner ever receives tribute weight / inventory / oil.
        RpcId(p.Peer, nameof(ClientOwnState), p.Peer, p.TributeWeight, p.Inv.Encode(), (int)Math.Ceiling(p.Health), p.Stats.SpeedMul, p.Stats.JumpMul, p.Stats.NoFallDamage, p.Burden, p.Oil, Config.LanternOilSeconds);
    }

    /// <summary>Server: the Goblin says something. Every peer picks the same line from (category, seed).</summary>
    public void ServerCue(string category, string seed, string a0 = "", string a1 = "", string mood = "") =>
        Rpc(nameof(ClientGoblinCue), category, seed, a0, a1, mood);

    void SyncHeld(ServerPlayer p) => Rpc(nameof(ClientHeld), p.Peer, p.Inv.Held);

    /// <summary>A miner that may act: alive, not downed, round in progress.</summary>
    public bool CanAct(ServerPlayer p) => p != null && p.Alive && !p.Downed && Phase == GamePhase.Round;

    ServerPlayer Sender()
    {
        int s = Multiplayer.GetRemoteSenderId();
        if (s == 0) s = MyId;
        return Players.TryGetValue(s, out var p) ? p : null;
    }

    // ============================================================ client -> server requests
    // ---- local helper wrappers used by Miner/UI
    public void SendState(Vector3 pos, float yaw, float pitch, int flags) => RpcId(1, nameof(ReportState), pos, yaw, pitch, flags);
    public void SendAttack(Vector3 origin, Vector3 dir) => RpcId(1, nameof(RequestAttack), origin, dir);
    public void SendInteract(int id) => RpcId(1, nameof(RequestInteract), id);
    public void SendSelectUtil(int slot) => RpcId(1, nameof(RequestSelectUtil), slot);
    public void SendDrop(int utilSlot) => RpcId(1, nameof(RequestDrop), utilSlot);
    public void ReportFall(float speed) => RpcId(1, nameof(ReportFallRpc), speed);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    void ReportState(Vector3 pos, float yaw, float pitch, int flags)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null || !p.Alive || (Phase != GamePhase.Round && Phase != GamePhase.Weighing)) return;
        p.Pos = pos; p.Yaw = yaw; p.Pitch = pitch;
        ItemsServer.OnState(p, flags);
        foreach (var other in Net.I.Names.Keys)
        {
            if (other == p.Peer) continue;
            if (other == 1) World?.GetMiner(p.Peer)?.ApplyNetState(pos, yaw, pitch, flags);
            else RpcId(other, nameof(ClientState), p.Peer, pos, yaw, pitch, flags);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestAttack(Vector3 origin, Vector3 dir)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (!CanAct(p) || Now < p.NextAttack) return;
        var def = Utilities.Get(p.Inv.Held);
        bool pick = def?.IsPickaxe == true;
        p.NextAttack = Now + (pick ? 0.75 : 0.38);
        if (origin.DistanceTo(p.Pos + new Vector3(0, 1.6f, 0)) > 3f || dir.LengthSquared() < 0.5f) return;
        dir = dir.Normalized();
        Rpc(nameof(ClientSwing), p.Peer);
        float reach = pick ? 3.0f : 2.3f;
        var me = World.GetMiner(p.Peer);
        var q = Queries.Ray(origin, origin + dir * reach, 1 | 2 | 4);
        if (me != null) q.Exclude = new Godot.Collections.Array<Rid> { me.GetRid() };
        var hit = World.GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count == 0) return;
        var col = hit["collider"].As<GodotObject>();
        if (col is Miner vm)
        {
            if (!Players.TryGetValue(vm.PeerId, out var v) || !v.Alive) return;
            if (World.Cave.InHall(v.Pos) || World.Cave.InHall(p.Pos)) return; // no fighting inside the Hall
            float dmg = (pick ? def.MeleeDamage : 5f) * p.Stats.MeleeDamageMul;
            var kb = dir with { Y = 0 };
            kb = kb.Normalized() * (pick ? 4f : 9f) + new Vector3(0, pick ? 1f : 3f, 0);
            RpcId(v.Peer, nameof(ClientKnockback), kb);
            float dealt = v.TakeDamage(dmg, p, "melee");
            Util.Pass("damage_applied", $"attacker={p.Name} victim={v.Name} dmg={dealt:0.#} hp={v.Health:0.#} roundLeft={_roundLeft:0.0}");
        }
        else if (col is NetObject o)
        {
            o.ServerHit(p, p.Stats.MiningSpeedMul, pick);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestInteract(int objId)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (!CanAct(p)) return;
        if (!World.Objects.TryGetValue(objId, out var o)) return;
        if (p.Pos.DistanceTo(o.GlobalPosition) > o.InteractRange + 1.0f) return;
        o.ServerInteract(p);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestSelectUtil(int slot)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (!CanAct(p) || slot < 0 || slot >= Inventory.UtilSlots) return;
        p.Inv.Selected = slot; p.Dirty = true;
        SyncHeld(p);
    }

    /// <summary>Drops a utility from a slot. Stones can NEVER be dropped: the sack is bottomless and nothing comes out of it.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestDrop(int utilSlot)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (!CanAct(p) || utilSlot < 0 || utilSlot >= Inventory.UtilSlots || string.IsNullOrEmpty(p.Inv.Utils[utilSlot])) return;
        string item = "util:" + p.Inv.Utils[utilSlot]; p.Inv.Utils[utilSlot] = null;
        p.Dirty = true;
        var fwd = new Vector3(-Mathf.Sin(p.Yaw), 0, -Mathf.Cos(p.Yaw));
        ServerSpawnObject("pickup", p.Pos + fwd * 1.2f + new Vector3(0, 0.4f, 0), item);
        SyncHeld(p);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ReportFallRpc(float speed)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (!CanAct(p) || p.Stats.NoFallDamage) return;
        speed = Mathf.Clamp(speed, 0, 40);
        if (speed <= Miner.FallSafeSpeed) return;
        p.TakeDamage((speed - Miner.FallSafeSpeed) * 6f, null, "fall");
    }

    // ============================================================ server -> client
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientMatchStart(int rounds)
    {
        TotalRounds = rounds; Round = 0; OwnTribute = 0; LocalWasKilled = false; Feed.Clear(); Weigh = null;
        _ownStates = 0; _foreignStates = 0;
        Util.Log($"MATCH: begins, {rounds} rounds");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientBeginRound(int round, int seed, float time, int[] eliminated, int totalRounds, int[] minerPeers)
    {
        FreeWorld();
        RoundPeers = minerPeers;
        Round = round; TimeLeft = time; RoundTotal = Mathf.Max(1f, time); TotalRounds = totalRounds; Weigh = null; OwnDowned = false;
        Alive.Clear(); Status.Clear();
        foreach (var id in Net.I.Names.Keys)
        {
            bool out_ = Array.IndexOf(eliminated, id) >= 0;
            Alive[id] = !out_;
            Status[id] = out_ ? MinerStatus.Eliminated : MinerStatus.Alive;
        }
        World = new World { Name = "World" };
        AddChild(World);
        World.BuildCave(seed, round, minerPeers.Length);
        Phase = GamePhase.Round;
        if (Array.IndexOf(eliminated, MyId) >= 0) EliminatedRole.ClientEnter(this, World.Cave.MinerSpawns[0] + new Vector3(0, 1.5f, 0));
        PhaseChanged?.Invoke();
        Util.Pass("round_started", $"round={round}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientSpawnMiner(int peer, Vector3 pos, string name, string held) => SpawnMinerNode(peer, pos, name, held);

    /// <summary>Creates (or replaces) the Miner node of a peer on this machine; the local peer's gets the controller.</summary>
    Miner SpawnMinerNode(int peer, Vector3 pos, string name, string held)
    {
        if (World == null) return null;
        World.GetMiner(peer)?.Free();
        bool local = peer == MyId;
        var m = new Miner { Name = peer.ToString(), PeerId = peer, PlayerName = name, IsLocal = local };
        m.SetMultiplayerAuthority(peer);
        if (local)
        {
            m.Controller = Util.HasArg("bot") ? ItemsDebug.MakeBot(Util.Arg("bot")) : new HumanController();
        }
        m.Position = pos;
        World.MinersRoot.AddChild(m);
        m.GlobalPosition = pos;
        m.SetHeld(held);
        Alive[peer] = true;
        if (Status.GetValueOrDefault(peer) != MinerStatus.Eliminated) Status[peer] = MinerStatus.Alive;
        if (local)
        {
            LocalMiner = m; OwnDowned = false;
            if (LocalGhost != null) { LocalGhost.QueueFree(); LocalGhost = null; }
            if (LocalWasKilled && Phase == GamePhase.Round && Round >= 2) Util.Pass("respawned_next_round", $"round={Round}");
        }
        return m;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientRemoveMiner(int peer)
    {
        World?.GetMiner(peer)?.QueueFree();
        Alive.Remove(peer);
        Status[peer] = MinerStatus.Eliminated;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientMinerDied(int peer)
    {
        Alive[peer] = false; Status[peer] = MinerStatus.Dead;
        if (peer == MyId) OwnDowned = false;
        var m = World?.GetMiner(peer);
        Vector3 at = m != null ? m.GlobalPosition + Vector3.Up * 1.5f : new Vector3(0, 2, 6);
        m?.QueueFree();
        Sfx.Play(SfxNames.Death, at);
        if (peer == MyId)
        {
            LocalMiner = null; LocalWasKilled = true;
            EnterGhost(at);
            Util.Pass("ghost_after_death", $"round={Round}");
        }
    }

    public void EnterGhost(Vector3 at)
    {
        if (World == null) return;
        LocalGhost?.QueueFree();
        LocalGhost = new Ghost { Name = "Ghost" };
        World.AddChild(LocalGhost);
        LocalGhost.GlobalPosition = at;
        PhaseChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    void ClientState(int peer, Vector3 pos, float yaw, float pitch, int flags)
    {
        if (peer == MyId) return;
        World?.GetMiner(peer)?.ApplyNetState(pos, yaw, pitch, flags);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientHeld(int peer, string id) => World?.GetMiner(peer)?.SetHeld(id);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientSwing(int peer) { if (peer != MyId) World?.GetMiner(peer)?.PlaySwing(); }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientKnockback(Vector3 v) => LocalMiner?.ApplyKnockback(v);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientSpawnObject(int id, string kind, Vector3 pos, string data) => World?.SpawnObject(id, kind, pos, data);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientObjectData(int id, string data) { if (World != null && World.Objects.TryGetValue(id, out var o)) o.ApplyData(data); }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientDespawnObject(int id) => World?.RemoveObject(id);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    void ClientRoundTime(float t) { TimeLeft = t; }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientAnnounce(string msg)
    {
        Feed.Add(msg);
        if (Feed.Count > 6) Feed.RemoveAt(0);
        Util.Log("GOBLIN: " + msg);
        FeedChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientOwnState(int owner, int tribute, string inv, int hp, float speedMul, float jumpMul, bool noFall, float burden, float oil, float oilMax)
    {
        if (owner != MyId)
        {
            _foreignStates++;
            Util.Fail("foreign_state_received", $"owner={owner} me={MyId}");
            return;
        }
        _ownStates++;
        if (burden >= 2f && !_burdenSeen) { _burdenSeen = true; Util.Pass("burden_slows", $"burden={burden:0.#} speed={Burden.SpeedMul(burden):0.00}"); }
        int prev = OwnTribute;
        OwnTribute = tribute; OwnInv = Inventory.Decode(inv); OwnHealth = hp; OwnBurden = burden; OwnOil = oil; OwnOilMax = oilMax;
        if (LocalMiner != null) { LocalMiner.SpeedMul = speedMul; LocalMiner.JumpMul = jumpMul; LocalMiner.NoFall = noFall; LocalMiner.Burden = burden; }
        Util.Log($"OWNSTATE tribute={tribute} burden={burden:0.#} hp={hp} oil={oil:0} inv={inv}");
        OwnChanged?.Invoke();
    }

    /// <summary>Public status change of a miner (downed, dead, eliminated).</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientStatus(int peer, int status)
    {
        Status[peer] = (MinerStatus)status;
        Alive[peer] = (MinerStatus)status is MinerStatus.Alive or MinerStatus.Downed;
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientMatchEnd(string[] names, int[] thrownRound, string winner)
    {
        LastMatchEnd = new MatchEndData { Names = names, ThrownRound = thrownRound, Winner = winner };
        Phase = GamePhase.MatchEnd;
        PhaseChanged?.Invoke();
        Util.Log($"FINAL name={Net.I.LocalName} winner={winner}");
        if (_foreignStates == 0 && _ownStates > 0) Util.Pass("no_foreign_state", $"ownStates={_ownStates}");
        else Util.Fail("no_foreign_state", $"own={_ownStates} foreign={_foreignStates}");
        Util.Pass("match_end", $"winner={winner}");
        if (Util.Automated) ScheduleQuit(2.5f);
    }

    /// <summary>Screenshot staging only (UiDebug): shows the match-end screen on a host without ending the match.</summary>
    public void DebugShowMatchEnd(MatchEndData d)
    {
        LastMatchEnd = d; Phase = GamePhase.MatchEnd; _phaseTimer = 1e6;
        PhaseChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientReturnLobby()
    {
        FreeWorld();
        Weigh = null;
        Phase = GamePhase.Lobby;
        PhaseChanged?.Invoke();
    }

    // ============================================================ client helpers
    void ScheduleQuit(float seconds)
    {
        if (_autoQuitScheduled) return;
        _autoQuitScheduled = true;
        GetTree().CreateTimer(seconds).Timeout += () => { Util.Log("QUIT: automated run finished"); GetTree().Quit(); };
    }

    public void FreeWorld()
    {
        if (World != null) { World.QueueFree(); World = null; }
        LocalMiner = null; LocalGhost = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    void OnServerLost()
    {
        Util.Log("GAME: host connection lost, returning to menu");
        TearDown();
        if (Util.Automated) ScheduleQuit(0.5f);
    }

    /// <summary>Leave any match/connection and reset local state (menu "leave" button, host quit).</summary>
    public void TearDown()
    {
        FreeWorld();
        Weigh = null;
        Phase = GamePhase.Lobby;
        Players.Clear();
        Feed.Clear();
        PhaseChanged?.Invoke();
        ReturnedToMenu?.Invoke();
    }

    public void LeaveToMenu()
    {
        Net.I.Disconnect();
        TearDown();
    }

    public override void _Process(double delta)
    {
        if (Phase == GamePhase.Round && TimeLeft > 0) TimeLeft -= (float)delta;
    }
}
