using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>RPCs of step 2 (geodes, bright chests, mimics). Public information only: what a reveal looks like, where stones land. What is IN a geode / chest is rolled by the
/// server at the moment of the reveal and reaches the clients as the pickups it spawns. The chest hold is a request; the server decides.</summary>
public partial class Game
{
    // ------------------------------------------------------------ client -> server
    public void SendChestHold(int objId, bool start) => RpcId(1, nameof(RequestChestHold), objId, start);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestChestHold(int objId, bool start)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null || World == null || !World.Objects.TryGetValue(objId, out var o) || o is not GlowChestObj c) return;
        if (start) { if (CanAct(p)) c.ServerStartHold(p); }
        else c.ServerStopHold(p, "released");
    }

    // ------------------------------------------------------------ server -> client
    /// <summary>A geode / chest reveal: the burst, the stones flying to their landing spots (pickups already exist), labels, and screen feedback by distance.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ClientLootBurst(Vector3 pos, int tier, Color col, int[] ids, string[] items, Vector3[] land)
    {
        if (World == null) return;
        LootFx.Reveal(pos, tier, col, ids, items, land);
        Util.Pass("loot_burst_seen", $"tier={tier} n={items.Length}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ClientGeodeDud(Vector3 pos, int kind)
    {
        if (World == null) return;
        LootFx.Dud(pos, kind);
        Util.Pass("geode_dud_seen", $"kind={kind}");
    }

    /// <summary>Owner only: mode 1 hold accepted (the bar starts), 0 refused / interrupted, 2 completed.</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ClientChestHold(int objId, int mode) => Loot2Client.OnServerHold(objId, mode);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ClientChestPop(int chestId, int index, int total, int pickupId, Vector3 land, string item)
    {
        if (World == null) return;
        LootFx.ChestPop(chestId, index, total, pickupId, land, item);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void ClientChestMove(int id, Vector3 pos, float yaw)
    {
        if (World != null && World.Objects.TryGetValue(id, out var o) && o is GlowChestObj c) c.ClientMove(pos, yaw);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void ClientChestSettle(int id, Vector3 pos, float yaw)
    {
        if (World != null && World.Objects.TryGetValue(id, out var o) && o is GlowChestObj c) c.ClientSettle(pos, yaw);
    }

    // ------------------------------------------------------------ test only (honoured on --bot=l2_* clients)
    public void L2Step(string step, string arg) => Rpc(nameof(ClientL2Step), step, arg);
    public void L2Teleport(ServerPlayer p, Vector3 pos, float yaw) { p.Pos = pos; RpcId(p.Peer, nameof(ClientL2Teleport), pos, yaw); }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientL2Step(string step, string arg) => Loot2Bot.OnStep(step, arg);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientL2Teleport(Vector3 pos, float yaw)
    {
        if (!Util.Arg("bot").StartsWith("l2_")) return;
        LocalMiner?.Teleport(pos, yaw);
    }
}
