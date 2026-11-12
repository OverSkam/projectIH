using Godot;

namespace AppStein;

/// <summary>
/// What an eliminated player does for the rest of the match. This is the single seam the Turned (monster players) will replace:
/// today an eliminated player is a permanent ghost (free-fly spectator that can blow out a lantern or knock a pebble).
/// </summary>
public static class EliminatedRole
{
    /// <summary>Server: the Scale just threw this miner (during the throw stage, before the animation ends).</summary>
    public static void OnEliminated(Game g, ServerPlayer p)
    {
        g.ServerNotice(p, "The Goblin throws you down the shaft. You will haunt the mine for the rest of the match.");
    }

    /// <summary>Server: called every round start for each connected eliminated player (after the world is populated).
    /// Ghost role: nothing to do on the server (ghost meddling is validated by RequestGhostMeddle: !Alive and (Participant or Eliminated)).</summary>
    public static void OnRoundStart(Game g, ServerPlayer p) { }

    /// <summary>Client (the local player is eliminated; the round world exists, or a throw animation just ended): take control of whatever the role is.</summary>
    public static void ClientEnter(Game g, Vector3 at)
    {
        Util.Pass("eliminated_ghost", $"round={g.Round}");
        g.EnterGhost(at);
    }
}
