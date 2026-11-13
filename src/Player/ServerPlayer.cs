using System;
using Godot;

namespace AppStein;

/// <summary>Server-only record of one player. Never replicated as a whole: owner gets private state via ClientOwnState.</summary>
public class ServerPlayer
{
    public int Peer;
    public string Name;
    /// <summary>Tribute of this miner right now = what is in the sack (plus a debug / Blood adjustment). Private: only ever sent to its owner (until the weighing).
    /// Counts at the weighing only if the miner is inside the Hall; the sack is emptied after every weighing.</summary>
    public int TributeWeight
    {
        get => Math.Max(0, Ores.TributeSum(Inv.Sack) + TributeAdj);
        set => TributeAdj = value - Ores.TributeSum(Inv.Sack);
    }
    /// <summary>Adjustment on top of the sack's worth (Blood Stein penalty, test staging with --tribute).</summary>
    public int TributeAdj;
    /// <summary>Effective burden (sack contents x Packmule factor).</summary>
    public float Burden => Ores.BurdenSum(Inv.Sack) * Stats.BurdenMul;
    public float Health = 100f;
    public const float MaxHealth = 100f;
    public bool Alive;
    public bool Participant;        // takes part in the current round (false for eliminated miners and late joiners)
    /// <summary>Thrown by the Goblin (or left / joined late): permanently out of the match. What they do instead is decided by <see cref="EliminatedRole"/>.</summary>
    public bool Eliminated;
    public int EliminatedRound;
    /// <summary>At 0 health: crawls, cannot act, can be looted; dies after DownedLeft seconds or on any further damage.</summary>
    public bool Downed;
    public float DownedLeft;
    public ServerPlayer DownedBy;   // whoever dealt the downing blow (gets the kill credit if the miner bleeds out)
    public double NextLoot;
    /// <summary>Lantern oil in seconds of light left (private, owner only). Starts every round full.</summary>
    public float Oil;
    public bool LanternOn = true;   // switched on (public: the light itself is replicated, the oil level is not)
    public Inventory Inv = new();
    public StatBlock Stats = new();
    /// <summary>Died or was locked out: next round they return with only the pickaxe and the lantern.</summary>
    public bool NeedsKit;
    public bool Initialized;
    public int EliminationSeq;
    public int KillsThisRound;
    public Vector3 Pos;
    public float Yaw, Pitch;
    public double NextAttack;
    /// <summary>Seconds (Time.GetTicksMsec / 1000) of the last damage this miner took: interrupts the chest hold (src/Loot2).</summary>
    public double LastDamageAt;
    public bool Dirty;              // own-state needs resending to the owner
    public double LastOwnSend;
    public double NextMeddle;
    public ItemState Items = new();
    float _regenAcc;

    public void RecalcStats()
    {
        var s = new StatBlock();
        foreach (var id in Inv.Steins) Steins.Get(id)?.Modify?.Invoke(s);
        foreach (var id in Inv.Steins) Steins.Get(id)?.ModifyFor?.Invoke(this, s);
        ItemsServer.ApplyUtilityStats(this, s);
        Stats = s;
        Dirty = true;
    }

    public void Fire(SteinEvent e)
    {
        foreach (var id in Inv.Steins.ToArray()) Steins.Get(id)?.OnEvent?.Invoke(this, e);
    }

    public void Tick(float dt)
    {
        if (!Alive) return;
        if (Downed)
        {
            DownedLeft -= dt;
            if (DownedLeft <= 0f) Game.I.KillMiner(this, DownedBy, "bleed");
            return;
        }
        foreach (var id in Inv.Steins.ToArray()) Steins.Get(id)?.OnTick?.Invoke(this, dt);
        if (Stats.RegenPerSec > 0) Regen(Stats.RegenPerSec * dt);
        ItemsServer.Tick(this, dt);
    }

    /// <summary>Fractional healing accumulator (so tiny per-tick amounts still count).</summary>
    public void Regen(float amount)
    {
        if (!Alive || Downed || Health >= MaxHealth) return;
        _regenAcc += amount;
        if (_regenAcc >= 1f)
        {
            int whole = (int)_regenAcc;
            _regenAcc -= whole;
            Health = Math.Min(MaxHealth, Health + whole);
            Dirty = true;
        }
    }

    public void Heal(float amount) { if (Alive && !Downed) { Health = Math.Min(MaxHealth, Health + amount); Dirty = true; } }

    /// <summary>Applies damage through stat modifiers; returns the damage actually dealt.</summary>
    public float TakeDamage(float amount, ServerPlayer source, string cause)
    {
        if (!Alive) return 0;
        if (cause == "gas" && Stats.GasImmune) return 0;
        float dmg = amount * Stats.DamageTakenMul;
        if (Downed) { source?.Fire(new SteinEvent { Kind = SteinEventKind.DamageDealt, Amount = dmg, Other = this }); Game.I.KillMiner(this, source ?? DownedBy, cause); return dmg; }
        Health -= dmg;
        if (dmg > 0f) LastDamageAt = Time.GetTicksMsec() / 1000.0;
        Dirty = true;
        if (Util.HasArg("soak")) Util.Log($"SOAKDMG cause={cause} victim={Name} dmg={dmg:0.#} hp={Health:0.#} by={source?.Name ?? "-"}");
        Fire(new SteinEvent { Kind = SteinEventKind.DamageTaken, Amount = dmg, Other = source });
        source?.Fire(new SteinEvent { Kind = SteinEventKind.DamageDealt, Amount = dmg, Other = this });
        if (Health <= 0f) Game.I.DownMiner(this, source, cause);
        return dmg;
    }

    /// <summary>Tries to put an item ("ore:x", "stein:x", "util:x") into the matching slot group.</summary>
    public bool TryAdd(string item)
    {
        int c = item.IndexOf(':');
        string kind = item.Substring(0, c), id = item.Substring(c + 1);
        switch (kind)
        {
            case "ore":
                Inv.Sack.Add(id); break;   // bottomless
            case "stein":
                if (Inv.Steins.Count >= Inventory.SteinSlots) return false;
                Inv.Steins.Add(id); RecalcStats(); break;
            case "util":
                int slot = Inv.FreeUtilSlot();
                if (slot < 0) return false;
                Inv.Utils[slot] = id; break;
            default: return false;
        }
        Dirty = true;
        return true;
    }
}
