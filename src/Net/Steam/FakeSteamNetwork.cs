using System;
using System.Collections.Generic;

namespace AppStein;

/// <summary>In-memory stand-in for Steam Networking Sockets: several "machines" (Steam IDs) in one process. Used by --steamcheck. Delivers events and messages in order, like Steam does on one connection.</summary>
public sealed class FakeSteamNetwork
{
    public readonly Dictionary<ulong, FakeSockets> Nodes = new();
    internal int NextHandle = 1000;
    public FakeSockets Node(ulong steamId)
    {
        if (!Nodes.TryGetValue(steamId, out var n)) Nodes[steamId] = n = new FakeSockets(this, steamId);
        return n;
    }
}

public sealed class FakeSockets : ISteamSockets
{
    sealed class End { public int Handle; public ulong Remote; public End Other; public FakeSockets Owner; public bool Open; }

    readonly FakeSteamNetwork _net;
    public readonly ulong Id;
    bool _listening;
    readonly Dictionary<int, End> _conns = new();
    readonly Queue<Action<ISocketEvents>> _q = new();

    public int MaxMessageSize { get; set; } = 512 * 1024;
    public int MaxUnreliableSize { get; set; } = 1100;
    /// <summary>How the last Send went out (lets a test see that an oversized unreliable packet was upgraded to reliable).</summary>
    public SockSend LastHow;
    public int Sent;
    public bool Listening => _listening;
    public int OpenConnections => _conns.Count;

    public FakeSockets(FakeSteamNetwork net, ulong id) { _net = net; Id = id; }

    public bool Listen() { _listening = true; return true; }
    public void StopListen() { _listening = false; }

    public int Connect(ulong remoteId)
    {
        var me = new End { Handle = _net.NextHandle++, Remote = remoteId, Owner = this };
        _conns[me.Handle] = me;
        if (!_net.Nodes.TryGetValue(remoteId, out var target) || !target._listening)
        {
            int h = me.Handle;
            _q.Enqueue(s => { if (_conns.ContainsKey(h)) s.OnState(h, SockState.Closed, "no route to host"); });
            return me.Handle;
        }
        var them = new End { Handle = _net.NextHandle++, Remote = Id, Owner = target, Other = me };
        me.Other = them;
        target._conns[them.Handle] = them;
        int th = them.Handle; ulong from = Id;
        target._q.Enqueue(s => s.OnIncoming(th, from));
        return me.Handle;
    }

    public bool Accept(int conn)
    {
        if (!_conns.TryGetValue(conn, out var e) || e.Other == null) return false;
        e.Open = true; e.Other.Open = true;
        int mine = e.Handle, theirs = e.Other.Handle; var other = e.Other.Owner;
        _q.Enqueue(s => { if (_conns.ContainsKey(mine)) s.OnState(mine, SockState.Connected, null); });
        other._q.Enqueue(s => { if (other._conns.ContainsKey(theirs)) s.OnState(theirs, SockState.Connected, null); });
        return true;
    }

    public void Close(int conn, string reason, bool linger)
    {
        if (!_conns.TryGetValue(conn, out var e)) return;
        _conns.Remove(conn);
        var o = e.Other;
        if (o != null && o.Owner._conns.ContainsKey(o.Handle))
        {
            var owner = o.Owner; int oh = o.Handle;
            owner._q.Enqueue(s => { if (owner._conns.ContainsKey(oh)) s.OnState(oh, SockState.Closed, reason); });
        }
    }

    public bool Send(int conn, byte[] data, int length, SockSend how)
    {
        if (!_conns.TryGetValue(conn, out var e) || !e.Open || e.Other == null || length > MaxMessageSize) return false;
        var copy = new byte[length]; Buffer.BlockCopy(data, 0, copy, 0, length);
        var owner = e.Other.Owner; int oh = e.Other.Handle;
        owner._q.Enqueue(s => { if (owner._conns.ContainsKey(oh)) s.OnMessage(oh, copy); });
        LastHow = how; Sent++;
        return true;
    }

    public ulong RemoteId(int conn) => _conns.TryGetValue(conn, out var e) ? e.Remote : 0;

    public void Poll(ISocketEvents sink)
    {
        int n = _q.Count;
        for (int i = 0; i < n && _q.Count > 0; i++) _q.Dequeue()(sink);
    }
}
