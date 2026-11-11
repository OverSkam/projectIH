using System;
using Godot;

namespace AppStein;

/// <summary>Server-only record of one player. Never replicated as a whole: owner gets private state via ClientOwnState.</summary>
public class ServerPlayer
{
    public int Peer;
    public string Name;
    public int Money;
    public float Health = 100f;
    public const float MaxHealth = 100f;
    public bool Alive;
    public bool Participant;        // takes part in the current round (false for mid-round joiners/ghosts)
    public Inventory Inv = new();
    public StatBlock Stats = new();
    public int DeliveredThisRound;
    public bool Cursed;
    public bool DiedLastRound, Initialized;
    public int KillsThisRound;
    public Vector3 Pos;
    public float Yaw, Pitch;
    public double NextAttack;
    public bool Dirty;              // own-state needs resending to the owner
    public double LastOwnSend;
    public double NextMeddle;
    float _regenAcc;

    public int SackCap => Inventory.BaseSack + Stats.SackBonus;

    public void RecalcStats()
    {
        var s = new StatBlock();
        foreach (var id in Inv.Steins) Steins.Get(id)?.Modify?.Invoke(s);
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
        foreach (var id in Inv.Steins.ToArray()) Steins.Get(id)?.OnTick?.Invoke(this, dt);
        if (Stats.RegenPerSec > 0) Regen(Stats.RegenPerSec * dt);
    }

    /// <summary>Fractional healing accumulator (so tiny per-tick amounts still count).</summary>
    public void Regen(float amount)
    {
        if (!Alive || Health >= MaxHealth) return;
        _regenAcc += amount;
        if (_regenAcc >= 1f)
        {
            int whole = (int)_regenAcc;
            _regenAcc -= whole;
            Health = Math.Min(MaxHealth, Health + whole);
            Dirty = true;
        }
    }

    public void Heal(float amount) { if (Alive) { Health = Math.Min(MaxHealth, Health + amount); Dirty = true; } }

    /// <summary>Applies damage through stat modifiers; returns the damage actually dealt.</summary>
    public float TakeDamage(float amount, ServerPlayer source, string cause)
    {
        if (!Alive) return 0;
        float dmg = amount * Stats.DamageTakenMul;
        Health -= dmg;
        Dirty = true;
        Fire(new SteinEvent { Kind = SteinEventKind.DamageTaken, Amount = dmg, Other = source });
        source?.Fire(new SteinEvent { Kind = SteinEventKind.DamageDealt, Amount = dmg, Other = this });
        if (Health <= 0f) Game.I.KillMiner(this, source, cause);
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
                if (Inv.Sack.Count >= SackCap) return false;
                Inv.Sack.Add(id); break;
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
