using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Per-player helmet colour. Same index on every peer because the roster (Net.Names, sorted by peer id) is identical everywhere.
/// The assignment is frozen while a match runs, so players leaving mid-match do not recolour the others.</summary>
public static class PlayerColors
{
    public static readonly Color[] Palette =
    {
        new(0.95f, 0.78f, 0.12f), // yellow
        new(0.90f, 0.25f, 0.22f), // red
        new(0.25f, 0.62f, 0.95f), // blue
        new(0.35f, 0.80f, 0.38f), // green
        new(0.95f, 0.50f, 0.15f), // orange
        new(0.72f, 0.40f, 0.92f), // purple
        new(0.30f, 0.85f, 0.85f), // cyan
        new(0.95f, 0.55f, 0.75f), // pink
    };
    public static readonly string[] ColorNames = { "Yellow", "Red", "Blue", "Green", "Orange", "Purple", "Cyan", "Pink" };

    static readonly Dictionary<int, int> _frozen = new();

    public static int IndexOf(int peer)
    {
        if (Game.I != null && Game.I.Phase != GamePhase.Lobby)
        {
            if (_frozen.TryGetValue(peer, out var f)) return f;
            var used = _frozen.Values.ToHashSet();
            int idx = Net.I.Names.Keys.ToList().IndexOf(peer); if (idx < 0) idx = 0;
            idx %= Palette.Length;
            while (used.Contains(idx) && used.Count < Palette.Length) idx = (idx + 1) % Palette.Length;
            _frozen[peer] = idx; return idx;
        }
        _frozen.Clear();
        int i = Net.I.Names.Keys.ToList().IndexOf(peer);
        return i < 0 ? 0 : i % Palette.Length;
    }

    public static Color Of(int peer) => Palette[IndexOf(peer)];
    public static string NameOf(int peer) => ColorNames[IndexOf(peer)];
}
