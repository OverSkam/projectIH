using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// v2.3 "other miners": shove (R), spill, pickpocket (crouch + hold E), the downed options (hold E revive, C rob, hold RMB drag),
/// and the coarse public size of every miner's sack. All server-authoritative; the only private data (stones taken out of a sack) goes to the victim
/// alone, late, as a toast. Client-side presentation lives in Miner.cs (input, prompts, stagger), MinerRig.cs (lunge, sack, pointing) and SocialFx.cs.
/// </summary>
public partial class Game
{
    public const int HoldPick = 1, HoldRevive = 2, HoldDrag = 3;

    // ------------------------------------------------------------ client state
    /// <summary>Public: the coarse sack size 0..4 of every miner (what the models show). Nothing else about a sack is public.</summary>
    public readonly Dictionary<int, int> SackSteps = new();
    /// <summary>Own: the stone that just left the sack (picked or spilled), for the red toast. Set just before the own-state that shows the loss arrives.</summary>
    public string StolenOre = ""; public double StolenAt = -100;
    /// <summary>Own, while downed: who does what to me. peer -> (mode 1 helping up / 2 robbing / 3 dragging, until).</summary>
    public readonly Dictionary<int, (int mode, double until)> DownedActors = new();
    /// <summary>Own: a server-ended hold (the player must release the key before holding again).</summary>
    public int HoldEndedKind; public double HoldEndedAt = -100;
    public int ShovesFelt;
    /// <summary>Server test counters: shoves that hit / were blocked by the cooldown / found nobody / were refused (weighing), pickpocket attempts that started.</summary>
    public int ShoveHits, ShoveBlocked, ShoveMisses, ShoveDenied, PickStarts;

    // ------------------------------------------------------------ client -> server
    public void SendShove() => RpcId(1, nameof(RequestShove));
    public void SendHold(int kind, int target, bool on) => RpcId(1, nameof(RequestHold), kind, target, on);

    // ============================================================ SERVER: bookkeeping
    void SocialResetPlayer(ServerPlayer p)
    {
        EndHold(p, false);
        ReleaseDragOf(p);
        p.StaggerUntil = 0; p.NextShove = 0; p.PickCooldownUntil = 0; p.HoldOwnUntil = 0; p.StolenOre = null; p.DraggedBy = -1; p.Dragging = -1; p.SackStep = 0;
    }

    /// <summary>The fuse burned out: every attempt in progress ends, and a shove that landed in the last seconds is logged with what it decided (the Hall check at the fuse is the server's last reported position).</summary>
    void SocialAtFuse(List<ServerPlayer> contenders)
    {
        foreach (var p in Players.Values.ToArray())
        {
            EndHold(p, true);
            ReleaseDragOf(p);
            p.StaggerUntil = 0; p.HoldOwnUntil = 0;
            if (p.StolenOre != null) FlushStolen(p);
        }
        foreach (var p in contenders)
        {
            if (!p.Alive || Now - p.LastShovedAt > 5.0) continue;
            bool inHall = World.Cave.InHall(p.Pos);
            Util.Pass("shove_at_fuse", $"victim={p.Name} by={p.LastShovedBy} ago={Now - p.LastShovedAt:0.0}s in_hall={inHall} counted={inHall} weight={(inHall ? p.TributeWeight : 0)}");
        }
    }

    void FlushStolen(ServerPlayer p)
    {
        if (p.StolenOre == null) return;
        string ore = p.StolenOre; p.StolenOre = null;
        if (p.StolenAt > 0) Util.Pass("pickpocket_victim_notified", $"victim={p.Name} delay={Now - p.StolenAt:0.00}");
        p.StolenAt = 0;
        if (Net.I.Names.ContainsKey(p.Peer)) RpcId(p.Peer, nameof(ClientStolen), ore);
        p.Dirty = true;
    }

    /// <summary>Per miner and physics frame (round only): the late "-" toast of a pickpocketed victim, the public sack step, and the hold-to-act checks.</summary>
    void SocialTickPlayer(ServerPlayer p)
    {
        if (p.StolenOre != null && Now >= p.HoldOwnUntil) FlushStolen(p);
        int step = p.Alive ? Burden.Step(p.Burden) : 0;
        if (step != p.SackStep) { p.SackStep = step; Rpc(nameof(ClientSackStep), p.Peer, step); }
        if (p.Hold != null) TickHold(p);
        if (p.DraggedBy >= 0 && (!p.Downed || !p.Alive)) StopDrag(Players.GetValueOrDefault(p.DraggedBy));
    }

    ServerPlayer Other(int peer) => Players.TryGetValue(peer, out var v) && v.Participant ? v : null;
    static float Flat(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    // ============================================================ SERVER: shove
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestShove()
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null) return;
        if (!CanAct(p)) { if (p.Alive && Phase != GamePhase.Round) { ShoveDenied++; Util.Log($"SHOVE_DENIED by={p.Name} phase={Phase}"); } return; }
        if (Now < p.NextShove) { ShoveBlocked++; Util.Log($"SHOVE_COOLDOWN by={p.Name} left={p.NextShove - Now:0.00}"); return; }
        p.NextShove = Now + SocialTuning.ShoveCooldown;
        EndHold(p, true);
        bool sprint = (p.Flags & 2) != 0;
        Rpc(nameof(ClientShoveAnim), p.Peer);
        ServerNoise(p.Pos, SocialTuning.ShoveNoise, p);
        var fwd = SocialTuning.Fwd(p.Yaw);
        ServerPlayer best = null; float bestScore = float.MaxValue;
        foreach (var v in Players.Values)
        {
            if (v == p || !v.Participant || !v.Alive || v.Downed) continue;
            var d = new Vector3(v.Pos.X - p.Pos.X, 0, v.Pos.Z - p.Pos.Z);
            float dist = d.Length();
            if (dist > SocialTuning.ShoveRange + SocialTuning.ShoveSlack || Mathf.Abs(v.Pos.Y - p.Pos.Y) > 1.3f) continue;
            if (dist > 0.45f && fwd.Dot(d / dist) < SocialTuning.ShoveConeCos) continue;
            // no shoving through a wall
            var q = Queries.Ray(p.Pos + new Vector3(0, 1.3f, 0), v.Pos + new Vector3(0, 1.2f, 0), 1 | 4);
            if (World.GetWorld3D().DirectSpaceState.IntersectRay(q).Count > 0) continue;
            float score = dist + (1f - fwd.Dot(dist > 0.01f ? d / dist : fwd)) * 0.8f;
            if (score < bestScore) { bestScore = score; best = v; }
        }
        if (best == null) { ShoveMisses++; Util.Log($"SHOVE_MISS by={p.Name}"); return; }
        ServerShoveHit(p, best, sprint);
    }

    void ServerShoveHit(ServerPlayer p, ServerPlayer v, bool sprint)
    {
        var away = new Vector3(v.Pos.X - p.Pos.X, 0, v.Pos.Z - p.Pos.Z);
        away = away.LengthSquared() < 0.01f ? SocialTuning.Fwd(p.Yaw) : away.Normalized();
        // shoving from beside or in front pushes along the shover's facing rather than the line between the two (keeps the push straight for the shover)
        var dir = (away + SocialTuning.Fwd(p.Yaw)).Normalized();
        ShoveHits++;
        float speed = sprint ? SocialTuning.ShoveSprintSpeed : SocialTuning.ShoveSpeed;
        v.StaggerUntil = Now + SocialTuning.ShoveStagger;
        v.LastShovedAt = Now; v.LastShovedBy = p.Name;
        EndHold(v, true);                                   // a thief who is shoved loses the grip
        RpcId(v.Peer, nameof(ClientShoved), dir * speed, SocialTuning.ShoveStagger, p.Peer);
        Rpc(nameof(ClientShoveHit), v.Peer, v.Pos);
        bool back = SocialTuning.IsBehind(v.Pos, v.Yaw, p.Pos);
        string spilled = "";
        if (back) spilled = ServerSpill(v, p) ?? "";
        Util.Pass("shove_hit", $"by={p.Name} victim={v.Name} back={back} sprint={sprint} spilled={(spilled == "" ? "-" : spilled)} dir={dir.X:0.00},{dir.Z:0.00}");
    }

    /// <summary>The top (most recently picked) stone pops out of the sack as a normal pickup that rolls away. Returns its ore id, or null when the sack is empty.</summary>
    string ServerSpill(ServerPlayer v, ServerPlayer by)
    {
        if (v.Inv.Sack.Count == 0) return null;
        string ore = v.Inv.Sack[^1];
        v.Inv.Sack.RemoveAt(v.Inv.Sack.Count - 1);
        v.Dirty = true;
        var back = -SocialTuning.Fwd(v.Yaw);
        float ang = _rng.RandfRange(-0.7f, 0.7f), dist = _rng.RandfRange(1.4f, 2.2f);
        var dir = back.Rotated(Vector3.Up, ang);
        var from = v.Pos + new Vector3(0, 1.1f, 0) + back * 0.35f;
        var target = v.Pos + dir * dist;
        // stop short of a wall
        var wq = Queries.Ray(v.Pos + new Vector3(0, 0.8f, 0), target + new Vector3(0, 0.8f, 0), 1 | 4);
        var wh = World.GetWorld3D().DirectSpaceState.IntersectRay(wq);
        if (wh.Count > 0) target = wh["position"].AsVector3() - dir * 0.35f;
        var fq = Queries.Ray(new Vector3(target.X, v.Pos.Y + 1.5f, target.Z), new Vector3(target.X, v.Pos.Y - 4f, target.Z), 1);
        var fh = World.GetWorld3D().DirectSpaceState.IntersectRay(fq);
        var floor = fh.Count > 0 ? fh["position"].AsVector3() : new Vector3(target.X, v.Pos.Y, target.Z);
        int id = ServerSpawnObject("pickup", floor + new Vector3(0, 0.4f, 0), "ore:" + ore);
        Rpc(nameof(ClientSpill), id, from, v.Pos);
        ServerNoise(v.Pos, 10f, by);
        v.StolenOre = ore; v.HoldOwnUntil = 0; FlushStolen(v);   // the victim feels this one at once
        SendOwnState(v);
        Util.Pass("spill", $"victim={v.Name} by={by.Name} ore={ore} id={id} sack_left={v.Inv.Sack.Count}");
        return ore;
    }

    // ============================================================ SERVER: hold actions (pickpocket, revive, drag)
    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RequestHold(int kind, int target, bool on)
    {
        if (!Net.I.IsServer) return;
        var p = Sender();
        if (p == null) return;
        if (!on) { if (p.Hold != null && p.Hold.Kind == kind) EndHold(p, false); return; }
        if (!CanAct(p)) return;
        EndHold(p, false);
        var v = Other(target);
        if (v == null || v == p || !v.Alive) { RpcId(p.Peer, nameof(ClientHoldEnd), kind); return; }
        float flat = Flat(p.Pos, v.Pos);
        switch (kind)
        {
            case HoldPick:
                if (v.Downed || (p.Flags & 1) == 0) { RpcId(p.Peer, nameof(ClientHoldEnd), kind); return; }
                if (Now < p.PickCooldownUntil) { ServerNotice(p, "Your hands are still shaking."); RpcId(p.Peer, nameof(ClientHoldEnd), kind); return; }
                if (flat > SocialTuning.PickRange + SocialTuning.PickRangeSlack || !SocialTuning.IsBehind(v.Pos, v.Yaw, p.Pos)) { RpcId(p.Peer, nameof(ClientHoldEnd), kind); return; }
                if (v.Inv.Sack.Count == 0) { ServerNotice(p, "Nothing in that sack."); RpcId(p.Peer, nameof(ClientHoldEnd), kind); return; }
                p.Hold = new HoldAct { Kind = kind, Target = target, Start = Now };
                PickStarts++;
                Rpc(nameof(ClientRustle), v.Pos);
                Util.Pass("pickpocket_started", $"thief={p.Name} victim={v.Name}");
                break;
            case HoldRevive:
                if (!v.Downed || flat > SocialTuning.ReviveRange + 0.8f) { RpcId(p.Peer, nameof(ClientHoldEnd), kind); return; }
                p.Hold = new HoldAct { Kind = kind, Target = target, Start = Now };
                RpcId(v.Peer, nameof(ClientDownedAction), p.Peer, 1, true);
                Util.Pass("revive_started", $"by={p.Name} victim={v.Name}");
                break;
            case HoldDrag:
                if (!v.Downed || flat > SocialTuning.DragRange + 0.8f || v.DraggedBy >= 0) { RpcId(p.Peer, nameof(ClientHoldEnd), kind); return; }
                p.Hold = new HoldAct { Kind = kind, Target = target, Start = Now };
                p.Dragging = v.Peer; v.DraggedBy = p.Peer;
                RpcId(v.Peer, nameof(ClientDragged), p.Peer, true);
                RpcId(v.Peer, nameof(ClientDownedAction), p.Peer, 3, true);
                Rpc(nameof(ClientDragPose), p.Peer, true);
                Util.Pass("drag_started", $"by={p.Name} victim={v.Name}");
                break;
        }
    }

    void EndHold(ServerPlayer p, bool tellClient)
    {
        var h = p.Hold; if (h == null) return;
        p.Hold = null;
        var v = Players.GetValueOrDefault(h.Target);
        if (h.Kind == HoldDrag) StopDrag(p);
        else if (h.Kind == HoldRevive && v != null && Net.I.Names.ContainsKey(v.Peer)) RpcId(v.Peer, nameof(ClientDownedAction), p.Peer, 1, false);
        if (tellClient && Net.I.Names.ContainsKey(p.Peer)) RpcId(p.Peer, nameof(ClientHoldEnd), h.Kind);
    }

    /// <summary>Let go of whoever this miner drags (the hold ended, the dragger was shoved or died, the round ended).</summary>
    void StopDrag(ServerPlayer d)
    {
        if (d == null || d.Dragging < 0) return;
        var v = Players.GetValueOrDefault(d.Dragging);
        d.Dragging = -1;
        if (d.Hold != null && d.Hold.Kind == HoldDrag) d.Hold = null;
        if (Net.I.Names.ContainsKey(d.Peer)) { Rpc(nameof(ClientDragPose), d.Peer, false); RpcId(d.Peer, nameof(ClientHoldEnd), HoldDrag); }
        if (v == null) return;
        v.DraggedBy = -1;
        Util.Pass("drag_ended", $"by={d.Name} victim={v.Name}");
        if (Net.I.Names.ContainsKey(v.Peer)) { RpcId(v.Peer, nameof(ClientDragged), -1, false); RpcId(v.Peer, nameof(ClientDownedAction), d.Peer, 3, false); }
    }

    /// <summary>Either side of a drag pair: stop it.</summary>
    void ReleaseDragOf(ServerPlayer p)
    {
        if (p.Dragging >= 0) StopDrag(p);
        if (p.DraggedBy >= 0) StopDrag(Players.GetValueOrDefault(p.DraggedBy));
        p.DraggedBy = -1; p.Dragging = -1;
    }

    void TickHold(ServerPlayer p)
    {
        var h = p.Hold;
        var v = Other(h.Target);
        double el = Now - h.Start;
        if (!CanAct(p) || v == null || !v.Alive) { EndHold(p, true); return; }
        float flat = Flat(p.Pos, v.Pos);
        bool hurt = p.LastDamageAt > h.Start;
        switch (h.Kind)
        {
            case HoldPick:
            {
                bool lost = v.Downed || (p.Flags & 1) == 0 || flat > SocialTuning.PickRange + SocialTuning.PickRangeSlack + 0.2f
                            || !SocialTuning.IsBehind(v.Pos, v.Yaw, p.Pos, SocialTuning.PickFailCos) || hurt || v.Inv.Sack.Count == 0;
                if (lost) { FailPick(p, v, el); return; }
                if (el >= SocialTuning.PickSeconds) SucceedPick(p, v);
                break;
            }
            case HoldRevive:
                if (!v.Downed || flat > SocialTuning.ReviveRange + 1.2f || hurt) { EndHold(p, true); return; }
                if (el >= SocialTuning.ReviveSeconds)
                {
                    p.Hold = null;
                    RpcId(v.Peer, nameof(ClientDownedAction), p.Peer, 1, false);
                    ServerRevive(v, SocialTuning.ReviveHealth);
                    Util.Pass("revive_done", $"by={p.Name} victim={v.Name} hp={v.Health:0}");
                    RpcId(p.Peer, nameof(ClientHoldEnd), HoldRevive);
                }
                break;
            case HoldDrag:
                if (!v.Downed || flat > SocialTuning.DragMaxRange) EndHold(p, true);
                break;
        }
    }

    void FailPick(ServerPlayer p, ServerPlayer v, double el)
    {
        bool loud = el > SocialTuning.PickSeconds * SocialTuning.PickLoudAfter;
        p.Hold = null;
        if (loud)
        {
            Rpc(nameof(ClientClatter), v.Pos);
            ServerNoise(v.Pos, SocialTuning.PickLoudNoise, p);
            p.PickCooldownUntil = Now + SocialTuning.PickFailCooldown;
            ServerNotice(p, "You fumble the sack!");
        }
        Util.Pass("pickpocket_failed", $"thief={p.Name} victim={v.Name} held={el:0.0} loud={loud} cooldown={(loud ? SocialTuning.PickFailCooldown : 0)}");
        if (Net.I.Names.ContainsKey(p.Peer)) RpcId(p.Peer, nameof(ClientHoldEnd), HoldPick);
    }

    void SucceedPick(ServerPlayer p, ServerPlayer v)
    {
        p.Hold = null;
        int i = (int)(_rng.Randi() % (uint)v.Inv.Sack.Count);
        string ore = v.Inv.Sack[i];
        v.Inv.Sack.RemoveAt(i);
        p.Inv.Sack.Add(ore);
        v.Dirty = true; p.Dirty = true;
        SendOwnState(p);
        // the victim learns about it 1.5 s later, so the thief has a moment to get away; the own-state is held back too (SendOwnState)
        v.StolenOre = ore; v.StolenAt = Now; v.HoldOwnUntil = Now + SocialTuning.PickVictimDelay;
        v.Dirty = true;
        Util.Pass("pickpocket_success", $"thief={p.Name} victim={v.Name} ore={ore} victim_sack_left={v.Inv.Sack.Count} thief_sack={p.Inv.Sack.Count}");
        if (Net.I.Names.ContainsKey(p.Peer)) RpcId(p.Peer, nameof(ClientHoldEnd), HoldPick);
        p.PickCooldownUntil = Math.Max(p.PickCooldownUntil, Now + 1.0);
    }

    // ============================================================ server -> client
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientShoveAnim(int peer)
    {
        var m = World?.GetMiner(peer);
        if (m == null || peer == MyId) return;     // the shover already played their own (predicted) animation
        m.PlayShove();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientShoved(Vector3 vel, float stagger, int by)
    {
        ShovesFelt++;
        LocalMiner?.GetShoved(vel, stagger);
        Util.Pass("shoved_received", $"speed={new Vector2(vel.X, vel.Z).Length():0.0} stagger={stagger:0.0}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientShoveHit(int victim, Vector3 pos)
    {
        Sfx.Play(SfxNames.ShoveHit, pos + Vector3.Up);
        Sfx.Play(SfxNames.Grunt, pos + new Vector3(0, 1.4f, 0), victim == MyId ? -2f : 0f);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientSpill(int id, Vector3 from, Vector3 at)
    {
        Sfx.Play(SfxNames.StoneClatter, at + Vector3.Up * 0.5f);
        if (World != null && World.Objects.TryGetValue(id, out var o)) SocialFx.Toss(o, from);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientStolen(string ore)
    {
        StolenOre = ore; StolenAt = ClientNow;
        Util.Pass("stolen_notice", $"ore={ore}");
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientSackStep(int peer, int step)
    {
        SackSteps[peer] = step;
        World?.GetMiner(peer)?.SetSackStep(step);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientRustle(Vector3 pos) => Sfx.Play(SfxNames.Rustle, pos + Vector3.Up * 0.9f);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientClatter(Vector3 pos) => Sfx.Play(SfxNames.PickFumble, pos + Vector3.Up * 0.9f);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientHoldEnd(int kind) { HoldEndedKind = kind; HoldEndedAt = ClientNow; }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientDownedAction(int actor, int mode, bool on)
    {
        if (on) DownedActors[actor] = (mode, ClientNow + (mode == 2 ? 1.6 : 1e9));
        else DownedActors.Remove(actor);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientDragged(int by, bool on) { if (LocalMiner != null) LocalMiner.SetDraggedBy(on ? by : -1); }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientDragPose(int dragger, bool on) => World?.GetMiner(dragger)?.SetDragging(on);
}
