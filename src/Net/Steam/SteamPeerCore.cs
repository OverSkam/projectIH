using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>
/// The logic of the Steam multiplayer transport without any Steam call: peer ids, the small hello packet, packet framing (channel + transfer mode),
/// targeting (0 = everybody, positive = one peer, negative = everybody except), the receive queue, connect / disconnect events, refusing connections.
/// It talks to the outside only through <see cref="ISteamSockets"/>, so tests run it against an in-memory fake. SteamPeer (a MultiplayerPeerExtension)
/// is a thin wrapper around this class.
/// Wire format of one message: byte0 = kind (high nibble: 1 data, 2 hello) | transfer mode (low nibble: 0 unreliable, 1 unreliable ordered, 2 reliable).
/// Data: [byte0][channel][payload]; unreliable ordered adds a 2-byte sequence number before the payload (older ones are dropped).
/// Hello (server to client, first message on a new connection): [0x20][protocol version][peer id, 4 bytes little endian].
/// </summary>
public sealed class SteamPeerCore : ISocketEvents
{
    public const byte Protocol = 1;
    const int KData = 1, KHello = 2;
    public const int Header = 2, OrderedExtra = 2;

    public struct Packet { public int Peer, Channel; public MultiplayerPeer.TransferModeEnum Mode; public byte[] Data; }

    readonly ISteamSockets _s;
    readonly Queue<Packet> _in = new();
    Packet _last;

    // server
    readonly Dictionary<int, int> _connToPeer = new(), _peerToConn = new();
    readonly HashSet<int> _accepted = new();          // accepted connections, whether or not their hello went out yet
    // client
    int _clientConn;
    readonly Dictionary<long, ushort> _sendSeq = new(), _recvSeq = new();
    readonly HashSet<long> _recvSeen = new();

    public bool IsServer { get; private set; }
    public int UniqueId { get; private set; }
    public MultiplayerPeer.ConnectionStatus Status { get; private set; } = MultiplayerPeer.ConnectionStatus.Disconnected;
    public bool RefuseNew;
    public int MaxConnections = 16;
    public int TargetPeer, TransferChannel;
    public MultiplayerPeer.TransferModeEnum TransferMode = MultiplayerPeer.TransferModeEnum.Reliable;
    /// <summary>Where new client peer ids come from (tests replace it to force collisions).</summary>
    public Func<int> IdSource = () => System.Security.Cryptography.RandomNumberGenerator.GetInt32(2, int.MaxValue);

    public event Action<int> PeerConnected, PeerDisconnected;
    /// <summary>Client only: why the connection ended or never came up (sentence for logs / the player).</summary>
    public string EndReason = "";

    public SteamPeerCore(ISteamSockets sockets) { _s = sockets; }

    public int MaxPacketSize => _s.MaxMessageSize - Header - OrderedExtra;
    public int Available => _in.Count;
    public int PeerCount => _peerToConn.Count;
    public IEnumerable<int> Peers => _peerToConn.Keys;

    // ------------------------------------------------------------ start / stop
    public bool StartServer()
    {
        if (Status != MultiplayerPeer.ConnectionStatus.Disconnected) return false;
        if (!_s.Listen()) { EndReason = "Steam would not open the room."; return false; }
        IsServer = true; UniqueId = 1; Status = MultiplayerPeer.ConnectionStatus.Connected;
        return true;
    }

    public bool StartClient(ulong hostSteamId)
    {
        if (Status != MultiplayerPeer.ConnectionStatus.Disconnected) return false;
        _clientConn = _s.Connect(hostSteamId);
        if (_clientConn == 0) { EndReason = "Could not start the connection."; return false; }
        IsServer = false; UniqueId = 0; Status = MultiplayerPeer.ConnectionStatus.Connecting;
        return true;
    }

    public void Close()
    {
        if (Status == MultiplayerPeer.ConnectionStatus.Disconnected) return;
        foreach (var c in new List<int>(_accepted)) _s.Close(c, "host closed the room", true);
        if (_clientConn != 0) _s.Close(_clientConn, "left", true);
        if (IsServer) _s.StopListen();
        Reset();
    }

    void Reset(bool keepQueue = false)
    {
        _accepted.Clear(); _connToPeer.Clear(); _peerToConn.Clear(); _clientConn = 0;
        if (!keepQueue) _in.Clear();   // when the other side closed, packets it sent just before (e.g. a refusal reason) stay readable
        _sendSeq.Clear(); _recvSeq.Clear(); _recvSeen.Clear();
        Status = MultiplayerPeer.ConnectionStatus.Disconnected; IsServer = false; UniqueId = 0;
    }

    public void DisconnectPeer(int peer, bool force)
    {
        if (Status == MultiplayerPeer.ConnectionStatus.Disconnected) return;
        if (!IsServer) { if (peer == 1) Close(); return; }
        if (!_peerToConn.TryGetValue(peer, out var conn)) return;
        DropServerConn(conn, peer, "kicked", !force);
    }

    void DropServerConn(int conn, int peer, string reason, bool linger)
    {
        _s.Close(conn, reason, linger);
        _accepted.Remove(conn); _connToPeer.Remove(conn);
        if (peer != 0) _peerToConn.Remove(peer);
        ForgetSeq(conn);
        if (peer != 0) PeerDisconnected?.Invoke(peer);
    }

    void ForgetSeq(int conn)
    {
        foreach (var k in new List<long>(_sendSeq.Keys)) if ((k >> 8) == conn) _sendSeq.Remove(k);
        foreach (var k in new List<long>(_recvSeq.Keys)) if ((k >> 8) == conn) _recvSeq.Remove(k);
        _recvSeen.RemoveWhere(k => (k >> 8) == conn);
    }

    public void Poll()
    {
        if (Status == MultiplayerPeer.ConnectionStatus.Disconnected) return;
        _s.Poll(this);
    }

    public ulong RemoteSteamId(int peer)
    {
        if (IsServer) return _peerToConn.TryGetValue(peer, out var c) ? _s.RemoteId(c) : 0;
        return peer == 1 && _clientConn != 0 ? _s.RemoteId(_clientConn) : 0;
    }

    // ------------------------------------------------------------ events from the sockets
    public void OnIncoming(int conn, ulong remoteId)
    {
        if (!IsServer || Status != MultiplayerPeer.ConnectionStatus.Connected || RefuseNew || _accepted.Count >= MaxConnections)
        {
            _s.Close(conn, RefuseNew ? "not accepting connections" : "room full", false);
            return;
        }
        if (!_s.Accept(conn)) { _s.Close(conn, "accept failed", false); return; }
        _accepted.Add(conn);
    }

    public void OnState(int conn, SockState state, string reason)
    {
        if (IsServer)
        {
            if (!_accepted.Contains(conn)) return;
            if (state == SockState.Connected && !_connToPeer.ContainsKey(conn))
            {
                int id = NewPeerId();
                var hello = new byte[] { (byte)(KHello << 4), Protocol, (byte)id, (byte)(id >> 8), (byte)(id >> 16), (byte)(id >> 24) };
                _connToPeer[conn] = id; _peerToConn[id] = conn;
                _s.Send(conn, hello, hello.Length, SockSend.Reliable);
                PeerConnected?.Invoke(id);
            }
            else if (state == SockState.Closed)
            {
                _connToPeer.TryGetValue(conn, out var peer);
                DropServerConn(conn, peer, reason, false);
            }
            return;
        }
        if (conn != _clientConn || _clientConn == 0) return;
        if (state == SockState.Closed)
        {
            bool wasConnected = Status == MultiplayerPeer.ConnectionStatus.Connected;
            EndReason = string.IsNullOrEmpty(reason) ? (wasConnected ? "The host left." : "Could not reach the host.") : reason;
            _s.Close(conn, "closed", false);
            Reset(keepQueue: true);
            if (wasConnected) PeerDisconnected?.Invoke(1);
        }
    }

    int NewPeerId()
    {
        for (int i = 0; i < 1000; i++)
        {
            int id = IdSource();
            if (id >= 2 && !_peerToConn.ContainsKey(id)) return id;
        }
        throw new InvalidOperationException("no free peer id");
    }

    public void OnMessage(int conn, byte[] d)
    {
        if (d == null || d.Length < 1) return;
        int kind = d[0] >> 4, mode = d[0] & 0xF;
        if (IsServer)
        {
            if (!_connToPeer.TryGetValue(conn, out var peer) || kind != KData) return;
            Accept(conn, peer, d, mode);
            return;
        }
        if (conn != _clientConn) return;
        if (kind == KHello)
        {
            if (Status != MultiplayerPeer.ConnectionStatus.Connecting) return;
            if (d.Length < 6 || d[1] != Protocol)
            {
                EndReason = "The host runs a different version of the game.";
                _s.Close(conn, "protocol", false); Reset(); return;
            }
            UniqueId = d[2] | (d[3] << 8) | (d[4] << 16) | (d[5] << 24);
            if (UniqueId < 2) { EndReason = "The host sent a bad hello."; _s.Close(conn, "bad hello", false); Reset(); return; }
            Status = MultiplayerPeer.ConnectionStatus.Connected;
            PeerConnected?.Invoke(1);
        }
        else if (kind == KData && Status == MultiplayerPeer.ConnectionStatus.Connected) Accept(conn, 1, d, mode);
    }

    void Accept(int conn, int peer, byte[] d, int mode)
    {
        if (mode > 2 || d.Length < Header) return;
        int channel = d[1];
        int at = Header;
        if (mode == 1)
        {
            if (d.Length < Header + OrderedExtra) return;
            ushort seq = (ushort)(d[2] | (d[3] << 8));
            long key = ((long)conn << 8) | (uint)channel;
            if (_recvSeen.Contains(key) && (short)(seq - _recvSeq[key]) <= 0) return;   // older than what we already delivered
            _recvSeen.Add(key); _recvSeq[key] = seq;
            at += OrderedExtra;
        }
        var data = new byte[d.Length - at];
        Buffer.BlockCopy(d, at, data, 0, data.Length);
        _in.Enqueue(new Packet { Peer = peer, Channel = channel, Mode = (MultiplayerPeer.TransferModeEnum)mode, Data = data });
    }

    // ------------------------------------------------------------ packets
    public byte[] Get()
    {
        if (_in.Count == 0) return Array.Empty<byte>();
        _last = _in.Dequeue();
        return _last.Data;
    }

    /// <summary>Sender of the next waiting packet (or of the one just read when none is waiting).</summary>
    public int PacketPeer => _in.Count > 0 ? _in.Peek().Peer : _last.Peer;
    public int PacketChannel => _in.Count > 0 ? _in.Peek().Channel : _last.Channel;
    public MultiplayerPeer.TransferModeEnum PacketMode => _in.Count > 0 ? _in.Peek().Mode : _last.Mode;

    public Error Put(byte[] payload)
    {
        if (Status != MultiplayerPeer.ConnectionStatus.Connected) return Error.Unconfigured;
        if (payload == null) return Error.InvalidParameter;
        if (payload.Length > MaxPacketSize) return Error.OutOfMemory;
        int mode = (int)TransferMode; if (mode < 0 || mode > 2) mode = 2;
        int channel = Math.Clamp(TransferChannel, 0, 255);
        // Steam splits unreliable messages that do not fit one UDP packet and loses the whole message if one piece is lost: send those reliably.
        bool asReliable = mode == 2 || payload.Length + Header + OrderedExtra > _s.MaxUnreliableSize;
        var how = asReliable ? SockSend.Reliable : SockSend.Unreliable;
        Error result = Error.Ok;

        if (!IsServer)
        {
            if (TargetPeer != 0 && TargetPeer != 1) return Error.InvalidParameter;
            return SendTo(_clientConn, mode, channel, payload, how) ? Error.Ok : Error.Failed;
        }
        if (TargetPeer > 0)
        {
            if (!_peerToConn.TryGetValue(TargetPeer, out var c)) return Error.InvalidParameter;
            return SendTo(c, mode, channel, payload, how) ? Error.Ok : Error.Failed;
        }
        int except = TargetPeer < 0 ? -TargetPeer : 0;
        foreach (var kv in _peerToConn)
        {
            if (kv.Key == except) continue;
            if (!SendTo(kv.Value, mode, channel, payload, how)) result = Error.Failed;
        }
        return result;
    }

    bool SendTo(int conn, int mode, int channel, byte[] payload, SockSend how)
    {
        int extra = mode == 1 ? OrderedExtra : 0;
        var f = new byte[Header + extra + payload.Length];
        f[0] = (byte)((KData << 4) | mode); f[1] = (byte)channel;
        if (mode == 1)
        {
            long key = ((long)conn << 8) | (uint)channel;
            _sendSeq.TryGetValue(key, out var seq); seq++; _sendSeq[key] = seq;
            f[2] = (byte)seq; f[3] = (byte)(seq >> 8);
        }
        Buffer.BlockCopy(payload, 0, f, Header + extra, payload.Length);
        return _s.Send(conn, f, f.Length, how);
    }
}
