using Godot;

namespace AppStein;

/// <summary>Audio stub. UI/game events call Sfx.Play(name, pos) now; a later audio pass fills in the body.</summary>
public static class Sfx
{
    /// <summary>name e.g. "ui_click", "ui_hover", "deposit", "goblin_laugh", "goblin_growl", "hurt", "round_start", "warning". pos = world position, null for UI.</summary>
    public static void Play(string name, Vector3? pos = null) { }
}
