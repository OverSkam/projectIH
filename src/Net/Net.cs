using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace AppStein;

/// <summary>Autoload: ENet connection + lobby roster. Game (autoload) owns everything about matches.</summary>
public partial class Net : Node
{
    public static Net I;
    public const int DefaultPort = 7777, MaxPeers = 8;
    /// <summary>Players in a room, the host included. ENet gets more slots than this so a full room can answer "room full" instead of timing out.</summary>
    public const int MaxPlayers = 8;
    const int Slots = 16;
    const double JoinTimeout = 10, SteamJoinTimeout = 25, AuthTimeoutSecs = 6;
    const int WrongTriesPerMinute = 5;
    // handshake packets (SceneMultiplayer authentication channel): 1 = server challenge, 2 = client answer, 3 = refusal with a reason
    const byte PktChallenge = 1, PktAnswer = 2, PktReject = 3;

    public string LocalName = "Miner";
    /// <summary>peer id -> display name (all connected, registered peers). Identical on every peer.</summary>
    public readonly SortedDictionary<int, string> Names = new();

    public event Action LobbyChanged;
    public event Action Connected;           // client: connected + registered
    public event Action ConnectionFailed;
    public event Action ServerLost;          // client: host went away / we left
    public event Action<int> PeerLeft;       // server only
    public event Action<int> PeerRegistered; // server only

    /// <summary>Hosting: the room password ("" = open room). Joining: the password typed by the player.</summary>
    public string RoomPassword = "";
    /// <summary>Server: the room has a password. Client: the host told us during the handshake that it has one.</summary>
    public bool HasPassword;
    /// <summary>Why the last join failed (sentence for the player).</summary>
    public string FailReason = "";
    /// <summary>The port a client is joining / the server hosts.</summary>
    public int RoomPort = DefaultPort;
    public PortOpener Room;
    /// <summary>The room runs over Steam (SteamPeer + a Steam lobby) instead of ENet.</summary>
    public bool SteamMode;
    /// <summary>Host: "Anyone with the code" (public lobby) instead of friends only.</summary>
    public bool SteamAnyone;
    /// <summary>Steam lobby of the current room (host and joiners); null while Steam is not available.</summary>
    public SteamLobby Lobby;
    /// <summary>Friends list and invites (real Steam, or the fake for --fakesteam screenshots); null without Steam.</summary>
    public IFriendsSource Friends;
    /// <summary>The Steam lobby of the current room (0 = none yet). Invites go to this lobby.</summary>
    public ulong CurrentLobbyId => Lobby != null && Lobby.Active ? Lobby.Id : (DebugFakeCode != null ? 1UL : 0UL);
    /// <summary>Host: the lobby is created and the room code exists.</summary>
    public bool SteamRoomReady => SteamMode && ((Lobby != null && Lobby.Active) || DebugFakeCode != null);
    public string SteamRoomCode => DebugFakeCode ?? (SteamRoomReady ? SteamLobbyCode.Encode(Lobby.Id) : null);
    /// <summary>Screenshot staging only (--screen=steamroom): pretend the Steam room exists.</summary>
    public string DebugFakeCode;
    public void DebugSteamRoom(string code, bool anyone) { SteamMode = true; SteamAnyone = anyone; DebugFakeCode = code; LobbyChanged?.Invoke(); }
    /// <summary>The last Steam lobby we tried to join (the menu puts its code back into the Join field so a password can be added).</summary>
    public ulong LastSteamLobby;
    /// <summary>A Steam join is under way before the transport exists (asking Steam for the room).</summary>
    public bool JoiningSteam => _joining && !Online;
    /// <summary>One-line messages for the menu (host-side failures such as "Could not create the Steam room").</summary>
    public event Action<string> Notice;

    readonly HashSet<int> _verified = new(), _pending = new();
    readonly Dictionary<int, byte[]> _nonces = new();
    readonly Dictionary<int, string> _authNames = new();
    readonly Dictionary<string, List<double>> _fails = new();
    readonly Dictionary<string, double> _banUntil = new();
    bool _joining; double _joinStart; string _joinPassword = "", _joinTarget = "";
    string _refusal;
    static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>True for the host itself and every peer that passed the password check (central guard for RPC entry points).</summary>
    public bool IsVerified(int peer) => peer == 1 || _verified.Contains(peer);

    public bool Online => Multiplayer.MultiplayerPeer != null && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;
    public bool IsServer => Online && Multiplayer.IsServer();
    public int MyId => Online ? Multiplayer.GetUniqueId() : 1;

    public override void _Ready()
    {
        I = this;
        Room = new PortOpener { Name = "Room" };
        AddChild(Room);
        var sm = (SceneMultiplayer)Multiplayer;
        sm.AuthCallback = Callable.From<long, byte[]>(OnAuth);
        sm.AuthTimeout = AuthTimeoutSecs;
        sm.PeerAuthenticating += OnPeerAuthenticating;
        sm.PeerAuthenticationFailed += OnAuthFailed;
        Multiplayer.PeerConnected += id => { };
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += () => FailJoin(ConnectFailText());
        Multiplayer.ServerDisconnected += OnServerDisconnected;
        // Steam: one service node (callbacks every frame, shutdown on exit). Nothing happens when Steam is not available.
        if (SteamService.Init())
        {
            AddChild(new SteamService { Name = "Steam" });
            Lobby = new SteamLobby();
            Lobby.JoinRequested += id => JoinSteam(id, "");
            Friends = new SteamFriendsSource();
        }
        else if (SteamService.FakeUi) Friends = FakeFriendsSource.Demo();   // --fakesteam screenshots only
    }

    public override void _Notification(int what)
    {
        // Closing the window while hosting/joining: tell the other side right away instead of letting it time out.
        if (what == NotificationWMCloseRequest) Disconnect();
    }

    public override void _Process(double delta)
    {
        Lobby?.Tick(delta);
        if (_joining && Now - _joinStart > (SteamMode || JoiningSteam ? SteamJoinTimeout : JoinTimeout))
            FailJoin(SteamMode || JoiningSteam ? "Could not connect to the host through Steam (no answer). Check that the host is still in the game and try again." : $"Could not reach the host ({_joinTarget}). Check the code, and that the host has started the room.");
    }

    public override void _ExitTree()
    {
        bool hadSteam = SteamMode;
        if (SteamMode) { try { Multiplayer.MultiplayerPeer?.Close(); } catch { } }
        Lobby?.Dispose();
        (Friends as IDisposable)?.Dispose();
        if (hadSteam) { SteamService.RunCallbacksNow(); System.Threading.Thread.Sleep(150); }   // let the "room closed" messages leave before Steam shuts down
        Room?.Stop();
        Room?.WaitForCleanup(1500);
    }

    public Error Host(int port = DefaultPort, string password = "")
    {
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(port, Slots);
        if (err != Error.Ok) { Util.Log("ERROR: host failed " + err); return err; }
        Multiplayer.MultiplayerPeer = peer;
        RoomPassword = password ?? ""; HasPassword = RoomPassword != ""; RoomPort = port;
        _verified.Clear(); _pending.Clear(); _nonces.Clear(); _authNames.Clear(); _fails.Clear(); _banUntil.Clear();
        Names.Clear();
        Names[1] = Unique(LocalName);
        Util.Log($"NET: hosting on port {port}{(HasPassword ? " (password protected)" : "")}");
        Room.Start(port);
        LobbyChanged?.Invoke();
        return Error.Ok;
    }

    public Error Join(string ip, int port = DefaultPort, string password = "")
    {
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(ip, port);
        if (err != Error.Ok) { Util.Log("ERROR: join failed " + err); return err; }
        Multiplayer.MultiplayerPeer = peer;
        _joinPassword = password ?? ""; HasPassword = false; RoomPort = port; FailReason = ""; _refusal = null;
        _joining = true; _joinStart = Now; _joinTarget = $"{ip}:{port}"; LastSteamLobby = 0;
        Names.Clear();
        Util.Log($"NET: joining {ip}:{port}");
        return Error.Ok;
    }

    // ============================================================ Steam rooms (SteamPeer transport + Steam lobby)
    bool _publishHooked;

    string ConnectFailText()
    {
        if (!SteamMode) return "Could not reach the host. Check the code, and that the host has started the room.";
        var why = (Multiplayer.MultiplayerPeer as SteamPeer)?.EndReason;
        return "Could not connect to the host through Steam" + (string.IsNullOrEmpty(why) ? "." : $" ({why}).") + " Check that the host is still in the game and try again.";
    }

    /// <summary>Host a room through Steam: opens the Steam listen socket at once and creates the lobby; the room code appears when Steam answers.</summary>
    public Error HostSteam(string password = "", bool anyoneWithCode = false)
    {
        if (!SteamService.Available || Lobby == null) { Notice?.Invoke("Steam is not running — start Steam to invite friends."); return Error.Unavailable; }
        if (Online) return Error.Busy;
        SteamPeer peer = null; Error err;
        try { peer = new SteamPeer(); err = peer.CreateHost(); }
        catch (Exception e) { Util.Log("ERROR: Steam host failed: " + e.Message); err = Error.CantCreate; }
        if (err != Error.Ok) { Util.Log("ERROR: Steam host failed " + err); Notice?.Invoke("Could not create the Steam room."); return err; }
        Multiplayer.MultiplayerPeer = peer;
        RoomPassword = password ?? ""; HasPassword = RoomPassword != ""; RoomPort = 0;
        SteamMode = true; SteamAnyone = anyoneWithCode;
        _verified.Clear(); _pending.Clear(); _nonces.Clear(); _authNames.Clear(); _fails.Clear(); _banUntil.Clear();
        Names.Clear();
        Names[1] = Unique(LocalName);
        Util.Log($"NET: hosting through Steam ({(anyoneWithCode ? "anyone with the code" : "friends only")}){(HasPassword ? " (password protected)" : "")}");
        LobbyChanged?.Invoke();
        Lobby.Create(anyoneWithCode, (ok, msg) =>
        {
            if (!SteamMode || !IsServer) return;   // cancelled meanwhile
            if (!ok)
            {
                Util.Log("NET: " + msg);
                Game.I.LeaveToMenu();
                Notice?.Invoke(msg);
                return;
            }
            Util.Log("NET: Steam room ready, code " + SteamRoomCode);
            if (Game.I != null && !_publishHooked) { Game.I.PhaseChanged += PublishLobby; _publishHooked = true; }
            PublishLobby();
            LobbyChanged?.Invoke();
        });
        return Error.Ok;
    }

    void PublishLobby()
    {
        if (SteamRoomReady && IsServer) Lobby.Publish(HasPassword, Names.Count, Game.I != null && Game.I.Phase != GamePhase.Lobby);
    }

    /// <summary>Join a Steam room by lobby id (typed code, accepted invite, launch argument). Leaves the current room first. Failures arrive as ConnectionFailed + FailReason.</summary>
    public void JoinSteam(ulong lobbyId, string password = "")
    {
        if (!SteamService.Available || Lobby == null) { Notice?.Invoke("Steam is not running — start Steam to join this room."); return; }
        if (Online || _joining) Game.I.LeaveToMenu();   // an invite accepted while in a room: leave it first
        LastSteamLobby = lobbyId;
        _joinPassword = password ?? ""; HasPassword = false; FailReason = ""; _refusal = null;
        _joining = true; _joinStart = Now; _joinTarget = "Steam room";
        SteamMode = true;
        Names.Clear();
        Util.Log("NET: joining Steam room " + lobbyId);
        LobbyChanged?.Invoke();   // the menu switches to "Connecting..."
        Lobby.Join(lobbyId, OnLobbyJoined);
    }

    void OnLobbyJoined(LobbyJoin j)
    {
        if (!_joining) { Lobby?.Leave(); return; }
        if (!j.Ok) { FailJoin(j.Error); return; }
        if (!string.IsNullOrEmpty(j.Version) && j.Version != SteamConfig.GameVersion)
        { FailJoin($"Could not join: the host runs game version {j.Version} and you have {SteamConfig.GameVersion}. Both need the same version."); return; }
        if (j.HasPassword && _joinPassword == "")
        { HasPassword = true; FailJoin("This room needs a password. Type it under Join and press Join game."); return; }
        SteamPeer peer = null; Error err;
        try { peer = new SteamPeer(); err = peer.CreateClient(j.Host); }
        catch (Exception e) { Util.Log("ERROR: Steam join failed: " + e.Message); err = Error.CantConnect; }
        if (err != Error.Ok) { FailJoin("Could not connect to the host through Steam."); return; }
        Multiplayer.MultiplayerPeer = peer;
        _joinStart = Now;
        LobbyChanged?.Invoke();
    }

    /// <summary>Close the connection (host: everyone else gets ServerDisconnected).</summary>
    public void Disconnect()
    {
        _joining = false;
        _verified.Clear(); _pending.Clear(); _nonces.Clear(); _authNames.Clear();
        Room?.Stop();
        if (SteamMode || (Lobby?.Active ?? false)) Lobby?.Leave();
        if (_publishHooked && Game.I != null) { Game.I.PhaseChanged -= PublishLobby; _publishHooked = false; }
        SteamMode = false; DebugFakeCode = null;
        if (Multiplayer.MultiplayerPeer != null && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer)
            Multiplayer.MultiplayerPeer.Close();
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        Names.Clear();
    }

    string Unique(string n)
    {
        n = (n ?? "").Trim();
        if (n.Length == 0) n = "Miner";
        if (n.Length > 16) n = n.Substring(0, 16);
        string r = n; int i = 2;
        while (Names.Values.Contains(r)) r = n + i++;
        return r;
    }

    void OnConnectedToServer()
    {
        _joining = false;
        Util.Log("NET: connected to server as " + Multiplayer.GetUniqueId());
        RpcId(1, nameof(RegisterName), LocalName);
    }

    void OnServerDisconnected()
    {
        Util.Log("NET: server disconnected");
        Disconnect();
        ServerLost?.Invoke();
    }

    void OnPeerDisconnected(long id)
    {
        if (!IsServer) return;
        _verified.Remove((int)id); _nonces.Remove((int)id); _authNames.Remove((int)id);
        Names.Remove((int)id);
        PeerLeft?.Invoke((int)id);
        Broadcast();
    }


    // ============================================================ handshake (password check before a peer exists for the game)
    // SceneMultiplayer authentication: until both sides call CompleteAuth the peer is "pending": it gets no broadcasts, cannot call any RPC,
    // and no player is created for it. The server sends a nonce; the client answers name + SHA-256(nonce + password).
    string PeerAddress(long id)
    {
        try
        {
            // Steam rooms: the Steam ID plays the role of the address (the password brake keys on it)
            if (Multiplayer.MultiplayerPeer is SteamPeer sp) { ulong sid = sp.GetRemoteSteamId((int)id); return sid != 0 ? "steam:" + sid : ""; }
            return (Multiplayer.MultiplayerPeer as ENetMultiplayerPeer)?.GetPeer((int)id)?.GetRemoteAddress() ?? "";
        }
        catch { return ""; }
    }

    void OnPeerAuthenticating(long id)
    {
        if (!IsServer) return;   // client: wait for the server's challenge
        int pid = (int)id;
        _pending.Add(pid);
        string addr = PeerAddress(id);
        if (_banUntil.TryGetValue(addr, out var until) && until > Now) { Reject(pid, "Too many wrong passwords from your address. Wait a minute and try again.", false); return; }
        if (_verified.Count + 1 >= MaxPlayers) { Reject(pid, $"The room is full ({MaxPlayers} miners).", false); return; }
        var nonce = RoomAuth.NewNonce();
        _nonces[pid] = nonce;
        var pkt = new byte[2 + nonce.Length];
        pkt[0] = PktChallenge; pkt[1] = (byte)(HasPassword ? 1 : 0);
        Buffer.BlockCopy(nonce, 0, pkt, 2, nonce.Length);
        ((SceneMultiplayer)Multiplayer).SendAuth(pid, pkt);
    }

    void OnAuth(long id, byte[] data)
    {
        if (data == null || data.Length == 0) return;
        var sm = (SceneMultiplayer)Multiplayer;
        int pid = (int)id;
        if (!IsServer)
        {
            if (data[0] == PktReject) { _refusal = Encoding.UTF8.GetString(data, 1, data.Length - 1); FailJoin(_refusal); return; }
            if (data[0] != PktChallenge || data.Length < 2 + RoomAuth.NonceLen) return;
            if (Util.HasArg("authstall")) return;   // test only: a client that never answers the challenge
            HasPassword = data[1] == 1;
            var nonce = new byte[RoomAuth.NonceLen]; Buffer.BlockCopy(data, 2, nonce, 0, nonce.Length);
            var hash = RoomAuth.Hash(nonce, _joinPassword);
            var name = Encoding.UTF8.GetBytes(LocalName ?? "");
            var ans = new byte[2 + hash.Length + name.Length];
            ans[0] = PktAnswer; ans[1] = (byte)(_joinPassword.Length > 0 ? 1 : 0);
            Buffer.BlockCopy(hash, 0, ans, 2, hash.Length); Buffer.BlockCopy(name, 0, ans, 2 + hash.Length, name.Length);
            sm.SendAuth(1, ans);
            sm.CompleteAuth(1);
            return;
        }
        if (data[0] != PktAnswer || !_pending.Contains(pid) || !_nonces.TryGetValue(pid, out var n) || data.Length < 2 + RoomAuth.HashLen) return;
        bool gave = data[1] == 1;
        var answer = new byte[RoomAuth.HashLen]; Buffer.BlockCopy(data, 2, answer, 0, answer.Length);
        string nm = Encoding.UTF8.GetString(data, 2 + RoomAuth.HashLen, data.Length - 2 - RoomAuth.HashLen);
        if (HasPassword && !RoomAuth.Verify(n, RoomPassword, answer))
        {
            Reject(pid, gave ? "Wrong password." : "This room needs a password.", true);
            return;
        }
        _authNames[pid] = nm; _verified.Add(pid); _pending.Remove(pid); _nonces.Remove(pid);
        _fails.Remove(PeerAddress(id));
        Util.Log($"NET: peer {pid} passed the handshake");
        sm.CompleteAuth(pid);
    }

    void OnAuthFailed(long id)
    {
        int pid = (int)id;
        if (IsServer) { if (_pending.Remove(pid)) Util.Log($"NET: peer {pid} never finished the handshake (timeout or left)"); _nonces.Remove(pid); return; }
        if (_joining && id == 1) FailJoin(_refusal ?? "The host did not accept the connection.");
    }

    /// <summary>Server: tell a pending peer why, then drop it a moment later (a disconnect right away would discard the message).</summary>
    void Reject(int pid, string reason, bool countAsFail)
    {
        string addr = PeerAddress(pid);
        Util.Log($"NET: peer {pid} refused: {reason}");
        if (countAsFail && addr != "")
        {
            if (!_fails.TryGetValue(addr, out var l)) _fails[addr] = l = new List<double>();
            double now = Now;
            l.RemoveAll(t => now - t > 60); l.Add(now);
            if (l.Count >= WrongTriesPerMinute) { _banUntil[addr] = now + 60; l.Clear(); Util.Log($"NET: address {addr} blocked for a minute (too many wrong passwords)"); }
        }
        var rb = Encoding.UTF8.GetBytes(reason);
        var pkt = new byte[1 + rb.Length]; pkt[0] = PktReject; Buffer.BlockCopy(rb, 0, pkt, 1, rb.Length);
        ((SceneMultiplayer)Multiplayer).SendAuth(pid, pkt);
        GetTree().CreateTimer(0.5).Timeout += () =>
        {
            if (_pending.Remove(pid) && IsServer && Multiplayer.MultiplayerPeer != null) Multiplayer.MultiplayerPeer.DisconnectPeer(pid);
        };
        _nonces.Remove(pid);
    }

    void FailJoin(string reason)
    {
        if (!_joining) return;
        _joining = false;
        FailReason = reason;
        Util.Log("NET: join failed: " + reason);
        Disconnect();
        ConnectionFailed?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RegisterName(string name)
    {
        if (!IsServer) return;
        int id = Multiplayer.GetRemoteSenderId();
        if (!_verified.Contains(id)) return;   // never passed the handshake
        if (_authNames.TryGetValue(id, out var authName)) name = authName;
        Names[id] = Unique(name);
        Util.Log($"NET: peer {id} registered as {Names[id]}");
        Broadcast();
        PeerRegistered?.Invoke(id);
    }

    void Broadcast()
    {
        PublishLobby();
        Rpc(nameof(SyncLobby), Names.Keys.ToArray(), Names.Values.ToArray());
        LobbyChanged?.Invoke();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void SyncLobby(int[] ids, string[] names)
    {
        bool first = Names.Count == 0;
        Names.Clear();
        for (int i = 0; i < ids.Length; i++) Names[ids[i]] = names[i];
        LobbyChanged?.Invoke();
        if (first && Names.ContainsKey(MyId)) { Util.Pass("connected", "id=" + MyId); Connected?.Invoke(); }
    }

    public string NameOf(int peer) => Names.TryGetValue(peer, out var n) ? n : "peer" + peer;
}
