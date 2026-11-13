using Godot;

namespace AppStein;

/// <summary>Tiny settings singleton (saved to user://settings.cfg). The miner controller reads SensitivityRad/Fov from here.</summary>
public static class UserSettings
{
    const string Path = "user://settings.cfg";
    public static string PlayerName = "";
    public static string LastIp = "127.0.0.1";
    /// <summary>Mouse sensitivity multiplier (1 = default 0.0025 rad/pixel).</summary>
    public static float MouseSens = 1f;
    public static float Fov = 80f;
    public static float Volume = 0.8f;
    public static bool Fullscreen;
    public static bool FilmGrain = true;

    public static float SensitivityRad => 0.0025f * MouseSens;

    static bool _loaded;
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var cf = new ConfigFile();
        if (cf.Load(Path) != Error.Ok) return;
        PlayerName = (string)cf.GetValue("player", "name", "");
        LastIp = (string)cf.GetValue("player", "ip", "127.0.0.1");
        MouseSens = Mathf.Clamp((float)(double)cf.GetValue("input", "sens", 1.0), 0.2f, 4f);
        Fov = Mathf.Clamp((float)(double)cf.GetValue("video", "fov", 80.0), 60f, 110f);
        Volume = Mathf.Clamp((float)(double)cf.GetValue("audio", "volume", 0.8), 0f, 1f);
        Fullscreen = (bool)cf.GetValue("video", "fullscreen", false);
        FilmGrain = (bool)cf.GetValue("video", "grain", true);
    }

    public static void Save()
    {
        var cf = new ConfigFile();
        cf.SetValue("player", "name", PlayerName);
        cf.SetValue("player", "ip", LastIp);
        cf.SetValue("input", "sens", (double)MouseSens);
        cf.SetValue("video", "fov", (double)Fov);
        cf.SetValue("audio", "volume", (double)Volume);
        cf.SetValue("video", "fullscreen", Fullscreen);
        cf.SetValue("video", "grain", FilmGrain);
        cf.Save(Path);
    }

    /// <summary>Pushes volume / window mode / UI scaling to the engine.</summary>
    public static void Apply(Window window = null)
    {
        AudioServer.SetBusVolumeDb(0, Volume <= 0.001f ? -80f : Mathf.LinearToDb(Volume));
        if (DisplayServer.GetName() == "headless") return;
        var mode = Fullscreen ? DisplayServer.WindowMode.ExclusiveFullscreen : DisplayServer.WindowMode.Windowed;
        if (DisplayServer.WindowGetMode() != mode) DisplayServer.WindowSetMode(mode);
        if (window != null)
        {
            // UI scales with window height (design size 1280x720) and gains extra width on wide screens.
            window.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
            window.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
            window.ContentScaleSize = new Vector2I(1280, 720);
        }
    }
}
