using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

public enum GamePhase { Lobby, Round, Summary, MatchEnd }

public class SummaryData
{
    public int Round, Delivered, Quota, Bonus;
    public bool QuotaMet, Last;
    public string Poorest = "", Survivor = "";
}

public class MatchEndData
{
    public string[] Names; public int[] Money; public string Winner;
}

/// <summary>
/// Autoload: match + round state machine (server-authoritative) and all in-round RPCs.
/// Conventions: Request*/Report* = client -> server (AnyPeer, validated). Client* = server -> clients (Authority).
/// Private per-miner data (money, inventory) only ever leaves the server via RpcId(owner, ClientOwnState).
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
    public int Quota, Delivered;
    public readonly Dictionary<int, bool> Alive = new(); // public: who is alive this round
    public SummaryData LastSummary;
    public MatchEndData LastMatchEnd;

    // private (own) state, only filled from ClientOwnState
    public int OwnMoney, OwnSackCap = 4;
    public float OwnHealth = 100f;
    public Inventory OwnInv = new();
    public Miner LocalMiner;
    public Ghost LocalGhost;
    public bool LocalWasKilled;

    public readonly List<string> Feed = new();

    public event Action PhaseChanged, OwnChanged, FeedChanged, ReturnedToMenu;

    // test counters
    int _ownStates, _foreignStates;

    // ------------------------------------------------------------ server-only state
    public readonly Dictionary<int, ServerPlayer> Players = new();
    readonly RandomNumberGenerator _rng = new();
    double _roundLeft, _phaseTimer, _syncTimer;
    int _seed;
    bool _collapsing;
    bool _autoQuitScheduled;

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

    MatchConfig PresetConfig() => MatchConfig.FromName(Util.Arg("preset", "default"));

    public void ServerStartMatch()
    {
        if (!CanStart) return;
        Config = PresetConfig();
        Util.Log($"MATCH: start preset={Config.Name} players={Net.I.Names.Count}");
        Players.Clear();
        foreach (var id in Net.I.Names.Keys)
        {
            var p = GetPlayer(id);
            p.Money = 0; p.Cursed = false; p.Initialized = false;
        }
        Round = 0;
        Rpc(nameof(ClientMatchStart), Config.Rounds);
        ServerStartRound();
    }

    void ServerStartRound()
    {
        Round++;
        Phase = GamePhase.Round;
        _collapsing = false;
        _roundLeft = Config.RoundSeconds(Round);
        _syncTimer = 0;
        _seed = (int)(_rng.Randi() & 0x7fffffff);
        var parts = Net.I.Names.Keys.ToList();
        foreach (var id in parts)
        {
            var p = GetPlayer(id);
            p.Participant = true; p.Alive = true;
            if (!p.Initialized)
            {
                // Match start (or mid-match joiner): start kit + one random Common stein.
                p.Initialized = true;
                p.Inv.Clear(); p.Inv.GiveStartKit();
                p.Inv.Steins.Add(Steins.RandomCommon(_rng));
            }
            p.Health = ServerPlayer.MaxHealth * (p.Cursed ? Config.CurseHealthFraction : 1f);
            p.Cursed = false;
            p.DeliveredThisRound = 0; p.KillsThisRound = 0;
            p.RecalcStats();
        }
        Quota = Config.Quota(Round, parts.Count);
        Delivered = 0;
        Rpc(nameof(ClientBeginRound), Round, _seed, (float)_roundLeft, Quota, false);
        PopulateWorld();
        int i = 0;
        foreach (var id in parts)
        {
            var p = Players[id];
            var pos = World.Cave.MinerSpawns[i++ % World.Cave.MinerSpawns.Count];
            p.Pos = pos;
            Rpc(nameof(ClientSpawnMiner), id, pos, p.Name, p.Inv.Held);
            p.Fire(new SteinEvent { Kind = SteinEventKind.RoundStart });
            p.Dirty = true;
        }
        Util.Log($"ROUND: {Round} start quota={Quota} time={_roundLeft}");
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
                    float hp = Ores.ValueOf(ore) >= 100 ? 4 : 3;
                    ServerSpawnObject("vein", m.Pos, VeinObj.MakeData(ore, hp, hp));
                }
                else if (m.Kind == "ore")
                    ServerSpawnObject("pickup", m.Pos + new Vector3(0, 0.4f, 0), "ore:" + Ores.Pick(rng, room.Depth, Round));
            }
        RunMarkerSpawners(rng);
    }

    void ServerEndRound()
    {
        if (Phase != GamePhase.Round) return;
        Phase = GamePhase.Summary;
        var parts = Players.Values.Where(p => p.Participant).ToList();
        bool met = Delivered >= Quota;
        if (!met) foreach (var p in parts) p.Money -= (int)(p.Money * Config.QuotaMissMoneyLoss);
        string poorest = "";
        if (parts.Count > 0)
        {
            var poor = parts.OrderBy(p => p.DeliveredThisRound).ThenBy(p => p.Money).ThenBy(p => p.Peer).First();
            poor.Cursed = true; poorest = poor.Name;
        }
        var alive = parts.Where(p => p.Alive).ToList();
        string survivor = ""; int bonus = 0;
        if (alive.Count == 1) { survivor = alive[0].Name; bonus = Config.SurvivorBonus; alive[0].Money += bonus; }
        foreach (var p in parts)
        {
            p.Fire(new SteinEvent { Kind = SteinEventKind.RoundEnd });
            p.DiedLastRound = !p.Alive;
            if (!p.Alive) { p.Inv.Clear(); p.Inv.GiveStartKit(); p.RecalcStats(); }
            p.Dirty = true;
        }
        _phaseTimer = Config.SummarySeconds;
        Util.Log($"ROUND: {Round} end quotaMet={met} delivered={Delivered}/{Quota} poorest={poorest} survivor={survivor}");
        Rpc(nameof(ClientRoundSummary), Round, met, Delivered, Quota, poorest, survivor, bonus, Round >= Config.Rounds);
        foreach (var p in parts) SendOwnState(p);
    }

    void ServerEndMatch()
    {
        Phase = GamePhase.MatchEnd;
        var list = Players.Values.Where(p => Net.I.Names.ContainsKey(p.Peer)).OrderByDescending(p => p.Money).ThenBy(p => p.Peer).ToList();
        string winner = list.Count > 0 ? list[0].Name : "";
        foreach (var p in list) Util.Log($"FINAL: peer={p.Peer} name={p.Name} money={p.Money}");
        Util.Log($"WINNER={winner}");
        if (list.Count > 0 && list.All(p => p.Money <= list[0].Money)) Util.Pass("winner_correct", $"winner={winner} money={list[0].Money}");
        _phaseTimer = Config.MatchEndSeconds;
        Rpc(nameof(ClientMatchEnd), list.Select(p => p.Name).ToArray(), list.Select(p => p.Money).ToArray(), winner);
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
                if (_roundLeft <= 0) ServerCollapse();
                break;
            case GamePhase.Summary:
                _phaseTimer -= dt;
                foreach (var p in Players.Values) if (p.Dirty && Now - p.LastOwnSend > 0.1) SendOwnState(p);
                if (_phaseTimer <= 0) { if (Round >= Config.Rounds) ServerEndMatch(); else ServerStartRound(); }
                break;
            case GamePhase.MatchEnd:
                _phaseTimer -= dt;
                if (_phaseTimer <= 0) ServerReturnToLobby();
                break;
        }
    }

    void ServerCollapse()
    {
        _collapsing = true;
        Rpc(nameof(ClientAnnounce), "The Goblin collapses the mine! Everyone outside the camp is buried.");
        foreach (var p in Players.Values.Where(p => p.Participant && p.Alive).ToArray())
            if (!World.Cave.InCamp(p.Pos)) KillMiner(p, null, "collapse");
        _collapsing = false;
        ServerEndRound();
    }

    void CheckAllDead()
    {
        if (Phase != GamePhase.Round || _collapsing) return;
        if (!Players.Values.Any(p => p.Participant && p.Alive))
        {
            Rpc(nameof(ClientAnnounce), "Every miner is dead. The Goblin ends the round.");
            ServerEndRound();
        }
    }

    void OnPeerRegistered(int id)
    {
        var p = GetPlayer(id);
        if (Phase == GamePhase.Lobby) return;
        // Mid-match joiner: ghost until the next round.
        p.Participant = false; p.Alive = false;
        RpcId(id, nameof(ClientAnnounce), "A match is in progress. You will join as a ghost until the next round.");
        if (Phase == GamePhase.Round) ServerSendSnapshot(id);
    }

    void ServerSendSnapshot(int peer)
    {
        RpcId(peer, nameof(ClientBeginRound), Round, _seed, (float)_roundLeft, Quota, true);
        RpcId(peer, nameof(ClientQuota), Delivered);
        foreach (var o in World.Objects.Values) RpcId(peer, nameof(ClientSpawnObject), o.Id, o.Kind, o.Position, o.Data);
        foreach (var p in Players.Values)
            if (p.Participant && p.Alive && p.Peer != peer)
                RpcId(peer, nameof(ClientSpawnMiner), p.Peer, p.Pos, p.Name, p.Inv.Held);
    }

    void OnPeerLeft(int id)
    {
        if (!Players.TryGetValue(id, out var p)) return;
        Util.Log($"GAME: peer {id} ({p.Name}) left");
        if (p.Alive && Phase == GamePhase.Round) KillMiner(p, null, "left");
        p.Participant = false;
        Players.Remove(id);
        Rpc(nameof(ClientRemoveMiner), id);
        CheckAllDead();
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

    public void KillMiner(ServerPlayer v, ServerPlayer killer, string cause)
    {
        if (!v.Alive) return;
        v.Alive = false; v.Health = 0;
        v.Fire(new SteinEvent { Kind = SteinEventKind.Death, Other = killer });
        if (killer != null) { killer.KillsThisRound++; killer.Fire(new SteinEvent { Kind = SteinEventKind.Kill, Other = v }); }
        var items = v.Inv.ToItemList();
        if (items.Count > 0)
        {
            int id = ServerSpawnObject("bag", v.Pos, "");
            if (World.Objects.TryGetValue(id, out var o) && o is BagObj bag) bag.Items = items;
        }
        v.Inv.Clear(); v.RecalcStats();
        Util.Pass("death_bag_dropped", $"victim={v.Name} cause={cause} items={items.Count}");
        Rpc(nameof(ClientMinerDied), v.Peer);
        Rpc(nameof(ClientAnnounce), $"{v.Name} has perished.");
        SendOwnState(v);
        CheckAllDead();
    }

    public void ServerDeposit(ServerPlayer p)
    {
        if (p.Inv.Sack.Count == 0) return;
        int total = 0; foreach (var o in p.Inv.Sack) total += Ores.ValueOf(o);
        int n = p.Inv.Sack.Count;
        p.Inv.Sack.Clear();
        p.Money += total; p.DeliveredThisRound += total; Delivered += total;
        p.Dirty = true;
        p.Fire(new SteinEvent { Kind = SteinEventKind.Deposit, Amount = total });
        Util.Pass("deposit_server", $"peer={p.Peer} name={p.Name} ore={n} amount={total} money={p.Money}");
        Rpc(nameof(ClientQuota), Delivered);
        Rpc(nameof(ClientAnnounce), $"The Goblin takes ore from a miner. Quota: {Delivered}/{Quota}.");
        SendOwnState(p);
    }

    void SendOwnState(ServerPlayer p)
    {
        p.Dirty = false; p.LastOwnSend = Now;
        if (!Net.I.Names.ContainsKey(p.Peer)) return;
        // HIDDEN INFORMATION: only the owner ever receives money/inventory.
        RpcId(p.Peer, nameof(ClientOwnState), p.Peer, p.Money, p.Inv.Encode(), (int)Math.Ceiling(p.Health), p.Stats.SpeedMul, p.Stats.JumpMul, p.Stats.NoFallDamage, p.SackCap);
    }

    void SyncHeld(ServerPlayer p) => Rpc(nameof(ClientHeld), p.Peer, p.Inv.Held);

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
    public void SendDrop(string kind, int idx) => RpcId(1, nameof(RequestDrop), kind, idx);
    public void ReportFall(float speed) => RpcId(1, nameof(ReportFallRpc), speed);
    public void SendMeddle() => RpcId(1, nameof(RequestMeddle));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    void ReportState(Vector3 pos, float yaw, float pitch, int flags)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null || !p.Alive || Phase != GamePhase.Round) return;
        p.Pos = pos; p.Yaw = yaw; p.Pitch = pitch;
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
        if (p == null || !p.Alive || Phase != GamePhase.Round || Now < p.NextAttack) return;
        var def = Utilities.Get(p.Inv.Held);
        bool pick = def?.IsPickaxe == true;
        p.NextAttack = Now + (pick ? 0.75 : 0.38);
        if (origin.DistanceTo(p.Pos + new Vector3(0, 1.6f, 0)) > 3f || dir.LengthSquared() < 0.5f) return;
        dir = dir.Normalized();
        Rpc(nameof(ClientSwing), p.Peer);
        float reach = pick ? 3.0f : 2.3f;
        var me = World.GetMiner(p.Peer);
        var q = PhysicsRayQueryParameters3D.Create(origin, origin + dir * reach, 1 | 2 | 4);
        if (me != null) q.Exclude = new Godot.Collections.Array<Rid> { me.GetRid() };
        var hit = World.GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count == 0) return;
        var col = hit["collider"].As<GodotObject>();
        if (col is Miner vm)
        {
            if (!Players.TryGetValue(vm.PeerId, out var v) || !v.Alive) return;
            if (World.Cave.InCamp(v.Pos) || World.Cave.InCamp(p.Pos)) return; // no PvP in camp
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
        if (p == null || !p.Alive || Phase != GamePhase.Round) return;
        if (!World.Objects.TryGetValue(objId, out var o)) return;
        if (p.Pos.DistanceTo(o.GlobalPosition) > o.InteractRange + 1.0f) return;
        o.ServerInteract(p);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestSelectUtil(int slot)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null || !p.Alive || slot < 0 || slot >= Inventory.UtilSlots) return;
        p.Inv.Selected = slot; p.Dirty = true;
        SyncHeld(p);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestDrop(string kind, int idx)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null || !p.Alive || Phase != GamePhase.Round || idx < 0) return;
        string item = null;
        if (kind == "util" && idx < Inventory.UtilSlots && !string.IsNullOrEmpty(p.Inv.Utils[idx])) { item = "util:" + p.Inv.Utils[idx]; p.Inv.Utils[idx] = null; }
        else if (kind == "ore" && idx < p.Inv.Sack.Count) { item = "ore:" + p.Inv.Sack[idx]; p.Inv.Sack.RemoveAt(idx); }
        if (item == null) return;
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
        if (p == null || !p.Alive || Phase != GamePhase.Round || p.Stats.NoFallDamage) return;
        speed = Mathf.Clamp(speed, 0, 40);
        if (speed <= Miner.FallSafeSpeed) return;
        p.TakeDamage((speed - Miner.FallSafeSpeed) * 6f, null, "fall");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestMeddle()
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null || p.Alive || !p.Participant || Now < p.NextMeddle) return;
        p.NextMeddle = Now + 8;
        // STUB: real ghost meddling (blow out a lantern, knock a pebble) belongs to a later workstream.
        Util.Log($"MEDDLE (stub) by {p.Name}");
        Rpc(nameof(ClientAnnounce), "Something rattles in the dark...");
    }

    // ============================================================ server -> client
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientMatchStart(int rounds)
    {
        TotalRounds = rounds; Round = 0; OwnMoney = 0; LocalWasKilled = false; Feed.Clear();
        _ownStates = 0; _foreignStates = 0;
        Util.Log($"MATCH: begins, {rounds} rounds");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientBeginRound(int round, int seed, float time, int quota, bool spectator)
    {
        FreeWorld();
        Round = round; TimeLeft = time; Quota = quota; Delivered = 0;
        Alive.Clear();
        foreach (var id in Net.I.Names.Keys) Alive[id] = !spectator;
        World = new World { Name = "World" };
        AddChild(World);
        World.BuildCave(seed, round);
        Phase = GamePhase.Round;
        if (spectator) EnterGhost(new Vector3(0, 2, 6));
        PhaseChanged?.Invoke();
        Util.Pass("round_started", $"round={round}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientSpawnMiner(int peer, Vector3 pos, string name, string held)
    {
        if (World == null) return;
        World.GetMiner(peer)?.QueueFree();
        bool local = peer == MyId;
        var m = new Miner { Name = peer.ToString(), PeerId = peer, PlayerName = name, IsLocal = local };
        m.SetMultiplayerAuthority(peer);
        if (local)
        {
            m.Controller = Util.HasArg("bot") ? new BotBrain(Util.Arg("bot")) : new HumanController();
        }
        m.Position = pos;
        World.MinersRoot.AddChild(m);
        m.GlobalPosition = pos;
        m.SetHeld(held);
        Alive[peer] = true;
        if (local)
        {
            LocalMiner = m;
            if (LocalGhost != null) { LocalGhost.QueueFree(); LocalGhost = null; }
            if (LocalWasKilled && Round >= 2) Util.Pass("respawned_next_round", $"round={Round}");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientRemoveMiner(int peer)
    {
        World?.GetMiner(peer)?.QueueFree();
        Alive.Remove(peer);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientMinerDied(int peer)
    {
        Alive[peer] = false;
        var m = World?.GetMiner(peer);
        Vector3 at = m != null ? m.GlobalPosition + Vector3.Up * 1.5f : new Vector3(0, 2, 6);
        m?.QueueFree();
        if (peer == MyId)
        {
            LocalMiner = null; LocalWasKilled = true;
            EnterGhost(at);
            Util.Pass("ghost_after_death", $"round={Round}");
        }
    }

    void EnterGhost(Vector3 at)
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
    void ClientQuota(int delivered) { Delivered = delivered; OwnChanged?.Invoke(); }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientAnnounce(string msg)
    {
        Feed.Add(msg);
        if (Feed.Count > 6) Feed.RemoveAt(0);
        Util.Log("GOBLIN: " + msg);
        FeedChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientOwnState(int owner, int money, string inv, int hp, float speedMul, float jumpMul, bool noFall, int sackCap)
    {
        if (owner != MyId)
        {
            _foreignStates++;
            Util.Fail("foreign_state_received", $"owner={owner} me={MyId}");
            return;
        }
        _ownStates++;
        int prev = OwnMoney;
        OwnMoney = money; OwnInv = Inventory.Decode(inv); OwnHealth = hp; OwnSackCap = sackCap;
        if (LocalMiner != null) { LocalMiner.SpeedMul = speedMul; LocalMiner.JumpMul = jumpMul; LocalMiner.NoFall = noFall; }
        Util.Log($"OWNSTATE money={money} hp={hp} inv={inv}");
        if (money > prev) Util.Pass("deposit_credited", $"money={money}");
        OwnChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientRoundSummary(int round, bool met, int delivered, int quota, string poorest, string survivor, int bonus, bool last)
    {
        LastSummary = new SummaryData { Round = round, QuotaMet = met, Delivered = delivered, Quota = quota, Poorest = poorest, Survivor = survivor, Bonus = bonus, Last = last };
        Phase = GamePhase.Summary;
        PhaseChanged?.Invoke();
        Util.Pass("round_summary_shown", $"round={round} quotaMet={met} poorest={poorest}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientMatchEnd(string[] names, int[] money, string winner)
    {
        LastMatchEnd = new MatchEndData { Names = names, Money = money, Winner = winner };
        Phase = GamePhase.MatchEnd;
        PhaseChanged?.Invoke();
        Util.Log($"FINAL own_money={OwnMoney} name={Net.I.LocalName} winner={winner}");
        if (_foreignStates == 0 && _ownStates > 0) Util.Pass("no_foreign_state", $"ownStates={_ownStates}");
        else Util.Fail("no_foreign_state", $"own={_ownStates} foreign={_foreignStates}");
        Util.Pass("match_end", $"winner={winner}");
        if (Util.Automated) ScheduleQuit(2.5f);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientReturnLobby()
    {
        FreeWorld();
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
