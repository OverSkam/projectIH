using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Steamworks;

namespace AppStein;

/// <summary>
/// The real <see cref="ISteamSockets"/>: Steam Networking Sockets (P2P through Steam's relay network) with one poll group for all connections.
/// Needs a running, initialised SteamService. NEVER EXECUTED AGAINST REAL STEAM in development (no Steam client in the build container).
/// </summary>
public sealed class SteamSockets : ISteamSockets, IDisposable
{
    // Steam: reliable messages up to 512 KiB; unreliable ones are split if larger than one UDP packet, so keep those small.
    public int MaxMessageSize => 512 * 1024;
    public int MaxUnreliableSize => 1100;

    struct Ev { public int Conn; public ulong Remote; public SockState State; public bool Incoming; public string Reason; }

    readonly Queue<Ev> _events = new();
    readonly Dictionary<int, ulong> _remote = new();
    HSteamListenSocket _listen = HSteamListenSocket.Invalid;
    HSteamNetPollGroup _group = HSteamNetPollGroup.Invalid;
    Callback<SteamNetConnectionStatusChangedCallback_t> _statusCb;
    readonly IntPtr[] _msgs = new IntPtr[64];

    static int H(HSteamNetConnection c) => unchecked((int)c.m_HSteamNetConnection);
    static HSteamNetConnection C(int h) => new HSteamNetConnection(unchecked((uint)h));

    public SteamSockets()
    {
        _statusCb = Callback<SteamNetConnectionStatusChangedCallback_t>.Create(OnStatus);
        _group = SteamNetworkingSockets.CreatePollGroup();
    }

    void OnStatus(SteamNetConnectionStatusChangedCallback_t d)
    {
        int h = H(d.m_hConn);
        var info = d.m_info;
        ulong remote = info.m_identityRemote.GetSteamID64();
        var st = info.m_eState;
        if (st == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting
            && d.m_eOldState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None
            && info.m_hListenSocket != HSteamListenSocket.Invalid && info.m_hListenSocket == _listen)
        {
            _remote[h] = remote;
            _events.Enqueue(new Ev { Conn = h, Remote = remote, Incoming = true, State = SockState.Connecting });
        }
        else if (st == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connected)
        {
            if (remote != 0) _remote[h] = remote;
            _events.Enqueue(new Ev { Conn = h, State = SockState.Connected });
        }
        else if (st == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ClosedByPeer
              || st == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally)
        {
            _events.Enqueue(new Ev { Conn = h, State = SockState.Closed, Reason = (info.m_szEndDebug ?? "") + (st == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_ProblemDetectedLocally ? " (lost)" : "") });
        }
    }

    public bool Listen()
    {
        _listen = SteamNetworkingSockets.CreateListenSocketP2P(SteamConfig.VirtualPort, 0, null);
        return _listen != HSteamListenSocket.Invalid;
    }

    public void StopListen()
    {
        if (_listen != HSteamListenSocket.Invalid) SteamNetworkingSockets.CloseListenSocket(_listen);
        _listen = HSteamListenSocket.Invalid;
    }

    public int Connect(ulong remoteId)
    {
        var id = new SteamNetworkingIdentity();
        id.SetSteamID64(remoteId);
        var c = SteamNetworkingSockets.ConnectP2P(ref id, SteamConfig.VirtualPort, 0, null);
        if (c == HSteamNetConnection.Invalid) return 0;
        _remote[H(c)] = remoteId;
        SteamNetworkingSockets.SetConnectionPollGroup(c, _group);
        return H(c);
    }

    public bool Accept(int conn)
    {
        var c = C(conn);
        if (SteamNetworkingSockets.AcceptConnection(c) != EResult.k_EResultOK) return false;
        SteamNetworkingSockets.SetConnectionPollGroup(c, _group);
        return true;
    }

    public void Close(int conn, string reason, bool linger)
    {
        SteamNetworkingSockets.CloseConnection(C(conn), 1000, reason ?? "", linger);
        _remote.Remove(conn);
    }

    public bool Send(int conn, byte[] data, int length, SockSend how)
    {
        int flags = how == SockSend.Reliable ? 8 /* k_nSteamNetworkingSend_Reliable */ : 1 /* UnreliableNoNagle */;
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            var r = SteamNetworkingSockets.SendMessageToConnection(C(conn), pin.AddrOfPinnedObject(), (uint)length, flags, out _);
            return r == EResult.k_EResultOK;
        }
        finally { pin.Free(); }
    }

    public ulong RemoteId(int conn) => _remote.TryGetValue(conn, out var r) ? r : 0;

    public void Poll(ISocketEvents sink)
    {
        SteamService.RunCallbacksNow();   // status changes arrive here, so a Connected event is always handled before that connection's messages
        while (_events.Count > 0)
        {
            var e = _events.Dequeue();
            if (e.Incoming) sink.OnIncoming(e.Conn, e.Remote); else sink.OnState(e.Conn, e.State, e.Reason);
        }
        if (_group == HSteamNetPollGroup.Invalid) return;
        for (int round = 0; round < 16; round++)
        {
            int n = SteamNetworkingSockets.ReceiveMessagesOnPollGroup(_group, _msgs, _msgs.Length);
            for (int i = 0; i < n; i++)
            {
                var m = SteamNetworkingMessage_t.FromIntPtr(_msgs[i]);
                var buf = new byte[m.m_cbSize];
                if (m.m_cbSize > 0) Marshal.Copy(m.m_pData, buf, 0, m.m_cbSize);
                int conn = H(m.m_conn);
                SteamNetworkingMessage_t.Release(_msgs[i]);
                sink.OnMessage(conn, buf);
            }
            if (n < _msgs.Length) break;
        }
    }

    /// <summary>Free the Steam-side handles. Call after the peer closed.</summary>
    public void Dispose()
    {
        try { _statusCb?.Dispose(); } catch { }
        _statusCb = null;
        StopListen();
        if (_group != HSteamNetPollGroup.Invalid) { try { SteamNetworkingSockets.DestroyPollGroup(_group); } catch { } _group = HSteamNetPollGroup.Invalid; }
    }
}
