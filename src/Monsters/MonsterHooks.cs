using Godot;

namespace AppStein;

/// <summary>Integration with the Items workstream: bear traps root monsters, dynamite hurts them / disperses wisps.</summary>
public abstract partial class MonsterObj : IRootable, IExplodable
{
    int _rootGen;
    public void ServerRoot(float seconds)
    {
        if (this is DragonObj || this is WispObj || State == MonsterState.Dead) return;
        Frozen = true;
        int gen = ++_rootGen;
        GetTree().CreateTimer(seconds).Timeout += () => { if (gen == _rootGen && IsInstanceValid(this)) Frozen = false; };
    }

    public void ServerTrapDamage(float amount)
    {
        if (this is DragonObj || this is WispObj) return;
        Hurt(amount, null);
    }

    public void OnExplosion(Vector3 center, float radius, float damage)
    {
        if (this is WispObj w) { w.Disperse(20f); return; }
        if (this is DragonObj) return;
        Hurt(damage, null);
    }
}
