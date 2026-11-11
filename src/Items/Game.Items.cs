using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>RPCs and helpers of the items workstream (utilities, active steins, ghost meddling, explosions).</summary>
public partial class Game
{
    // ------------------------------------------------------------ client-side own (private) item state
    public bool OwnDark;
    public string OwnAbility = "";
    public double OwnAbilityReadyAt, OwnEtherealUntil;
    public string ItemNotice = "";
    public double ItemNoticeAt;
    /// <summary>Set by the Infinity Stein when a holder has won; the match ends after the round summary.</summary>
    public string ForcedWinner;

    static double ClientNow => Time.GetTicksMsec() / 1000.0;

    // ------------------------------------------------------------ server helpers
    public void ServerAnnounce(string msg) => Rpc(nameof(ClientAnnounce), msg);
    public void ServerNotice(ServerPlayer p, string msg) { if (Net.I.Names.ContainsKey(p.Peer)) RpcId(p.Peer, nameof(ClientNotice), msg); }
    public void ServerSyncHeld(ServerPlayer p) => SyncHeld(p);
    public void ServerMask(int peer, bool on) => Rpc(nameof(ClientMask), peer, on);
    public void ServerLantern(int peer, bool outNow) => Rpc(nameof(ClientLantern), peer, outNow);
    public void ServerRootPlayer(ServerPlayer p, float seconds) => RpcId(p.Peer, nameof(ClientRoot), seconds);
    public void DebugEndRound() { if (Net.I.IsServer && Phase == GamePhase.Round) ServerCollapse(); }
    public void ServerKnock(ServerPlayer p, Vector3 v) => RpcId(p.Peer, nameof(ClientKnockback), v);

    /// <summary>Owner-only: dark vision flag, active ability id, cooldown and active time left.</summary>
    public void ServerSendItemState(ServerPlayer p)
    {
        if (!Net.I.Names.ContainsKey(p.Peer)) return;
        double now = ItemsServer.Now;
        RpcId(p.Peer, nameof(ClientItemState), p.Peer, p.Stats.DarkVision, ItemsServer.AbilityOf(p),
            (float)Math.Max(0, p.Items.AbilityReadyAt - now), (float)Math.Max(0, p.Items.EtherealUntil - now));
    }

    public void ServerExplode(Vector3 pos, ServerPlayer by, float radius = 6.5f, float maxDmg = 70f)
    {
        Rpc(nameof(ClientExplosion), pos, radius);
        int victims = 0; float total = 0;
        foreach (var p in Players.Values.ToArray())
        {
            if (!p.Alive || !p.Participant || World.Cave.InCamp(p.Pos)) continue;   // the camp is safe from explosions too
            var c = p.Pos + new Vector3(0, 0.9f, 0);
            float d = c.DistanceTo(pos);
            if (d > radius) continue;
            float dmg = maxDmg * (1f - d / radius);
            if (dmg < 1f) continue;
            var dir = (c - pos) with { Y = 0 };
            dir = dir.LengthSquared() < 0.01f ? Vector3.Right : dir.Normalized();
            ServerKnock(p, dir * 8f + new Vector3(0, 4f, 0));
            total += p.TakeDamage(dmg, by, "explosion");
            victims++;
        }
        int veins = 0;
        foreach (var o in World.Objects.Values.ToArray())
        {
            float d = o.GlobalPosition.DistanceTo(pos);
            if (o is VeinObj v && d <= radius - 1f)
            {
                ServerDespawnObject(v.Id);
                ServerSpawnObject("pickup", v.GlobalPosition + new Vector3(0, 0.45f, 0), "ore:" + v.OreId);
                veins++;
            }
            else if (o is IExplodable ex && d <= radius + 1f) ex.OnExplosion(pos, radius, maxDmg * Math.Max(0.1f, 1f - d / radius));
        }
        ServerNoise(pos, 45f, by);
        Util.Pass("dynamite_exploded", $"by={by?.Name} victims={victims} dmg={total:0.#} veins={veins}");
        if (veins > 0) Util.Pass("dynamite_vein_broken", $"veins={veins}");
    }

    // ------------------------------------------------------------ client -> server wrappers
    public void SendUseUtility() => RpcId(1, nameof(RequestUseUtility));
    public void SendAbility() => RpcId(1, nameof(RequestAbility));
    public void SendDropStein(int idx) => RpcId(1, nameof(RequestDropStein), idx);
    public void SendSteinSel(int idx) => RpcId(1, nameof(RequestSteinSel), idx);
    public void SendGhostMeddle(Vector3 pos, int mode) => RpcId(1, nameof(RequestGhostMeddle), pos, mode);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestUseUtility()
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null || !p.Alive || Phase != GamePhase.Round || Now < p.Items.NextUse) return;
        p.Items.NextUse = Now + 0.5;
        ItemsServer.UseHeld(p);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestAbility()
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null || !p.Alive || Phase != GamePhase.Round) return;
        ItemsServer.UseAbility(p);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestDropStein(int idx)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null || !p.Alive || Phase != GamePhase.Round) return;
        ItemsServer.DropStein(p, idx);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestSteinSel(int idx)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null || !p.Alive || idx < 0 || idx >= Inventory.SteinSlots) return;
        p.Inv.SteinSel = idx; p.Dirty = true;
    }

    /// <summary>Ghost meddling. mode 0 = blow out the lantern of the nearest miner, 1 = knock a pebble (noise at the ghost's position).</summary>
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestGhostMeddle(Vector3 pos, int mode)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null || p.Alive || !p.Participant || Phase != GamePhase.Round || Now < p.NextMeddle) return;
        if (pos.Length() > 500f) return;
        if (mode == 0)
        {
            ServerPlayer best = null; float bd = 9f;
            foreach (var m in Players.Values)
            {
                if (!m.Alive || !m.Participant || !ItemsServer.HasUtil(m, "lantern")) continue;
                float d = m.Pos.DistanceTo(pos);
                if (d < bd) { bd = d; best = m; }
            }
            if (best == null) { ServerNotice(p, "No lantern close enough to snuff."); return; }
            p.NextMeddle = Now + 6;
            best.Items.LanternOutUntil = Now + 6;
            ServerLantern(best.Peer, true);
            Util.Pass("ghost_lantern_blown", $"ghost={p.Name} victim={best.Name}");
        }
        else
        {
            p.NextMeddle = Now + 6;
            Rpc(nameof(ClientPebble), pos);
            ServerNoise(pos, 14f, null);
            Util.Pass("ghost_pebble", $"ghost={p.Name}");
        }
    }

    // ------------------------------------------------------------ server -> client
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientItemState(int owner, bool dark, string ability, float cd, float etherealLeft)
    {
        if (owner != MyId) { Util.Fail("foreign_item_state_received", $"owner={owner} me={MyId}"); return; }
        OwnDark = dark; OwnAbility = ability;
        OwnAbilityReadyAt = ClientNow + cd;
        OwnEtherealUntil = ClientNow + etherealLeft;
        if (etherealLeft > 0) Util.Pass("ethereal_state_received");
        OwnChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientMask(int peer, bool on) => World?.GetMiner(peer)?.SetMask(on);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientLantern(int peer, bool outNow) => World?.GetMiner(peer)?.SetLanternOut(outNow);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientRoot(float seconds)
    {
        if (LocalMiner == null) return;
        LocalMiner.Root(seconds);
        Util.Pass("rooted_received", $"secs={seconds}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientNotice(string msg)
    {
        ItemNotice = msg; ItemNoticeAt = ClientNow;
        Util.Log("NOTICE: " + msg);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientExplosion(Vector3 pos, float radius)
    {
        if (World == null) return;
        ItemFx.Explosion(World, pos, radius);
        Util.Pass("explosion_seen");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientPebble(Vector3 pos)
    {
        if (World != null) ItemFx.Pebble(World, pos);
        Util.Pass("pebble_seen");
    }

    // ------------------------------------------------------------ debug / test only (honoured on bot clients)
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientDebugStep(string step, string arg) => ItemsDebug.OnStep(step, arg);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientTeleport(Vector3 pos, float yaw)
    {
        if (!Util.Arg("bot").StartsWith("t_")) return;
        LocalMiner?.Teleport(pos, yaw);
    }
    public void DebugTeleport(ServerPlayer p, Vector3 pos, float yaw) { p.Pos = pos; RpcId(p.Peer, nameof(ClientTeleport), pos, yaw); }
    public void DebugStep(string step, string arg = "") => Rpc(nameof(ClientDebugStep), step, arg);
}
