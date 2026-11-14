using System;
using Steamworks;

namespace AppStein;

public struct LobbyJoin
{
    public bool Ok;
    public string Error;
    public ulong LobbyId, Host;
    public bool HasPassword;
    public string Version;
}

/// <summary>
/// One Steam lobby: created by the host (friends only or open to anyone with the code), joined by clients. The lobby carries only a few facts
/// (host Steam ID, game version, password flag, player count, in-match flag); the game traffic itself goes over SteamPeer.
/// Every method catches Steam errors and answers through its callback; nothing throws outward and every request has a timeout (Tick).
/// NEVER EXECUTED AGAINST REAL STEAM in development.
/// </summary>
public sealed class SteamLobby : IDisposable
{
    const double RequestTimeout = 15;
    public ulong Id { get; private set; }
    public bool Active => Id != 0;
    public bool IsOwner { get; private set; }

    CallResult<LobbyCreated_t> _created;
    CallResult<LobbyEnter_t> _entered;
    Callback<GameLobbyJoinRequested_t> _joinReq;
    Callback<GameRichPresenceJoinRequested_t> _richJoin;
    Action<bool, string> _createDone;
    Action<LobbyJoin> _joinDone;
    double _deadline = -1, _clock;

    /// <summary>A friend accepted an invite / clicked "Join Game" while the game is running. Argument = lobby id.</summary>
    public event Action<ulong> JoinRequested;

    public SteamLobby()
    {
        _created = CallResult<LobbyCreated_t>.Create(OnCreated);
        _entered = CallResult<LobbyEnter_t>.Create(OnEntered);
        _joinReq = Callback<GameLobbyJoinRequested_t>.Create(d => JoinRequested?.Invoke(d.m_steamIDLobby.m_SteamID));
        _richJoin = Callback<GameRichPresenceJoinRequested_t>.Create(d =>
        {
            if (SteamService.TryParseConnect(d.m_rgchConnect, out var id)) JoinRequested?.Invoke(id);
        });
    }

    /// <summary>Call every frame (answers that never come back are turned into an error after 15 s).</summary>
    public void Tick(double delta)
    {
        _clock += delta;
        if (_deadline < 0 || _clock < _deadline) return;
        _deadline = -1;
        if (_createDone != null) { var d = _createDone; _createDone = null; d(false, "Could not create the Steam room (Steam did not answer)."); }
        else if (_joinDone != null) { var d = _joinDone; _joinDone = null; d(new LobbyJoin { Error = "Could not join: Steam did not answer. Check that Steam is online." }); }
    }

    void Arm() => _deadline = _clock + RequestTimeout;

    // ------------------------------------------------------------ host
    public void Create(bool anyoneWithCode, Action<bool, string> done)
    {
        try
        {
            _createDone = done; Arm();
            var call = SteamMatchmaking.CreateLobby(anyoneWithCode ? ELobbyType.k_ELobbyTypePublic : ELobbyType.k_ELobbyTypeFriendsOnly, SteamConfig.LobbyMaxMembers);
            _created.Set(call);
        }
        catch (Exception e) { _createDone = null; _deadline = -1; done(false, "Could not create the Steam room (" + e.GetType().Name + ")."); }
    }

    void OnCreated(LobbyCreated_t r, bool ioFailure)
    {
        var done = _createDone; _createDone = null; _deadline = -1;
        if (done == null) { if (!ioFailure && r.m_eResult == EResult.k_EResultOK) { try { SteamMatchmaking.LeaveLobby(new CSteamID(r.m_ulSteamIDLobby)); } catch { } } return; }   // cancelled meanwhile
        if (ioFailure || r.m_eResult != EResult.k_EResultOK)
        {
            done(false, r.m_eResult == EResult.k_EResultLimitExceeded ? "Could not create the Steam room: too many rooms created recently. Wait a minute." : "Could not create the Steam room.");
            return;
        }
        Id = r.m_ulSteamIDLobby; IsOwner = true;
        try
        {
            var id = new CSteamID(Id);
            SteamMatchmaking.SetLobbyData(id, "host", SteamService.MySteamId.ToString());
            SteamMatchmaking.SetLobbyData(id, "ver", SteamConfig.GameVersion);
            SteamMatchmaking.SetLobbyData(id, "game", "appstein");
            SteamMatchmaking.SetLobbyJoinable(id, true);
            SteamFriends.SetRichPresence("connect", "+connect_lobby " + Id);
            SteamFriends.SetRichPresence("status", "Hosting a room");
            done(true, null);
        }
        catch (Exception e) { Leave(); done(false, "Could not create the Steam room (" + e.GetType().Name + ")."); }
    }

    /// <summary>Host: publish the facts a joiner reads before connecting.</summary>
    public void Publish(bool hasPassword, int players, bool inMatch)
    {
        if (!Active || !IsOwner) return;
        try
        {
            var id = new CSteamID(Id);
            SteamMatchmaking.SetLobbyData(id, "pw", hasPassword ? "1" : "0");
            SteamMatchmaking.SetLobbyData(id, "players", players.ToString());
            SteamMatchmaking.SetLobbyData(id, "inmatch", inMatch ? "1" : "0");
        }
        catch (Exception e) { Util.Log("STEAM: lobby data failed: " + e.Message); }
    }

    /// <summary>Opens the Steam overlay's "invite to game" window for this lobby.</summary>
    public bool Invite()
    {
        if (!Active) return false;
        try { SteamFriends.ActivateGameOverlayInviteDialog(new CSteamID(Id)); return true; }
        catch (Exception e) { Util.Log("STEAM: invite dialog failed: " + e.Message); return false; }
    }

    // ------------------------------------------------------------ joiner
    public void Join(ulong lobbyId, Action<LobbyJoin> done)
    {
        try
        {
            _joinDone = done; Arm();
            _entered.Set(SteamMatchmaking.JoinLobby(new CSteamID(lobbyId)));
        }
        catch (Exception e) { _joinDone = null; _deadline = -1; done(new LobbyJoin { Error = "Could not join the Steam room (" + e.GetType().Name + ")." }); }
    }

    void OnEntered(LobbyEnter_t r, bool ioFailure)
    {
        var done = _joinDone; _joinDone = null; _deadline = -1;
        if (done == null) { if (!ioFailure && r.m_EChatRoomEnterResponse == 1) { try { SteamMatchmaking.LeaveLobby(new CSteamID(r.m_ulSteamIDLobby)); } catch { } } return; }   // cancelled meanwhile
        if (ioFailure) { done(new LobbyJoin { Error = "Could not join: no answer from Steam. Check that Steam is online." }); return; }
        if (r.m_EChatRoomEnterResponse != 1)
        {
            done(new LobbyJoin { Error = EnterError(r.m_EChatRoomEnterResponse) });
            return;
        }
        Id = r.m_ulSteamIDLobby; IsOwner = false;
        try
        {
            var id = new CSteamID(Id);
            string host = SteamMatchmaking.GetLobbyData(id, "host");
            if (!ulong.TryParse(host, out var hostId) || hostId == 0)
            {
                // the lobby owner is the host unless the host already left
                hostId = SteamMatchmaking.GetLobbyOwner(id).m_SteamID;
                if (hostId == 0) { Leave(); done(new LobbyJoin { Error = "Could not join: room not found (the host may have left)." }); return; }
            }
            done(new LobbyJoin
            {
                Ok = true, LobbyId = Id, Host = hostId,
                HasPassword = SteamMatchmaking.GetLobbyData(id, "pw") == "1",
                Version = SteamMatchmaking.GetLobbyData(id, "ver") ?? "",
            });
        }
        catch (Exception e) { Leave(); done(new LobbyJoin { Error = "Could not join the Steam room (" + e.GetType().Name + ")." }); }
    }

    /// <summary>Steam's EChatRoomEnterResponse as a sentence.</summary>
    public static string EnterError(uint code) => code switch
    {
        2 => "Could not join: room not found. Check the code, and that the host is still in the game.",
        3 => "Could not join: this room is friends only and you are not on the host's Steam friends list.",
        4 => $"Could not join: the room is full ({SteamConfig.LobbyMaxMembers} miners).",
        6 or 9 => "Could not join: you are blocked from this room.",
        10 or 11 => "Could not join: one of you has blocked the other on Steam.",
        7 => "Could not join: your Steam account is limited and cannot join rooms yet.",
        _ => $"Could not join the Steam room (Steam error {code}).",
    };

    public void Leave()
    {
        _createDone = null; _joinDone = null; _deadline = -1;
        if (Id == 0) return;
        ulong id = Id; Id = 0; IsOwner = false;
        try
        {
            SteamMatchmaking.LeaveLobby(new CSteamID(id));
            SteamFriends.ClearRichPresence();
        }
        catch (Exception e) { Util.Log("STEAM: leaving the lobby failed: " + e.Message); }
    }

    public void Dispose()
    {
        Leave();
        try { _created?.Dispose(); _entered?.Dispose(); _joinReq?.Dispose(); _richJoin?.Dispose(); } catch { }
        _created = null; _entered = null; _joinReq = null; _richJoin = null;
    }
}
