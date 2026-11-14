namespace AppStein;

public enum SockSend { Reliable, Unreliable }
/// <summary>Connection state as the transport core needs it. Closed = closed by the other side, or lost / failed locally.</summary>
public enum SockState { Connecting, Connected, Closed }

/// <summary>Receives what <see cref="ISteamSockets.Poll"/> found. Called on the polling thread, in the order things happened.</summary>
public interface ISocketEvents
{
    /// <summary>Host side: somebody wants to connect to our listen socket. Answer with Accept or Close.</summary>
    void OnIncoming(int conn, ulong remoteId);
    void OnState(int conn, SockState state, string reason);
    void OnMessage(int conn, byte[] data);
}

/// <summary>
/// The small slice of Steam Networking Sockets the transport core needs. The real implementation (SteamSockets) talks to Steam;
/// the tests use an in-memory fake so everything above this interface runs without Steam. Connection handles are plain ints (never 0).
/// </summary>
public interface ISteamSockets
{
    /// <summary>Start accepting connections (host). False when Steam refuses.</summary>
    bool Listen();
    void StopListen();
    /// <summary>Open a connection to a host (client). Returns a handle, or 0 when it cannot even be started.</summary>
    int Connect(ulong remoteId);
    bool Accept(int conn);
    /// <summary>Close a connection. linger = let already queued reliable data go out first.</summary>
    void Close(int conn, string reason, bool linger);
    bool Send(int conn, byte[] data, int length, SockSend how);
    ulong RemoteId(int conn);
    /// <summary>Deliver everything that happened since the last call to the sink.</summary>
    void Poll(ISocketEvents sink);
    /// <summary>Largest message Send accepts.</summary>
    int MaxMessageSize { get; }
    /// <summary>Largest message that is sent unreliable as it is; bigger "unreliable" packets are sent reliable instead.</summary>
    int MaxUnreliableSize { get; }
}
