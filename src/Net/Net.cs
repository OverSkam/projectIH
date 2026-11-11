using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>Autoload: ENet connection + lobby roster. Game (autoload) owns everything about matches.</summary>
public partial class Net : Node
{
    public static Net I;
    public const int DefaultPort = 7777, MaxPeers = 8;

    public string LocalName = "Miner";
    /// <summary>peer id -> display name (all connected, registered peers). Identical on every peer.</summary>
    public readonly SortedDictionary<int, string> Names = new();

    public event Action LobbyChanged;
    public event Action Connected;           // client: connected + registered
    public event Action ConnectionFailed;
    public event Action ServerLost;          // client: host went away / we left
    public event Action<int> PeerLeft;       // server only
    public event Action<int> PeerRegistered; // server only

    public bool Online => Multiplayer.MultiplayerPeer != null && Multiplayer.MultiplayerPeer is not OfflineMultiplayerPeer;
    public bool IsServer => Online && Multiplayer.IsServer();
    public int MyId => Online ? Multiplayer.GetUniqueId() : 1;

    public override void _Ready()
    {
        I = this;
        Multiplayer.PeerConnected += id => { };
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += () => { Disconnect(); ConnectionFailed?.Invoke(); };
        Multiplayer.ServerDisconnected += OnServerDisconnected;
    }

    public Error Host(int port = DefaultPort)
    {
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(port, MaxPeers);
        if (err != Error.Ok) { Util.Log("ERROR: host failed " + err); return err; }
        Multiplayer.MultiplayerPeer = peer;
        Names.Clear();
        Names[1] = Unique(LocalName);
        Util.Log($"NET: hosting on port {port}");
        LobbyChanged?.Invoke();
        return Error.Ok;
    }

    public Error Join(string ip, int port = DefaultPort)
    {
        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateClient(ip, port);
        if (err != Error.Ok) { Util.Log("ERROR: join failed " + err); return err; }
        Multiplayer.MultiplayerPeer = peer;
        Names.Clear();
        Util.Log($"NET: joining {ip}:{port}");
        return Error.Ok;
    }

    /// <summary>Close the connection (host: everyone else gets ServerDisconnected).</summary>
    public void Disconnect()
    {
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
        Names.Remove((int)id);
        PeerLeft?.Invoke((int)id);
        Broadcast();
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void RegisterName(string name)
    {
        if (!IsServer) return;
        int id = Multiplayer.GetRemoteSenderId();
        Names[id] = Unique(name);
        Util.Log($"NET: peer {id} registered as {Names[id]}");
        Broadcast();
        PeerRegistered?.Invoke(id);
    }

    void Broadcast()
    {
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
