using Godot;

namespace AppStein;

/// <summary>Trap-related RPCs (server -> clients). Trap state itself travels through NetObject Data.</summary>
public partial class Game
{
    /// <summary>Server: play a one-shot positional sound on every peer.</summary>
    public static void ServerTrapSfx(Vector3 pos, string name, float db = 0f) => I?.Rpc(nameof(ClientTrapSfx), pos, name, db);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    void ClientTrapSfx(Vector3 pos, string name, float db) => TrapSfx.Play(pos, name, db);
}
