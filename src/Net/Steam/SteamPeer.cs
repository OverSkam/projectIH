using Godot;

namespace AppStein;

/// <summary>
/// Godot multiplayer peer that carries SceneMultiplayer (all RPCs) over Steam Networking Sockets, so friends connect through Steam's relay network
/// with no router setup. All logic lives in <see cref="SteamPeerCore"/>; this class only maps it onto the MultiplayerPeerExtension virtuals.
/// Host = peer id 1, joiners get a random id from the host. Use CreateHost() / CreateClient(steamId), then assign it to Multiplayer.MultiplayerPeer.
/// </summary>
public partial class SteamPeer : MultiplayerPeerExtension
{
    SteamPeerCore _core;
    ISteamSockets _sockets;

    public SteamPeerCore Core => _core;
    public string EndReason => _core?.EndReason ?? "";

    /// <summary>Use another socket layer (tests: the in-memory fake). Must be called before CreateHost / CreateClient.</summary>
    public SteamPeer Use(ISteamSockets sockets)
    {
        _sockets = sockets; Setup();
        return this;
    }

    void Setup()
    {
        _core = new SteamPeerCore(_sockets);
        _core.PeerConnected += id => EmitSignal(MultiplayerPeer.SignalName.PeerConnected, (long)id);
        _core.PeerDisconnected += id => EmitSignal(MultiplayerPeer.SignalName.PeerDisconnected, (long)id);
    }

    public Error CreateHost()
    {
        if (_core == null) Use(new SteamSockets());
        return _core.StartServer() ? Error.Ok : Error.CantCreate;
    }

    public Error CreateClient(ulong hostSteamId)
    {
        if (_core == null) Use(new SteamSockets());
        return _core.StartClient(hostSteamId) ? Error.Ok : Error.CantConnect;
    }

    /// <summary>Steam ID of a connected peer (host side: the joiner; client side: the host). 0 when unknown.</summary>
    public ulong GetRemoteSteamId(int peer) => _core?.RemoteSteamId(peer) ?? 0;

    public override void _Poll() => _core?.Poll();
    public override void _Close()
    {
        _core?.Close();
        (_sockets as System.IDisposable)?.Dispose();
    }
    public override void _DisconnectPeer(int peer, bool force) => _core?.DisconnectPeer(peer, force);
    public override ConnectionStatus _GetConnectionStatus() => _core?.Status ?? ConnectionStatus.Disconnected;
    public override int _GetUniqueId() => _core?.UniqueId ?? 0;
    public override bool _IsServer() => _core?.IsServer ?? false;
    public override bool _IsServerRelaySupported() => true;
    public override int _GetMaxPacketSize() => _core?.MaxPacketSize ?? 1024;
    public override bool _IsRefusingNewConnections() => _core?.RefuseNew ?? false;
    public override void _SetRefuseNewConnections(bool enable) { if (_core != null) _core.RefuseNew = enable; }
    public override void _SetTargetPeer(int peer) { if (_core != null) _core.TargetPeer = peer; }
    public override void _SetTransferChannel(int channel) { if (_core != null) _core.TransferChannel = channel; }
    public override int _GetTransferChannel() => _core?.TransferChannel ?? 0;
    public override void _SetTransferMode(TransferModeEnum mode) { if (_core != null) _core.TransferMode = mode; }
    public override TransferModeEnum _GetTransferMode() => _core?.TransferMode ?? TransferModeEnum.Reliable;
    public override int _GetAvailablePacketCount() => _core?.Available ?? 0;
    public override byte[] _GetPacketScript() => _core?.Get() ?? System.Array.Empty<byte>();
    public override int _GetPacketPeer() => _core?.PacketPeer ?? 0;
    public override int _GetPacketChannel() => _core?.PacketChannel ?? 0;
    public override TransferModeEnum _GetPacketMode() => _core?.PacketMode ?? TransferModeEnum.Reliable;
    public override Error _PutPacketScript(byte[] buffer) => _core?.Put(buffer) ?? Error.Unconfigured;
}
