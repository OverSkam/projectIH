using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>
/// A little social life for the soak bots (ExplorerBot, BotBrain "miner"): now and then it shoves a miner who stands in front of it, sneaks up on a miner who stands still
/// and picks their sack, and walks over to a downed miner to help them up, rob them or drag them towards the Hall. It only uses what a real client sees (public miner nodes).
/// Decisions are deterministic per peer / round (a hash of the two peers and the round, plus a per-peer seeded random for the "how often").
/// </summary>
public class SocialBot
{
    enum S { Idle, Approach, Do }
    readonly Random _rng;
    readonly int _me;
    S _s = S.Idle;
    int _action, _target;       // action: Game.HoldPick / HoldRevive / HoldDrag / 10 = rob
    double _t, _eval, _cool = 6, _doStart, _lastPress;
    int _presses;
    readonly Dictionary<int, (Vector3 pos, double t)> _seen = new();
    bool _holding;

    public SocialBot(int peer) { _me = peer; _rng = new Random(peer * 7919 + 13); }

    static int Hash(int a, int b, int c) { unchecked { uint h = 2166136261u; foreach (var x in new[] { a, b, c }) { h ^= (uint)x; h *= 16777619u; } return (int)(h & 0x7fffffff); } }

    void End(Miner m, Game g, double cooldown)
    {
        if (_holding) { g.SendHold(_action == 10 ? 0 : _action, _target, false); _holding = false; }
        m.SetBotHold(0);
        _s = S.Idle; _cool = cooldown; _presses = 0;
    }

    /// <summary>Returns true when the bot took control of this frame's input.</summary>
    public bool Run(Miner m, ref MinerInput inp, Game g, float dt)
    {
        if (g.World == null || m == null || g.Phase != GamePhase.Round) return false;
        _t += dt; _cool -= dt; _eval -= dt;
        if (m.IsDowned || g.TimeLeft < 50f) { if (_s != S.Idle) End(m, g, 20); return false; }
        switch (_s)
        {
            case S.Idle:
                if (_eval > 0) return false;
                _eval = 0.5;
                Evaluate(m, ref inp, g);
                return _s != S.Idle;
            case S.Approach: return Approach(m, ref inp, g);
            default: return Do(m, ref inp, g);
        }
    }

    void Evaluate(Miner m, ref MinerInput inp, Game g)
    {
        var world = g.World; Miner downed = null, still = null, front = null; float dd = 20f, ds = 10f;
        foreach (var o in world.Miners())
        {
            if (o == m || !g.Alive.GetValueOrDefault(o.PeerId)) continue;
            float d = o.GlobalPosition.DistanceTo(m.GlobalPosition);
            if (o.IsDowned) { if (d < dd) { dd = d; downed = o; } continue; }
            bool still_ = false;
            if (_seen.TryGetValue(o.PeerId, out var prev) && _t - prev.t > 0.45) { still_ = prev.pos.DistanceTo(o.GlobalPosition) < 0.2f; _seen[o.PeerId] = (o.GlobalPosition, _t); }
            else if (!_seen.ContainsKey(o.PeerId)) _seen[o.PeerId] = (o.GlobalPosition, _t);
            if (d < ds && still_ && !world.Cave.InHall(o.GlobalPosition)) { ds = d; still = o; }
            var to = o.GlobalPosition - m.GlobalPosition; to.Y = 0;
            if (d < 1.7f && d > 0.1f && SocialTuning.Fwd(m.Rotation.Y).Dot(to.Normalized()) > 0.5f) front = o;
        }
        if (downed != null && _cool <= 0)
        {
            int roll = Hash(_me, downed.PeerId, g.Round) % 6;
            if (roll == 5) { _cool = 25; return; }
            _action = roll <= 1 ? Game.HoldRevive : roll <= 3 ? 10 : Game.HoldDrag;
            Start(downed);
            return;
        }
        if (front != null && _cool <= 0 && _rng.NextDouble() < 0.4)
        {
            inp.Shove = true; _cool = 10 + _rng.NextDouble() * 12;   // "in my way": a shove, once in a while
            return;
        }
        if (still != null && _cool <= 0 && _rng.NextDouble() < 0.5)
        {
            _action = Game.HoldPick; Start(still);
        }
    }

    void Start(Miner target) { _target = target.PeerId; _s = S.Approach; _doStart = _t; }

    bool Approach(Miner m, ref MinerInput inp, Game g)
    {
        var tn = g.World.GetMiner(_target);
        if (tn == null || !g.Alive.GetValueOrDefault(_target) || _t - _doStart > 9) { End(m, g, 20); return false; }
        if (_action != Game.HoldPick && !tn.IsDowned) { End(m, g, 15); return false; }
        if (_action == Game.HoldPick && tn.IsDowned) { End(m, g, 15); return false; }
        var pos = m.GlobalPosition;
        Vector3 goal; float arrive;
        if (_action == Game.HoldPick) { goal = tn.GlobalPosition - SocialTuning.Fwd(tn.Rotation.Y) * 0.95f; arrive = 0.35f; }
        else { goal = tn.GlobalPosition + (pos - tn.GlobalPosition).Normalized() * 1.2f; arrive = 0.5f; }
        var flat = new Vector3(goal.X - pos.X, 0, goal.Z - pos.Z);
        var look = tn.GlobalPosition + new Vector3(0, _action == Game.HoldPick ? 1.0f : 0.2f, 0) - m.EyePos;
        inp.Yaw = Mathf.Atan2(-look.X, -look.Z); inp.Pitch = Mathf.Clamp(Mathf.Atan2(look.Y, new Vector2(look.X, look.Z).Length()), -1.2f, 1.2f);
        bool behindNow = SocialTuning.IsBehind(tn.GlobalPosition, tn.Rotation.Y, pos);
        if (flat.Length() <= arrive && (_action != Game.HoldPick || behindNow))
        {
            _s = S.Do; _doStart = _t; _lastPress = 0; _presses = 0; return true;
        }
        // walk straight towards the spot (the target is within a few metres, usually in the same room); crouch for the last stretch of a pickpocket
        var wishDir = flat.Length() > 0.01f ? flat.Normalized() : Vector3.Zero;
        var fwd = SocialTuning.Fwd(inp.Yaw); var right = new Vector3(Mathf.Cos(inp.Yaw), 0, -Mathf.Sin(inp.Yaw));
        inp.Move = new Vector2(wishDir.Dot(right), wishDir.Dot(fwd));
        if (inp.Move.Length() > 1f) inp.Move = inp.Move.Normalized();
        inp.Crouch = _action == Game.HoldPick && flat.Length() < 3.5f;
        return true;
    }

    bool Do(Miner m, ref MinerInput inp, Game g)
    {
        var tn = g.World.GetMiner(_target);
        double el = _t - _doStart;
        if (tn == null || !g.Alive.GetValueOrDefault(_target)) { End(m, g, 20); return false; }
        var look = tn.GlobalPosition + new Vector3(0, _action == Game.HoldPick ? 1.0f : 0.2f, 0) - m.EyePos;
        inp.Yaw = Mathf.Atan2(-look.X, -look.Z); inp.Pitch = Mathf.Clamp(Mathf.Atan2(look.Y, new Vector2(look.X, look.Z).Length()), -1.2f, 1.2f);
        switch (_action)
        {
            case Game.HoldPick:
                inp.Crouch = true;
                if (!_holding) { g.SendHold(Game.HoldPick, _target, true); _holding = true; m.SetBotHold(0); }
                if (el > SocialTuning.PickSeconds + 0.8 || g.HoldEndedAt > _doStart + 0.05) End(m, g, 40);
                break;
            case Game.HoldRevive:
                if (!_holding) { g.SendHold(Game.HoldRevive, _target, true); _holding = true; }
                if (el > SocialTuning.ReviveSeconds + 0.8 || g.HoldEndedAt > _doStart + 0.05) End(m, g, 20);
                break;
            case 10:   // rob: one stone per press
                if (el - _lastPress > 0.65) { _lastPress = el; g.SendLoot(_target); _presses++; }
                if (_presses >= 4) End(m, g, 20);
                break;
            case Game.HoldDrag:
            {
                if (!_holding) { g.SendHold(Game.HoldDrag, _target, true); _holding = true; m.SetBotHold(Game.HoldDrag); }
                // drag towards the Hall: straight at the bot's own cart for up to 7 s (the downed miner is pulled along on a 1.25 m tether)
                int rank = Math.Max(0, Array.IndexOf(g.RoundPeers, Net.I.MyId));
                var home = g.World.Cave.StandSpot(rank, Math.Max(1, g.RoundPeers.Length));
                var to = new Vector3(home.X - m.GlobalPosition.X, 0, home.Z - m.GlobalPosition.Z);
                if (to.Length() > 1.5f) { inp.Yaw = Mathf.Atan2(-to.X, -to.Z); inp.Pitch = 0; inp.Move = new Vector2(0, 1); }
                if (el > 7 || g.HoldEndedAt > _doStart + 0.05) End(m, g, 25);
                break;
            }
        }
        return true;
    }
}
