using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Downed state, robbing, stuffing a stein into the sack, lantern switch. A miner at 0 health is downed for Config.DownedSeconds: crawls very slowly, cannot act,
/// can be looted by a living miner, dies when the time runs out or on any further damage (then the usual death: bag, ghost, back next round).
/// Seam for later steps (drag / revive): ServerPlayer.Downed + <see cref="ServerRevive"/>, and the client side Miner.IsDowned / Miner.LootTargetPeer.
/// </summary>
public partial class Game
{
    // ============================================================ SERVER
    public void DownMiner(ServerPlayer v, ServerPlayer source, string cause)
    {
        if (!v.Alive || v.Downed) return;
        v.Downed = true; v.DownedLeft = Config.DownedSeconds; v.DownedBy = source; v.Health = 0; v.Dirty = true;
        Rpc(nameof(ClientDowned), v.Peer, true, v.DownedLeft);
        Util.Pass("miner_downed", $"victim={v.Name} cause={cause} by={source?.Name ?? "-"} secs={v.DownedLeft:0}");
        ServerCue("downed", $"{Round}{v.Name}", v.Name);
        SendOwnState(v);
    }

    /// <summary>Seam for reviving (later step): brings a downed miner back up with the given health.</summary>
    public void ServerRevive(ServerPlayer p, float health)
    {
        if (!p.Alive || !p.Downed) return;
        p.Downed = false; p.DownedBy = null; p.Health = Math.Clamp(health, 1f, ServerPlayer.MaxHealth); p.Dirty = true;
        Rpc(nameof(ClientDowned), p.Peer, false, 0f);
        Util.Pass("miner_revived", $"name={p.Name}");
    }

    /// <summary>Takes ONE item from a downed miner per press: the most valuable stone of the sack first (highest tribute), then steins, then utilities.
    /// Stones go straight into the looter's bottomless sack (with their burden).</summary>
    bool ServerLootOne(ServerPlayer looter, ServerPlayer victim)
    {
        if (victim.Inv.Sack.Count > 0)
        {
            int best = 0;
            for (int i = 1; i < victim.Inv.Sack.Count; i++) if (Ores.WeightOf(victim.Inv.Sack[i]) > Ores.WeightOf(victim.Inv.Sack[best])) best = i;
            string ore = victim.Inv.Sack[best];
            victim.Inv.Sack.RemoveAt(best);
            looter.Inv.Sack.Add(ore);
            victim.Dirty = true; looter.Dirty = true;
            SendOwnState(victim);
            Util.Pass("looted_downed", $"looter={looter.Name} victim={victim.Name} item=ore:{ore}");
            return true;
        }
        var order = new List<(string item, Action remove)>();
        for (int i = 0; i < victim.Inv.Steins.Count; i++)
        {
            int k = i; string id = victim.Inv.Steins[k];
            order.Add(("stein:" + id, () => { victim.Inv.Steins.RemoveAt(k); if (victim.Inv.SteinSel >= victim.Inv.Steins.Count) victim.Inv.SteinSel = 0; victim.RecalcStats(); }));
        }
        for (int i = 0; i < victim.Inv.Utils.Length; i++)
        {
            int k = i; string id = victim.Inv.Utils[k];
            if (string.IsNullOrEmpty(id)) continue;
            order.Add(("util:" + id, () => { victim.Inv.Utils[k] = null; SyncHeld(victim); }));
        }
        if (order.Count == 0) { ServerNotice(looter, "Nothing left to take."); return false; }
        foreach (var (item, remove) in order)
        {
            if (!looter.TryAdd(item)) continue;
            remove();
            victim.Dirty = true; looter.Dirty = true;
            SendOwnState(victim);
            Util.Pass("looted_downed", $"looter={looter.Name} victim={victim.Name} item={item}");
            ServerNotice(looter, "You take: " + ItemInfo.Line(item));
            return true;
        }
        ServerNotice(looter, "Your hands are full.");
        return false;
    }

    // ============================================================ client -> server
    public void SendLoot(int targetPeer) => RpcId(1, nameof(RequestLoot), targetPeer);
    public void SendStuffStein(int slot) => RpcId(1, nameof(RequestStuffStein), slot);
    public void SendToggleLantern() => RpcId(1, nameof(RequestToggleLantern));

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestLoot(int targetPeer)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (!CanAct(p) || Now < p.NextLoot) return;
        if (!Players.TryGetValue(targetPeer, out var v) || v == p || !v.Alive || !v.Downed) return;
        if (p.Pos.DistanceTo(v.Pos) > 3.4f) return;
        p.NextLoot = Now + 0.5;
        RpcId(v.Peer, nameof(ClientDownedAction), p.Peer, 2, true);   // "<Name> is robbing you" in the downed overlay
        ServerLootOne(p, v);
    }

    /// <summary>Hold G for a second on the selected stein slot (anywhere): the stein is stuffed into the sack and loses its power.</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestStuffStein(int slot)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (!CanAct(p) || slot < 0 || slot >= p.Inv.Steins.Count) return;
        ServerStuffStein(p, slot);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestToggleLantern()
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (!CanAct(p) || !ItemsServer.HasUtil(p, "lantern")) return;
        p.LanternOn = !p.LanternOn;
        if (p.LanternOn && p.Oil <= 0f) ServerNotice(p, "The lantern is out of oil.");
        Util.Pass("lantern_toggled", $"name={p.Name} on={p.LanternOn} oil={p.Oil:0}");
    }

    // ============================================================ server -> client
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientDowned(int peer, bool downed, float seconds)
    {
        Status[peer] = downed ? MinerStatus.Downed : MinerStatus.Alive;
        World?.GetMiner(peer)?.SetDowned(downed);
        if (peer == MyId) DownedActors.Clear();
        if (peer == MyId)
        {
            OwnDowned = downed; OwnDownedUntil = ClientNow + seconds;
            if (downed) Util.Pass("downed_seen", $"secs={seconds:0}");
            OwnChanged?.Invoke();
        }
    }
}
