using System;
using System.Runtime.CompilerServices;
using Godot;
using Steamworks;

namespace AppStein;

/// <summary>
/// The single door to Steam. Every Steam library call in the game is behind this class (and SteamSockets / SteamLobby, which only exist when this reports Available).
/// Available = the Steam library loaded, the Steam client is running and SteamAPI.Init worked. Anything else (library missing, Steam not running, headless test run,
/// --nosteam) gives Available = false and a reason in Status; nothing in here ever throws outward.
/// As a node (child of Net) it calls SteamAPI.RunCallbacks every frame and SteamAPI.Shutdown when the game exits.
/// The App ID comes from steam_appid.txt next to the exe (see SteamConfig).
/// </summary>
public partial class SteamService : Node
{
    public static bool Available { get; private set; }
    /// <summary>Why Steam is (not) available, for the log and the check.</summary>
    public static string Status { get; private set; } = "not started";
    public static ulong MySteamId { get; private set; }
    public static string PersonaName { get; private set; } = "";
    /// <summary>--fakesteam: only makes the menus draw as if Steam were available (screenshots). Never used in real play.</summary>
    public static bool FakeUi => Util.HasArg("fakesteam");
    /// <summary>The UI shows the Steam flow (real Steam, or the screenshot fake).</summary>
    public static bool UiSteam => Available || FakeUi;
    static bool _tried, _cbFailed;

    /// <summary>No Steam at all: automated runs (--host/--join/--bot), headless runs and --nosteam.</summary>
    public static bool ShouldSkip(out string why)
    {
        why = null;
        if (Util.HasArg("nosteam")) why = "skipped (--nosteam)";
        else if (Util.Automated) why = "skipped (automated run)";
        else if (DisplayServer.GetName() == "headless") why = "skipped (headless run)";
        return why != null;
    }

    /// <summary>Try to start Steam once. Returns Available. force = ignore the skip rules (the self-check calls it that way to prove the failure path is clean).</summary>
    public static bool Init(bool force = false)
    {
        if (Available) return true;
        if (_tried && !force) return false;
        if (!force && ShouldSkip(out var why)) { Status = why; _tried = true; Util.Log("STEAM: " + why); return false; }
        _tried = true;
        try
        {
            Available = InitNative(out var msg);
            Status = msg;
        }
        catch (DllNotFoundException e) { Available = false; Status = "Steam library not found (" + e.Message.Split('\n')[0] + ")"; }
        catch (BadImageFormatException e) { Available = false; Status = "Steam library cannot be loaded here (" + e.Message.Split('\n')[0] + ")"; }
        catch (Exception e) { Available = false; Status = "Steam failed to start (" + e.GetType().Name + ": " + e.Message.Split('\n')[0] + ")"; }
        Util.Log("STEAM: " + (Available ? "ready as " + PersonaName : "not available: " + Status));
        return Available;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static bool InitNative(out string msg)
    {
        // Packsize.Test only checks that the managed structs have the layout the native library expects; DllCheck is left out on purpose
        // (it also compares the native version string and would refuse a slightly different steam_api64.dll).
        if (!Packsize.Test()) { msg = "Steamworks.NET struct layout check failed"; return false; }
        var r = SteamAPI.InitEx(out string err);
        if (r != ESteamAPIInitResult.k_ESteamAPIInitResult_OK)
        {
            msg = r == ESteamAPIInitResult.k_ESteamAPIInitResult_NoSteamClient ? "Steam is not running" : $"SteamAPI.Init failed ({r}): {err}";
            return false;
        }
        SteamNetworkingUtils.InitRelayNetworkAccess();   // start talking to the relay network early so the first connection is quick
        MySteamId = SteamUser.GetSteamID().m_SteamID;
        PersonaName = SteamFriends.GetPersonaName() ?? "";
        uint app = SteamUtils.GetAppID().m_AppId;
        msg = app == SteamConfig.AppId ? "ok" : $"ok, but Steam says App ID {app} while the game expects {SteamConfig.AppId} (steam_appid.txt out of date?)";
        return true;
    }

    /// <summary>Pump Steam's callbacks (status changes, lobby answers, invites). Safe to call any number of times per frame.</summary>
    public static void RunCallbacksNow()
    {
        if (!Available) return;
        try { Pump(); }
        catch (Exception e) { if (!_cbFailed) { _cbFailed = true; Util.Log("STEAM: RunCallbacks failed: " + e.Message); } }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Pump() => SteamAPI.RunCallbacks();

    public static void Shutdown()
    {
        if (!Available) return;
        Available = false;
        try { ShutdownNative(); } catch (Exception e) { Util.Log("STEAM: shutdown failed: " + e.Message); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ShutdownNative() => SteamAPI.Shutdown();

    /// <summary>The lobby id from a "+connect_lobby 109775..." launch argument (game started by accepting a Steam invite), or 0.</summary>
    public static ulong LaunchLobbyId()
    {
        var all = OS.GetCmdlineArgs();
        for (int i = 0; i < all.Length - 1; i++)
            if (all[i] == "+connect_lobby" && ulong.TryParse(all[i + 1], out var id) && id != 0) return id;
        return 0;
    }

    /// <summary>Parses a rich-presence connect string such as "+connect_lobby 109775...".</summary>
    public static bool TryParseConnect(string s, out ulong lobbyId)
    {
        lobbyId = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var p = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < p.Length - 1; i++)
            if (p[i] == "+connect_lobby" && ulong.TryParse(p[i + 1], out lobbyId) && lobbyId != 0) return true;
        return false;
    }

    public override void _Process(double delta) => RunCallbacksNow();

    public override void _ExitTree()
    {
        if (Available) Shutdown();
    }
}
