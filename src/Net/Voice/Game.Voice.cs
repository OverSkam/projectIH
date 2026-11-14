using System;
using System.Collections.Generic;
using Godot;

namespace AppStein;

/// <summary>Voice chat on the server side: RPC entry points on <c>Game</c> (like every other RPC), the abuse limits and the routing (who hears whom).
/// The client side (capture, playback, mute, glyphs) lives in <see cref="VoiceHub"/>. Voice runs on its own unreliable channel and never touches gameplay state,
/// except that loud talking becomes <c>Game.ServerNoise</c> for the monsters.</summary>
public partial class Game
{
    sealed class VoiceState
    {
        public readonly VoiceBucket Frames = new(VoiceTuning.MaxFramesPerSec, VoiceTuning.FrameBurst);
        public readonly VoiceBucket Bytes = new(VoiceTuning.MaxBytesPerSec, VoiceTuning.ByteBurst);
        public double NextNoise;
    }
    readonly Dictionary<int, VoiceState> _voiceState = new();

    /// <summary>Server counters (tests, logs).</summary>
    public int VoiceAccepted, VoiceRelayed, VoiceRejUnverified, VoiceRejSize, VoiceRejRate, VoiceNoises;
    /// <summary>Raised on the server for every voice noise handed to the monsters (tests). speaker name, radius.</summary>
    public event Action<string, Vector3, float> VoiceNoiseMade;

    /// <summary>Client: send one compressed voice frame to the server. packet = [seq lo][seq hi][level 0..255][compressed audio].</summary>
    public void SendVoice(byte[] packet) => RpcId(1, nameof(VoiceUp), packet);

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = VoiceTuning.Channel)]
    void VoiceUp(byte[] packet)
    {
        if (!Net.I.IsServer) return;
        ServerOnVoice(Multiplayer.GetRemoteSenderId(), packet, Now);
    }

    /// <summary>Client: a voice frame relayed by the server. packet = [seq lo][seq hi][mode][level][compressed audio].</summary>
    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable, TransferChannel = VoiceTuning.Channel)]
    void VoiceDown(int speaker, byte[] packet)
    {
        if (Net.I.IsServer) return;
        VoiceHub.I?.OnVoiceFrame(speaker, packet);
    }

    public void VoiceForget(int peer) => _voiceState.Remove(peer);

    VoiceRole VoiceRoleOf(int peer, bool lobbyPhase)
    {
        if (lobbyPhase) return VoiceRole.Lobby;
        if (!Players.TryGetValue(peer, out var p) || !p.Alive) return VoiceRole.Ghost;
        return p.Downed ? VoiceRole.Downed : VoiceRole.Living;
    }

    Vector3 VoicePosOf(int peer)
    {
        if (VoiceTestPins.TryGet(Net.I.NameOf(peer), out var pin)) return pin;
        return Players.TryGetValue(peer, out var p) ? p.Pos : Vector3.Zero;
    }

    /// <summary>Server: validates one voice frame from <paramref name="sender"/> and relays it to whoever may hear it. Returns true when the frame was accepted.
    /// Guards, in this order: the sender passed the room handshake (pending peers cannot speak; the engine also drops their RPCs), the frame size, the
    /// per-client frame and byte rate. A speaker never gets their own audio back. Public so the headless tests can call it with a made-up sender.</summary>
    public bool ServerOnVoice(int sender, byte[] pkt, double now)
    {
        if (!Net.I.IsServer) return false;
        if (sender == 0) sender = MyId;
        if (!Net.I.IsVerified(sender) || !Net.I.Names.ContainsKey(sender)) { VoiceRejUnverified++; return false; }
        if (pkt == null || pkt.Length <= VoiceTuning.HeaderBytes || pkt.Length - VoiceTuning.HeaderBytes > VoiceTuning.MaxFrameBytes) { VoiceRejSize++; return false; }
        if (!_voiceState.TryGetValue(sender, out var st)) _voiceState[sender] = st = new VoiceState();
        if (!st.Frames.Take(1, now) || !st.Bytes.Take(pkt.Length, now)) { VoiceRejRate++; return false; }
        VoiceAccepted++;

        float level = Math.Clamp(pkt[2] / 255f, 0f, 1f);
        bool lobby = Phase == GamePhase.Lobby || Phase == GamePhase.MatchEnd;
        bool ceremony = Phase == GamePhase.Weighing;
        var sRole = VoiceRoleOf(sender, lobby);
        var sPos = VoicePosOf(sender);
        byte[] down = null;
        foreach (var other in Net.I.Names.Keys)
        {
            if (other == sender || !Net.I.IsVerified(other)) continue;   // never back to the speaker, never to a pending peer
            if (!VoiceRules.Decide(lobby, ceremony, sRole, sPos, VoiceRoleOf(other, lobby), VoicePosOf(other), out var mode)) continue;
            down = new byte[pkt.Length + 1];
            down[0] = pkt[0]; down[1] = pkt[1]; down[2] = (byte)mode; down[3] = pkt[2];
            Buffer.BlockCopy(pkt, VoiceTuning.HeaderBytes, down, VoiceTuning.DownHeaderBytes, pkt.Length - VoiceTuning.HeaderBytes);
            VoiceRelayed++;
            if (other == 1) VoiceHub.I?.OnVoiceFrame(sender, down);
            else RpcId(other, nameof(VoiceDown), sender, down);
        }

        // monsters hear loud talking (not whispers, not the dead, not while downed, only during the round), at most once per interval per speaker
        if (Phase == GamePhase.Round && sRole == VoiceRole.Living && now >= st.NextNoise && Players.TryGetValue(sender, out var sp))
        {
            float r = VoiceRules.NoiseRadius(level);
            if (r > 0f)
            {
                st.NextNoise = now + VoiceTuning.NoiseIntervalSecs;
                VoiceNoises++;
                var at = VoiceTestPins.TryGet(sp.Name, out var pin) ? pin : sp.Pos;
                VoiceNoiseMade?.Invoke(sp.Name, at, r);
                ServerNoise(at, r, sp);
            }
        }
        return true;
    }
}

/// <summary>Test hook (`--voicepin=Alice:0:0:0,Bob:4:0:0`): pins where the voice rules think a named player stands, on the server and on every client, so tests can
/// put miners at known distances without moving bots through the cave. Empty (and unused) in real play.</summary>
public static class VoiceTestPins
{
    static Dictionary<string, Vector3> _pins;
    public static bool TryGet(string name, out Vector3 pos)
    {
        if (_pins == null)
        {
            _pins = new();
            foreach (var tok in Util.Arg("voicepin", "").Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                var p = tok.Split(':');
                if (p.Length == 4 && float.TryParse(p[1], out var x) && float.TryParse(p[2], out var y) && float.TryParse(p[3], out var z)) _pins[p[0]] = new Vector3(x, y, z);
            }
        }
        return _pins.TryGetValue(name ?? "", out pos);
    }
}
