using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Steamworks;

namespace AppStein;

public enum FriendState { InGame = 0, Online = 1, Away = 2, Offline = 3 }

public struct FriendInfo
{
    public ulong Id;
    public string Name;
    public FriendState State;
    /// <summary>"In this game", "Online", "Away", "Offline", or "In another game".</summary>
    public string Detail;
}

public struct InviteInfo
{
    public ulong FromId;
    public string FromName;
    public ulong Lobby;
}

/// <summary>The friends list and invites, behind an interface so the sorting / filtering / banner logic is tested without Steam (see <see cref="FakeFriendsSource"/>).</summary>
public interface IFriendsSource
{
    bool Available { get; }
    /// <summary>Steam overlay can open (only when Steam started the process; false for a loose exe).</summary>
    bool OverlayEnabled { get; }
    List<FriendInfo> GetFriends();
    /// <summary>Sends a Steam lobby invite to one friend. False when Steam refused.</summary>
    bool InviteToLobby(ulong lobby, ulong friend);
    /// <summary>Opens the Steam overlay's own invite dialog (only meaningful when <see cref="OverlayEnabled"/>).</summary>
    bool OpenOverlayInvite(ulong lobby);
    /// <summary>A friend's name or presence changed (Steam PersonaStateChange_t).</summary>
    event Action Changed;
    /// <summary>A friend invited us to their lobby while the game is running (Steam LobbyInvite_t).</summary>
    event Action<InviteInfo> InviteReceived;
}

/// <summary>Real friends list through Steamworks. NEVER EXECUTED AGAINST REAL STEAM in development. Every Steam call is under try/catch.</summary>
public sealed class SteamFriendsSource : IFriendsSource, IDisposable
{
    Callback<PersonaStateChange_t> _persona;
    Callback<LobbyInvite_t> _invite;
    public bool Available => SteamService.Available;
    public event Action Changed;
    public event Action<InviteInfo> InviteReceived;

    public SteamFriendsSource()
    {
        try
        {
            _persona = Callback<PersonaStateChange_t>.Create(_ => Changed?.Invoke());
            _invite = Callback<LobbyInvite_t>.Create(d =>
            {
                string name = "A friend";
                try { var n = SteamFriends.GetFriendPersonaName(new CSteamID(d.m_ulSteamIDUser)); if (!string.IsNullOrEmpty(n)) name = n; } catch { }
                InviteReceived?.Invoke(new InviteInfo { FromId = d.m_ulSteamIDUser, FromName = name, Lobby = d.m_ulSteamIDLobby });
            });
        }
        catch (Exception e) { Util.Log("STEAM: friends callbacks failed: " + e.Message); }
    }

    public bool OverlayEnabled { get { try { return Available && Overlay(); } catch { return false; } } }
    [MethodImpl(MethodImplOptions.NoInlining)] static bool Overlay() => SteamUtils.IsOverlayEnabled();

    public List<FriendInfo> GetFriends()
    {
        try { return Read(); }
        catch (Exception e) { Util.Log("STEAM: friends list failed: " + e.Message); return new List<FriendInfo>(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static List<FriendInfo> Read()
    {
        var l = new List<FriendInfo>();
        if (!SteamService.Available) return l;
        const EFriendFlags flags = EFriendFlags.k_EFriendFlagImmediate;
        int n = SteamFriends.GetFriendCount(flags);
        for (int i = 0; i < n; i++)
        {
            var id = SteamFriends.GetFriendByIndex(i, flags);
            string name = SteamFriends.GetFriendPersonaName(id);
            var ps = SteamFriends.GetFriendPersonaState(id);
            bool inThis = false, inOther = false;
            if (SteamFriends.GetFriendGamePlayed(id, out FriendGameInfo_t gi))
            {
                if (gi.m_gameID.AppID().m_AppId == SteamConfig.AppId) inThis = true; else inOther = true;
            }
            FriendState st = inThis ? FriendState.InGame : ps == EPersonaState.k_EPersonaStateOffline ? FriendState.Offline
                : ps == EPersonaState.k_EPersonaStateAway || ps == EPersonaState.k_EPersonaStateSnooze ? FriendState.Away : FriendState.Online;
            l.Add(new FriendInfo { Id = id.m_SteamID, Name = string.IsNullOrEmpty(name) ? "?" : name, State = st, Detail = FriendsModel.DetailFor(st, inOther) });
        }
        return l;
    }

    public bool InviteToLobby(ulong lobby, ulong friend)
    {
        try { return Invite(lobby, friend); }
        catch (Exception e) { Util.Log("STEAM: invite failed: " + e.Message); return false; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static bool Invite(ulong lobby, ulong friend) => SteamMatchmaking.InviteUserToLobby(new CSteamID(lobby), new CSteamID(friend));

    public bool OpenOverlayInvite(ulong lobby)
    {
        try { return Open(lobby); } catch { return false; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static bool Open(ulong lobby) { SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(lobby)); return true; }

    public void Dispose() { try { _persona?.Dispose(); _invite?.Dispose(); } catch { } _persona = null; _invite = null; }
}

/// <summary>In-memory friends for `--steamcheck` and the `--fakesteam` screenshots.</summary>
public sealed class FakeFriendsSource : IFriendsSource
{
    public List<FriendInfo> Friends = new();
    public bool Overlay;
    public bool InviteResult = true;
    public readonly List<(ulong lobby, ulong friend)> Invited = new();
    public int OverlayOpened;
    public bool Available => true;
    public bool OverlayEnabled => Overlay;
    public event Action Changed;
    public event Action<InviteInfo> InviteReceived;
    public List<FriendInfo> GetFriends() => new(Friends);
    public bool InviteToLobby(ulong lobby, ulong friend) { Invited.Add((lobby, friend)); return InviteResult; }
    public bool OpenOverlayInvite(ulong lobby) { OverlayOpened++; return true; }
    public void RaiseChanged() => Changed?.Invoke();
    public void RaiseInvite(InviteInfo i) => InviteReceived?.Invoke(i);
    public FakeFriendsSource Add(ulong id, string name, FriendState st, string detail = null) { Friends.Add(new FriendInfo { Id = id, Name = name, State = st, Detail = detail ?? FriendsModel.DetailFor(st, false) }); return this; }

    /// <summary>A believable list for the screenshots.</summary>
    public static FakeFriendsSource Demo()
    {
        var f = new FakeFriendsSource { Overlay = false };
        f.Add(1, "Brynhild", FriendState.InGame).Add(2, "Cormac", FriendState.Online).Add(3, "dwarf_lord_88", FriendState.Online, "In another game")
         .Add(4, "Eilidh", FriendState.Online).Add(5, "Fenwick", FriendState.Away).Add(6, "Gudrun", FriendState.Away).Add(7, "Hobb", FriendState.InGame)
         .Add(8, "Ingrid", FriendState.Online).Add(9, "Jarl", FriendState.Offline).Add(10, "Kestrel", FriendState.Offline).Add(11, "Lorcan", FriendState.Online)
         .Add(12, "Mab", FriendState.Offline).Add(13, "Njord", FriendState.Away).Add(14, "Orla", FriendState.Online);
        return f;
    }
}

/// <summary>Sorting, filtering and invite-button state of the in-game friends panel. Pure logic, clock injected, no engine objects.</summary>
public sealed class FriendsModel
{
    public enum Btn { Invite, Invited, Failed, Disabled }
    public struct Row { public FriendInfo Friend; public Btn Button; }

    public const double InvitedSecs = 10, FailedSecs = 3, RefreshSecs = 3;
    readonly IFriendsSource _src;
    readonly Func<double> _now;
    readonly Func<ulong> _lobby;
    readonly Dictionary<ulong, double> _invitedUntil = new(), _failedUntil = new();
    List<FriendInfo> _all = new();
    double _nextRefresh;

    public string Filter = "";
    public bool ShowOffline;
    /// <summary>How many offline friends the current filter hides (for the "show offline" caption).</summary>
    public int HiddenOffline { get; private set; }

    public FriendsModel(IFriendsSource src, Func<double> clock, Func<ulong> lobbyId) { _src = src; _now = clock; _lobby = lobbyId; }

    public static string DetailFor(FriendState st, bool inOtherGame) => st switch
    {
        FriendState.InGame => "In this game",
        FriendState.Online => inOtherGame ? "In another game" : "Online",
        FriendState.Away => "Away",
        _ => "Offline",
    };

    public void Refresh() { _all = _src?.GetFriends() ?? new List<FriendInfo>(); _nextRefresh = _now() + RefreshSecs; }
    /// <summary>Call every frame while the panel is open: refreshes every few seconds. Returns true when it did.</summary>
    public bool Tick() { if (_now() < _nextRefresh) return false; Refresh(); return true; }

    bool Matches(FriendInfo f) => Filter.Length == 0 || f.Name.Contains(Filter.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Friends in this game first, then online, then away, then (only with ShowOffline) offline; by name inside each group; filtered by name.</summary>
    public List<Row> Rows()
    {
        double now = _now();
        HiddenOffline = _all.Count(f => f.State == FriendState.Offline && Matches(f));
        var list = _all.Where(f => Matches(f) && (ShowOffline || f.State != FriendState.Offline))
            .OrderBy(f => (int)f.State).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Id)
            .Select(f => new Row { Friend = f, Button = ButtonFor(f.Id, now) }).ToList();
        return list;
    }

    Btn ButtonFor(ulong id, double now)
    {
        if (_lobby() == 0) return Btn.Disabled;
        if (_invitedUntil.TryGetValue(id, out var iu) && now < iu) return Btn.Invited;
        if (_failedUntil.TryGetValue(id, out var fu) && now < fu) return Btn.Failed;
        return Btn.Invite;
    }

    /// <summary>The Invite button of one row. Returns false when nothing was sent (no room yet, already invited, Steam refused).</summary>
    public bool Invite(ulong friend)
    {
        double now = _now();
        ulong lobby = _lobby();
        if (lobby == 0 || ButtonFor(friend, now) != Btn.Invite) return false;
        if (_src.InviteToLobby(lobby, friend)) { _invitedUntil[friend] = now + InvitedSecs; return true; }
        _failedUntil[friend] = now + FailedSecs;
        return false;
    }

    public static string Signature(List<Row> rows) => string.Join(";", rows.Select(r => $"{r.Friend.Id}:{(int)r.Friend.State}:{r.Friend.Name}:{(int)r.Button}"));
}

/// <summary>"Name invited you - Join / Dismiss" for 30 seconds. Pure logic with an injected clock.</summary>
public sealed class InviteBannerModel
{
    public const double ShowSecs = 30;
    readonly Func<double> _now;
    InviteInfo _cur; double _until = -1;
    public InviteBannerModel(Func<double> clock) { _now = clock; }

    public bool Active => _until > 0 && _now() < _until;
    public InviteInfo Current => _cur;
    public double SecondsLeft => Active ? _until - _now() : 0;

    /// <summary>An invite arrived. Ignored without a lobby or when it is the lobby we are already in. A newer invite replaces the one shown.</summary>
    public bool OnInvite(InviteInfo i, ulong currentLobby)
    {
        if (i.Lobby == 0 || i.Lobby == currentLobby) return false;
        _cur = i; _until = _now() + ShowSecs;
        return true;
    }

    public void Dismiss() { _until = -1; }

    /// <summary>The Join button: returns the lobby to join and clears the banner (0 when there is none).</summary>
    public ulong Join() { if (!Active) return 0; var l = _cur.Lobby; _until = -1; return l; }
}
