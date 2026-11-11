using Godot;

namespace AppStein;

/// <summary>Monster RPCs (partial class Game). Position is unreliable ~10 Hz; state/health travel in NetObject.Data.</summary>
public partial class Game
{
    public void ServerMonsterSync(int id, Vector3 pos, float yaw) => Rpc(nameof(ClientMonsterSync), id, pos, yaw);

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    void ClientMonsterSync(int id, Vector3 pos, float yaw)
    {
        if (World != null && World.Objects.TryGetValue(id, out var o) && o is MonsterObj m) m.SetNetState(pos, yaw);
    }

    public void ServerAnnounce(string msg) => Rpc(nameof(ClientAnnounce), msg);

    public void ServerKnockback(int peer, Vector3 v) => RpcId(peer, nameof(ClientKnockback), v);
}
