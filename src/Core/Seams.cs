using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

namespace AppStein;

/// <summary>Physics layer bits shared by all workstreams.</summary>
public static class Layers
{
    public const uint World = 1, Miners = 2, Solid = 4, Pickup = 8;
    /// <summary>Thin walls only the Ethereal stein passes. Miners collide with it unless ethereal is active.</summary>
    public const uint ThinWall = 16;
    public const uint Monsters = 32;
}

/// <summary>
/// Put on a static class with a `public static void Register()` method. Register() runs once on every peer at startup.
/// Use it to add NetObject.Factories entries, Game.MarkerSpawners handlers, steins, utilities, event subscriptions.
/// Each workstream has its own module class in its own folder, so nobody edits shared registration code.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ContentModuleAttribute : Attribute { }

public partial class Game
{
    /// <summary>Server: called for every cave marker of that Kind when a round is populated. (game, room, marker, rng)</summary>
    public static readonly Dictionary<string, Action<Game, RoomInfo, SpawnMarker, RandomNumberGenerator>> MarkerSpawners = new();

    /// <summary>Server: fired after all markers were spawned for a round.</summary>
    public static event Action<Game, RandomNumberGenerator> RoundPopulated;

    /// <summary>Server: something made noise (mining, dynamite, sprinting, ghost pebble...). loudness ~ audible radius in metres.</summary>
    public static event Action<Vector3, float, ServerPlayer> NoiseMade;
    public static void ServerNoise(Vector3 pos, float loudness, ServerPlayer by = null)
    {
        if (by != null) loudness *= by.Stats.NoiseMul;
        NoiseMade?.Invoke(pos, loudness, by);
    }

    static bool _modulesLoaded;
    public static void LoadContentModules()
    {
        if (_modulesLoaded) return;
        _modulesLoaded = true;
        var types = Assembly.GetExecutingAssembly().GetTypes()
            .Where(t => t.GetCustomAttribute<ContentModuleAttribute>() != null).OrderBy(t => t.FullName);
        foreach (var t in types)
        {
            var m = t.GetMethod("Register", BindingFlags.Public | BindingFlags.Static);
            if (m == null) { GD.PushError($"ContentModule {t.Name} has no public static Register()"); continue; }
            m.Invoke(null, null);
        }
    }

    void RunMarkerSpawners(RandomNumberGenerator rng)
    {
        foreach (var room in World.Cave.Rooms)
            foreach (var m in room.Markers)
                if (MarkerSpawners.TryGetValue(m.Kind, out var f)) f(this, room, m, rng);
        RoundPopulated?.Invoke(this, rng);
    }
}
