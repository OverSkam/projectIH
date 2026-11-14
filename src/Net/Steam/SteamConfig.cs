namespace AppStein;

/// <summary>
/// The Steam App ID the game runs under. ONE place for the number in code (the other place is steam_appid.txt in the project root,
/// which tools/build_windows.sh copies next to the exe and checks against this constant).
/// 480 is Valve's public test app "Spacewar": while the game has no store page of its own, Steam shows it as "Spacewar".
/// WHEN YOU HAVE YOUR OWN APP ID: change the number below AND the number inside steam_appid.txt (see docs/ARCHITECTURE.md, "Switching to a real App ID").
/// </summary>
public static class SteamConfig
{
    public const uint AppId = 480;
    /// <summary>Game version stored in the Steam lobby; a joiner with a different version is refused with a clear message.</summary>
    public const string GameVersion = "0.9";
    /// <summary>Steam Networking Sockets "virtual port" the host listens on (any number, host and joiner must agree).</summary>
    public const int VirtualPort = 0;
    public const int LobbyMaxMembers = 8;
}
