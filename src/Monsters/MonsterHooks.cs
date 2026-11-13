using Godot;

namespace AppStein;

/// <summary>Integration with the Items workstream: bear traps and dynamite stun monsters briefly (never damage); dynamite also scatters gas wisps.</summary>
public abstract partial class MonsterObj : IRootable, IExplodable
{
    public const float TrapStunSeconds = 3f, BlastStunSeconds = 2.5f;

    public void ServerRoot(float seconds)
    {
        if (this is DragonObj || this is WispObj) return;
        Stun(Mathf.Max(seconds, TrapStunSeconds));
    }

    public virtual void OnExplosion(Vector3 center, float radius, float damage)
    {
        if (this is WispObj w) { w.Disperse(20f); return; }
        if (this is DragonObj) return;
        Stun(BlastStunSeconds);
    }
}
