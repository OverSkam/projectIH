using Godot;

namespace AppStein;

/// <summary>Client-side effects of the v2.3 interactions: the hop of a stone that was knocked out of a sack.</summary>
public static class SocialFx
{
    /// <summary>A stone knocked out of a sack: it was already spawned at its resting place; hop it there from the sack on the miner's back (two bounces).</summary>
    public static void Toss(Node3D o, Vector3 from)
    {
        if (o == null || !GodotObject.IsInstanceValid(o)) return;
        var end = o.Position;
        o.Position = from;
        var tw = o.CreateTween();
        tw.TweenMethod(Callable.From<float>(t =>
        {
            if (!GodotObject.IsInstanceValid(o)) return;
            float hop = t < 0.72f ? 0.75f * Mathf.Sin(Mathf.Pi * t / 0.72f) : 0.14f * Mathf.Sin(Mathf.Pi * (t - 0.72f) / 0.28f);
            float k = Mathf.Min(1f, t / 0.72f);
            var flat = from.Lerp(end, k);
            o.Position = new Vector3(flat.X, Mathf.Lerp(from.Y, end.Y, Mathf.Min(1f, k * 1.2f)) + hop, flat.Z);
            o.Rotation = new Vector3(t * 9f, t * 5f, 0);
        }), 0f, 1f, 0.7f);
        tw.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(o)) { o.Position = end; o.Rotation = Vector3.Zero; } }));
    }
}
