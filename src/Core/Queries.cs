using Godot;

namespace AppStein;

/// <summary>
/// Pooled physics query parameter objects. A fresh <c>PhysicsRayQueryParameters3D</c> per raycast is a RefCounted that the .NET GC finalises late; at shutdown the
/// engine then aborts with "Leaked unsafe reference to object" (exit code 134). Every ray/shape query in the game takes its parameters from this small ring pool,
/// which is disposed explicitly when the root node leaves the tree. Use the result immediately (do not hold it across more than a handful of other Ray calls).
/// </summary>
public static class Queries
{
    const int PoolSize = 16;
    static readonly PhysicsRayQueryParameters3D[] _rays = new PhysicsRayQueryParameters3D[PoolSize];
    static readonly PhysicsShapeQueryParameters3D[] _shapes = new PhysicsShapeQueryParameters3D[4];
    static int _ri, _si;

    /// <summary>Ray parameters from <paramref name="from"/> to <paramref name="to"/> (all other fields reset to defaults).</summary>
    public static PhysicsRayQueryParameters3D Ray(Vector3 from, Vector3 to, uint mask = 0xFFFFFFFF)
    {
        var q = _rays[_ri];
        if (q == null || !GodotObject.IsInstanceValid(q)) q = _rays[_ri] = new PhysicsRayQueryParameters3D();
        _ri = (_ri + 1) % PoolSize;
        q.From = from; q.To = to; q.CollisionMask = mask;
        q.Exclude = new Godot.Collections.Array<Rid>();
        q.CollideWithAreas = false; q.CollideWithBodies = true; q.HitFromInside = false; q.HitBackFaces = true;
        return q;
    }

    public static PhysicsShapeQueryParameters3D Shape(Shape3D shape, Transform3D xf, uint mask)
    {
        var q = _shapes[_si];
        if (q == null || !GodotObject.IsInstanceValid(q)) q = _shapes[_si] = new PhysicsShapeQueryParameters3D();
        _si = (_si + 1) % _shapes.Length;
        q.Shape = shape; q.Transform = xf; q.CollisionMask = mask;
        q.Exclude = new Godot.Collections.Array<Rid>();
        q.CollideWithAreas = false; q.CollideWithBodies = true; q.Margin = 0f; q.Motion = Vector3.Zero;
        return q;
    }

    /// <summary>Called once when the game root exits the tree.</summary>
    public static void DisposeAll()
    {
        for (int i = 0; i < _rays.Length; i++) { _rays[i]?.Dispose(); _rays[i] = null; }
        for (int i = 0; i < _shapes.Length; i++) { _shapes[i]?.Dispose(); _shapes[i] = null; }
    }
}
