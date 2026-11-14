using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>`-- --steamcheck`: headless self-check of the Steam transport (in-memory fake, no Steam needed), the Steam room codes and the SteamService failure path. Exit code 1 on failure.</summary>
[ContentModule]
public static class SteamTools
{
    public static void Register()
    {
        if (!Util.HasArg("steamcheck")) return;
        int fails = SteamCheck.Run();
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree != null) tree.ProcessFrame += () => tree.Quit(fails == 0 ? 0 : 1);
    }
}

public static class SteamCheck
{
    static int _fails, _checks;
    static void Ok(bool cond, string what) { _checks++; if (!cond) { _fails++; Util.Fail("steamcheck", what); } }

    public static int Run()
    {
        _fails = 0; _checks = 0;
        Transport();
        Codes();
        Service();
        OverSceneMultiplayer();
        Util.Log($"STEAMCHECK: {_checks} checks, {_fails} failed");
        if (_fails == 0) Util.Pass("steamcheck", $"{_checks} checks");
        return _fails;
    }

    // ------------------------------------------------------------ (a) transport core over the in-memory fake
    sealed class Node
    {
        public FakeSockets Sock; public SteamPeerCore Core;
        public List<int> Connected = new(), Disconnected = new();
        public Node(FakeSteamNetwork net, ulong id)
        {
            Sock = net.Node(id); Core = new SteamPeerCore(Sock);
            Core.PeerConnected += p => Connected.Add(p);
            Core.PeerDisconnected += p => Disconnected.Add(p);
        }
        public List<SteamPeerCore.Packet> Drain()
        {
            var l = new List<SteamPeerCore.Packet>();
            while (Core.Available > 0)
            {
                int peer = Core.PacketPeer, ch = Core.PacketChannel; var mode = Core.PacketMode;
                var d = Core.Get();
                l.Add(new SteamPeerCore.Packet { Peer = peer, Channel = ch, Mode = mode, Data = d });
            }
            return l;
        }
        public Error Send(int target, byte[] data, MultiplayerPeer.TransferModeEnum mode = MultiplayerPeer.TransferModeEnum.Reliable, int channel = 0)
        {
            Core.TargetPeer = target; Core.TransferMode = mode; Core.TransferChannel = channel;
            return Core.Put(data);
        }
    }

    static void Pump(params Node[] nodes) { for (int i = 0; i < 6; i++) foreach (var n in nodes) n.Core.Poll(); }
    static byte[] Bytes(int n, int seed) { var b = new byte[n]; for (int i = 0; i < n; i++) b[i] = (byte)(i * 31 + seed); return b; }
    static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);

    static void Transport()
    {
        var net = new FakeSteamNetwork();
        var h = new Node(net, 1001); var a = new Node(net, 2001); var b = new Node(net, 3001);
        Ok(h.Core.StartServer(), "host starts");
        Ok(h.Core.UniqueId == 1 && h.Core.IsServer && h.Core.Status == MultiplayerPeer.ConnectionStatus.Connected, "host is peer 1, server, connected");
        Ok(a.Core.StartClient(1001) && b.Core.StartClient(1001), "clients start");
        Ok(a.Core.Status == MultiplayerPeer.ConnectionStatus.Connecting, "client is Connecting before the hello");
        Pump(h, a, b);
        Ok(a.Core.Status == MultiplayerPeer.ConnectionStatus.Connected && b.Core.Status == MultiplayerPeer.ConnectionStatus.Connected, "clients connected after the hello");
        int ia = a.Core.UniqueId, ib = b.Core.UniqueId;
        Ok(ia >= 2 && ib >= 2 && ia != ib, $"client ids are positive and distinct ({ia}, {ib})");
        Ok(h.Connected.Count == 2 && h.Connected.Contains(ia) && h.Connected.Contains(ib), "host saw peer_connected for both clients");
        Ok(a.Connected.SequenceEqual(new[] { 1 }) && b.Connected.SequenceEqual(new[] { 1 }), "each client saw peer_connected(1) only");
        Ok(h.Core.RemoteSteamId(ia) == 2001 && h.Core.RemoteSteamId(ib) == 3001 && a.Core.RemoteSteamId(1) == 1001, "remote Steam ids are known (used by the password brake)");

        // broadcast, reliable, channel kept
        var p1 = Bytes(40, 1);
        Ok(h.Send(0, p1, MultiplayerPeer.TransferModeEnum.Reliable, 3) == Error.Ok, "broadcast put");
        Pump(h, a, b);
        var ra = a.Drain(); var rb = b.Drain();
        Ok(ra.Count == 1 && rb.Count == 1, "broadcast reaches both clients once");
        Ok(ra.Count == 1 && Same(ra[0].Data, p1) && ra[0].Peer == 1 && ra[0].Channel == 3 && ra[0].Mode == MultiplayerPeer.TransferModeEnum.Reliable, "broadcast: data, sender 1, channel 3, reliable");
        // targeted
        Ok(h.Send(ia, Bytes(10, 2)) == Error.Ok, "targeted put");
        Pump(h, a, b);
        Ok(a.Drain().Count == 1 && b.Drain().Count == 0, "targeted send reaches only the target");
        // all except
        Ok(h.Send(-ia, Bytes(10, 3)) == Error.Ok, "all-except put");
        Pump(h, a, b);
        Ok(a.Drain().Count == 0 && b.Drain().Count == 1, "negative target = everybody except that peer");
        Ok(h.Send(999999, Bytes(3, 3)) == Error.InvalidParameter, "send to an unknown peer is an error");
        // client -> server
        Ok(a.Send(1, Bytes(20, 4)) == Error.Ok && a.Send(0, Bytes(21, 5)) == Error.Ok, "client sends to 1 and to 0");
        Ok(a.Send(ib, Bytes(5, 5)) == Error.InvalidParameter, "client cannot address another client directly");
        Pump(h, a, b);
        var rh = h.Drain();
        Ok(rh.Count == 2 && rh.All(p => p.Peer == ia), "host receives both with the sender id");
        Ok(b.Drain().Count == 0, "client packets are not echoed to other clients");
        // modes
        Ok(h.Send(ia, Bytes(30, 6), MultiplayerPeer.TransferModeEnum.Unreliable, 1) == Error.Ok, "unreliable put");
        Ok(h.Sock.LastHow == SockSend.Unreliable, "small unreliable goes out unreliable");
        Pump(h, a, b);
        var ru = a.Drain();
        Ok(ru.Count == 1 && ru[0].Mode == MultiplayerPeer.TransferModeEnum.Unreliable && ru[0].Channel == 1, "unreliable mode and channel preserved");
        for (int i = 0; i < 5; i++) h.Send(ia, new[] { (byte)i }, MultiplayerPeer.TransferModeEnum.UnreliableOrdered, 2);
        Pump(h, a, b);
        var ro = a.Drain();
        Ok(ro.Count == 5 && ro.Select(p => (int)p.Data[0]).SequenceEqual(new[] { 0, 1, 2, 3, 4 }) && ro.All(p => p.Mode == MultiplayerPeer.TransferModeEnum.UnreliableOrdered), "unreliable ordered arrives in order with its mode");
        // ordered: an older sequence number is dropped (frames crafted by hand: [0x11][channel][seq lo][seq hi][payload])
        var lone = new SteamPeerCore(new NullSockets());
        lone.StartServer(); lone.OnIncoming(7, 1); lone.OnState(7, SockState.Connected, null);
        lone.OnMessage(7, new byte[] { 0x11, 0, 5, 0, 0xAA });
        lone.OnMessage(7, new byte[] { 0x11, 0, 3, 0, 0xBB });   // older: dropped
        lone.OnMessage(7, new byte[] { 0x11, 0, 6, 0, 0xCC });
        lone.OnMessage(7, new byte[] { 0x11, 1, 2, 0, 0xDD });   // another channel has its own counter
        var got = new List<byte>(); while (lone.Available > 0) got.Add(lone.Get()[0]);
        Ok(got.SequenceEqual(new byte[] { 0xAA, 0xCC, 0xDD }), "unreliable ordered drops an older packet, per channel");
        while (lone.Available > 0) lone.Get();
        lone.OnMessage(7, new byte[] { 0x11, 5, 0xFE, 0xFF, 0x01 }); lone.OnMessage(7, new byte[] { 0x11, 5, 0xFF, 0xFF, 0x02 }); lone.OnMessage(7, new byte[] { 0x11, 5, 1, 0, 0x03 });   // ... 65534, 65535, then 1: the counter wrapped, 1 is newer
        lone.OnMessage(7, new byte[] { 0x11, 5, 0xFF, 0xFF, 0x04 });   // an old one again: dropped
        Ok(lone.Available == 3, "sequence numbers survive wrap-around");
        while (lone.Available > 0) lone.Get();
        lone.OnMessage(7, new byte[] { 0x01 }); lone.OnMessage(7, new byte[] { 0x1F, 0, 1 }); lone.OnMessage(999, new byte[] { 0x12, 0, 1 }); lone.OnMessage(7, Array.Empty<byte>()); lone.OnMessage(7, new byte[] { 0x20, 1, 2, 0, 0, 0 });
        Ok(lone.Available == 0, "garbage frames, a hello sent to a server and unknown connections are ignored");

        // large packets
        int max = h.Core.MaxPacketSize;
        Ok(max >= 256 * 1024, $"max packet size is large ({max})");
        var big = Bytes(max, 9);
        Ok(h.Send(0, big) == Error.Ok, "a packet of exactly the maximum size is accepted");
        Pump(h, a, b);
        ra = a.Drain(); rb = b.Drain();
        Ok(ra.Count == 1 && rb.Count == 1 && Same(ra[0].Data, big) && Same(rb[0].Data, big), "maximum-size packet arrives intact on both clients");
        Ok(h.Send(0, new byte[max + 1]) == Error.OutOfMemory, "one byte more than the maximum is refused");
        var mid = Bytes(5000, 7);
        Ok(a.Send(1, mid, MultiplayerPeer.TransferModeEnum.Unreliable) == Error.Ok && a.Sock.LastHow == SockSend.Reliable, "an unreliable packet too big for one UDP packet is sent reliably");
        Pump(h, a, b);
        rh = h.Drain();
        Ok(rh.Count == 1 && Same(rh[0].Data, mid) && rh[0].Mode == MultiplayerPeer.TransferModeEnum.Unreliable, "...and still arrives intact with its original mode");
        Ok(h.Send(0, Array.Empty<byte>()) == Error.Ok, "an empty packet can be sent");
        Pump(h, a, b); Ok(a.Drain().Count == 1 && b.Drain().Count == 1, "...and arrives");

        // refuse new connections
        h.Core.RefuseNew = true;
        var d = new Node(net, 4001);
        Ok(d.Core.StartClient(1001), "refused client starts");
        Pump(h, a, b, d);
        Ok(d.Core.Status == MultiplayerPeer.ConnectionStatus.Disconnected && d.Connected.Count == 0 && d.Core.EndReason != "", "a refused client ends Disconnected with a reason");
        Ok(h.Connected.Count == 2 && h.Core.PeerCount == 2, "the host did not add the refused client");
        h.Core.RefuseNew = false;
        var e = new Node(net, 5001);
        e.Core.StartClient(1001); Pump(h, a, b, e);
        Ok(e.Core.Status == MultiplayerPeer.ConnectionStatus.Connected && h.Core.PeerCount == 3, "after refusing is switched off new clients get in again");
        int ie = e.Core.UniqueId;
        h.Core.MaxConnections = 3;
        var f = new Node(net, 6001); f.Core.StartClient(1001); Pump(h, a, b, e, f);
        Ok(f.Core.Status == MultiplayerPeer.ConnectionStatus.Disconnected && h.Core.PeerCount == 3, "connections beyond the maximum are closed");
        h.Core.MaxConnections = 16;

        // connecting to nobody
        var g = new Node(net, 7001);
        Ok(g.Core.StartClient(424242), "client to an unknown host starts");
        Pump(g);
        Ok(g.Core.Status == MultiplayerPeer.ConnectionStatus.Disconnected && g.Core.EndReason != "" && g.Disconnected.Count == 0, "no host: Disconnected with a reason, no peer_disconnected");

        // a client leaves
        e.Core.Close();
        Pump(h, a, b);
        Ok(h.Disconnected.Contains(ie) && h.Core.PeerCount == 2, "host saw peer_disconnected for the client that left");
        Ok(e.Core.Status == MultiplayerPeer.ConnectionStatus.Disconnected, "the leaving client is Disconnected");
        Ok(h.Send(ie, Bytes(3, 1)) == Error.InvalidParameter, "sending to the departed peer is an error");
        Ok(h.Send(0, Bytes(3, 2)) == Error.Ok, "broadcast still works");
        Pump(h, a, b);
        Ok(a.Drain().Count == 1 && b.Drain().Count == 1 && a.Core.Status == MultiplayerPeer.ConnectionStatus.Connected, "the others are unaffected");

        // host kicks one
        h.Core.DisconnectPeer(ib, false);
        Pump(h, a, b);
        Ok(h.Disconnected.Contains(ib) && b.Core.Status == MultiplayerPeer.ConnectionStatus.Disconnected && b.Disconnected.SequenceEqual(new[] { 1 }), "kicked client: host saw peer_disconnected, client saw peer_disconnected(1) and is Disconnected");

        // a kicked-for-wrong-password style flow: messages sent right before the close still arrive (linger)
        var k = new Node(net, 8001); k.Core.StartClient(1001); Pump(h, a, k);
        int ik = k.Core.UniqueId;
        h.Send(ik, new byte[] { 7, 7, 7 }); h.Core.DisconnectPeer(ik, false); Pump(h, a, k);
        Ok(k.Drain().Count == 1 && k.Core.Status == MultiplayerPeer.ConnectionStatus.Disconnected, "a message sent just before a disconnect is delivered first");

        // host goes away
        h.Core.Close();
        Pump(h, a);
        Ok(h.Core.Status == MultiplayerPeer.ConnectionStatus.Disconnected && !h.Sock.Listening, "closed host stops listening");
        Ok(a.Core.Status == MultiplayerPeer.ConnectionStatus.Disconnected && a.Disconnected.SequenceEqual(new[] { 1 }) && a.Core.EndReason != "", "when the host closes, the client sees peer_disconnected(1) and a reason");
        Ok(a.Send(1, Bytes(1, 1)) == Error.Unconfigured, "a disconnected peer cannot send");

        // peer id collision: the id source repeats itself
        var net2 = new FakeSteamNetwork();
        var h2 = new Node(net2, 1); var x = new Node(net2, 2); var y = new Node(net2, 3);
        var seq = new Queue<int>(new[] { 777, 777, 777, 778 });
        h2.Core.IdSource = () => seq.Count > 0 ? seq.Dequeue() : 12345;
        h2.Core.StartServer(); x.Core.StartClient(1); y.Core.StartClient(1); Pump(h2, x, y);
        Ok(x.Core.UniqueId == 777 && y.Core.UniqueId == 778, "a repeated random id is skipped, ids stay unique");
        // wrong protocol version in the hello
        var net3 = new FakeSteamNetwork();
        var cl = new Node(net3, 20); var fakeHost = net3.Node(10); fakeHost.Listen();
        cl.Core.StartClient(10);
        var hostEvents = new ProbeEvents(); fakeHost.Poll(hostEvents);
        fakeHost.Accept(hostEvents.Incoming); fakeHost.Poll(hostEvents);
        fakeHost.Send(hostEvents.Incoming, new byte[] { 0x20, 99, 5, 0, 0, 0 }, 6, SockSend.Reliable);
        Pump(cl);
        Ok(cl.Core.Status == MultiplayerPeer.ConnectionStatus.Disconnected && cl.Core.EndReason.Contains("version"), "a hello with another protocol version ends the connection with a clear reason");
        // data before the hello is ignored
        var cl2 = new Node(net3, 21); cl2.Core.StartClient(10);
        fakeHost.Poll(hostEvents); fakeHost.Accept(hostEvents.Incoming); fakeHost.Poll(hostEvents);
        fakeHost.Send(hostEvents.Incoming, new byte[] { 0x12, 0, 1, 2, 3 }, 5, SockSend.Reliable);
        Pump(cl2);
        Ok(cl2.Core.Available == 0 && cl2.Core.Status == MultiplayerPeer.ConnectionStatus.Connecting, "data before the hello is not delivered");
    }

    /// <summary>Accepts everything, sends nowhere: lets a test feed hand-made frames straight into a core.</summary>
    sealed class NullSockets : ISteamSockets
    {
        public bool Listen() => true; public void StopListen() { } public int Connect(ulong remoteId) => 1; public bool Accept(int conn) => true;
        public void Close(int conn, string reason, bool linger) { } public bool Send(int conn, byte[] data, int length, SockSend how) => true;
        public ulong RemoteId(int conn) => 0; public void Poll(ISocketEvents sink) { }
        public int MaxMessageSize => 512 * 1024; public int MaxUnreliableSize => 1100;
    }

    sealed class ProbeEvents : ISocketEvents
    {
        public int Incoming; public void OnIncoming(int conn, ulong remoteId) { Incoming = conn; }
        public void OnState(int conn, SockState state, string reason) { }
        public void OnMessage(int conn, byte[] data) { }
    }

    // ------------------------------------------------------------ (b) lobby codes
    static void Codes()
    {
        var rng = new Random(4242);
        int usual = 0, other = 0, shapeBad = 0, rtBad = 0, ipConfused = 0, lobbyAsIp = 0, typoMissed = 0, swapMissed = 0, typoTests = 0;
        var seen = new HashSet<string>();
        for (int i = 0; i < 4000; i++)
        {
            ulong id = i % 5 == 4 ? (ulong)rng.NextInt64(1, long.MaxValue) : (SteamLobbyCode.UsualHigh << 32) | (uint)rng.NextInt64(1, uint.MaxValue);
            bool isUsual = (id >> 32) == SteamLobbyCode.UsualHigh;
            string code = SteamLobbyCode.Encode(id);
            if (code == null) { shapeBad++; continue; }
            if (isUsual) { usual++; if (code.Length != 14 || code[0] != 'U' || code[4] != '-' || code[9] != '-') shapeBad++; } else { other++; if (code[0] != 'U') shapeBad++; }
            seen.Add(code);
            if (!SteamLobbyCode.TryDecode(code, out var back) || back != id) rtBad++;
            // forgiving: lower case, no dashes, spaces, O for 0, I/L for 1
            string sloppy = code.ToLowerInvariant().Replace("-", " ").Replace('0', 'o').Replace('1', 'l');
            if (!SteamLobbyCode.TryDecode(sloppy, out back) || back != id) rtBad++;
            if (RoomCode.TryDecode(code, out _, out _)) lobbyAsIp++;
            if (RoomCode.Resolve(code, 7777, out _, out _, out _)) lobbyAsIp++;
            if (!SteamLobbyCode.LooksLikeLobbyCode(code)) shapeBad++;
            if (i % 8 == 0)
            {
                string raw = code.Replace("-", "");
                for (int pos = 1; pos < raw.Length; pos++)
                {
                    char orig = raw[pos]; char r;
                    do r = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"[rng.Next(32)]; while (r == orig || (r == '0' && orig == 'O') || ((r == '1') && (orig == 'I' || orig == 'L')));
                    var chars = raw.ToCharArray(); chars[pos] = r;
                    typoTests++;
                    if (SteamLobbyCode.TryDecode(new string(chars), out var wrong) && wrong != id) typoMissed++;
                    if (pos + 1 < raw.Length && raw[pos] != raw[pos + 1])
                    {
                        var sw = raw.ToCharArray(); (sw[pos], sw[pos + 1]) = (sw[pos + 1], sw[pos]);
                        if (SteamLobbyCode.TryDecode(new string(sw), out var wrong2) && wrong2 != id) swapMissed++;
                    }
                }
            }
        }
        Ok(usual > 3000 && other > 500, $"both code forms were exercised ({usual} short, {other} long)");
        Ok(shapeBad == 0, $"code shape: UXXX-XXXX-XXXX for normal lobby ids, U-prefixed long form otherwise ({shapeBad} bad)");
        Ok(rtBad == 0, $"round trips incl. forgiving input ({rtBad} bad)");
        Ok(seen.Count >= 3990, $"codes are distinct ({seen.Count}/4000)");
        Ok(typoMissed == 0, $"every single-character typo is detected ({typoMissed} missed of {typoTests})");
        Ok(swapMissed == 0, $"every swap of two neighbouring characters is detected ({swapMissed} missed)");
        Ok(lobbyAsIp == 0, "no Steam code is ever accepted as an IP room code");
        for (int i = 0; i < 3000; i++)
        {
            string ip = $"{rng.Next(1, 224)}.{rng.Next(256)}.{rng.Next(256)}.{rng.Next(1, 255)}";
            string code = RoomCode.Encode(ip, rng.Next(1, 65536));
            if (SteamLobbyCode.TryDecode(code, out _) || SteamLobbyCode.LooksLikeLobbyCode(code)) ipConfused++;
        }
        Ok(ipConfused == 0, "no IP room code is ever read as a Steam code (IP codes never contain the letter U)");
        Ok(!SteamLobbyCode.TryDecode(null, out _) && !SteamLobbyCode.TryDecode("", out _) && !SteamLobbyCode.TryDecode("U", out _) && !SteamLobbyCode.TryDecode("UUUU-UUUU-UUUU", out _) && !SteamLobbyCode.TryDecode("U1234", out _), "garbage, short and empty input is refused");
        Ok(SteamLobbyCode.Encode(0) == null, "lobby id 0 has no code");
        Ok(!SteamLobbyCode.TryDecode("K7QM-4T2P-9XDA", out _), "an IP-style code is not a Steam code");
        Ok(!SteamLobbyCode.TryDecode("UK7QM-4T2P-9XDA-AAAA-BBBB-CCCC", out _), "an over-long code is refused");
    }

    // ------------------------------------------------------------ (c) SteamService without Steam
    static void Service()
    {
        bool threw = false, avail = true; string status = "";
        try { avail = SteamService.Init(force: true); status = SteamService.Status; SteamService.RunCallbacksNow(); SteamService.Shutdown(); }
        catch (Exception e) { threw = true; Util.Fail("steamcheck", "SteamService threw: " + e); }
        Ok(!threw, "SteamService.Init / RunCallbacksNow / Shutdown never throw");
        Ok(!avail && !SteamService.Available, "no Steam in this environment: Available is false");
        Ok(!string.IsNullOrEmpty(status), $"a reason is reported: {status}");
        Util.Log("STEAMCHECK: SteamService status = " + status);
        Ok(SteamService.TryParseConnect("+connect_lobby 109775241012345678", out var l) && l == 109775241012345678UL, "connect string is parsed");
        Ok(!SteamService.TryParseConnect("+connect_lobby", out _) && !SteamService.TryParseConnect("", out _) && !SteamService.TryParseConnect("+connect_lobby abc", out _) && !SteamService.TryParseConnect(null, out _), "bad connect strings are refused");
        Ok(!SteamService.UiSteam || SteamService.FakeUi, "UI is in Steam mode only with Steam or --fakesteam");
        Util.Log("STEAMCHECK: launch lobby id from the command line = " + SteamService.LaunchLobbyId());
        Ok(SteamConfig.AppId == 480, "App ID constant is the public test app 480");
        if (FileAccess.FileExists("res://steam_appid.txt")) Ok(FileAccess.GetFileAsString("res://steam_appid.txt").Trim() == SteamConfig.AppId.ToString(), "steam_appid.txt matches the constant");
        else Ok(false, "steam_appid.txt is missing from the project root");
    }

    // ------------------------------------------------------------ (d) the real SceneMultiplayer over the fake transport
    static void OverSceneMultiplayer()
    {
        var net = new FakeSteamNetwork();
        var hp = new SteamPeer().Use(net.Node(11)); var cp = new SteamPeer().Use(net.Node(22)); var cp2 = new SteamPeer().Use(net.Node(33));
        var hm = new SceneMultiplayer { RootPath = "/root" }; var cm = new SceneMultiplayer { RootPath = "/root" }; var cm2 = new SceneMultiplayer { RootPath = "/root" };
        Ok(hp.CreateHost() == Error.Ok, "SteamPeer host created");
        var hostSeen = new List<long>(); var clientSeen = new List<long>(); var packets = new List<(long, string)>(); var hostPackets = new List<(long, string)>();
        bool connectedToServer = false, serverDisc = false, failed2 = false;
        hm.PeerConnected += id => hostSeen.Add(id);
        cm.PeerConnected += id => clientSeen.Add(id);
        cm.ConnectedToServer += () => connectedToServer = true;
        cm.ServerDisconnected += () => serverDisc = true;
        cm.PeerPacket += (id, pk) => packets.Add((id, System.Text.Encoding.UTF8.GetString(pk)));
        hm.PeerPacket += (id, pk) => hostPackets.Add((id, System.Text.Encoding.UTF8.GetString(pk)));
        cm2.ConnectionFailed += () => failed2 = true;
        // authentication handshake through the real SceneMultiplayer (the same mechanism Net.cs uses for passwords)
        bool hostAuthDone = false, clientAuthDone = false;
        hm.AuthCallback = Callable.From<long, byte[]>((id, data) => { if (data.Length == 2 && data[0] == 0xA2) { hm.CompleteAuth((int)id); hostAuthDone = true; } });
        cm.AuthCallback = Callable.From<long, byte[]>((id, data) => { if (data.Length == 2 && data[0] == 0xA1) { cm.SendAuth(1, new byte[] { 0xA2, data[1] }); cm.CompleteAuth(1); clientAuthDone = true; } });
        hm.PeerAuthenticating += id => hm.SendAuth((int)id, new byte[] { 0xA1, 9 });
        hm.AuthTimeout = 5; cm.AuthTimeout = 5;
        hm.MultiplayerPeer = hp;
        Ok(cp.CreateClient(11) == Error.Ok, "SteamPeer client created");
        cm.MultiplayerPeer = cp;
        for (int i = 0; i < 8; i++) { hm.Poll(); cm.Poll(); }
        Ok(hostAuthDone && clientAuthDone, "the SceneMultiplayer authentication round trip works over the transport");
        Ok(connectedToServer, "client SceneMultiplayer raised connected_to_server");
        Ok(clientSeen.SequenceEqual(new long[] { 1 }), "client SceneMultiplayer saw peer_connected(1)");
        long cid = cm.GetUniqueId();
        Ok(cid >= 2 && hostSeen.SequenceEqual(new[] { cid }), $"host SceneMultiplayer saw peer_connected({cid})");
        Ok(hm.GetUniqueId() == 1 && hm.IsServer() && !cm.IsServer(), "unique ids and server flags");
        var err1 = hm.SendBytes(System.Text.Encoding.UTF8.GetBytes("hello client"), (int)cid, MultiplayerPeer.TransferModeEnum.Reliable, 0);
        var err2 = cm.SendBytes(System.Text.Encoding.UTF8.GetBytes("hello host"), 1, MultiplayerPeer.TransferModeEnum.Unreliable, 0);
        for (int i = 0; i < 4; i++) { hm.Poll(); cm.Poll(); }
        Ok(err1 == Error.Ok && err2 == Error.Ok, "SendBytes accepted");
        Ok(packets.Count == 1 && packets[0] == (1, "hello client"), "host to client packet arrives with the right sender");
        Ok(hostPackets.Count == 1 && hostPackets[0] == (cid, "hello host"), "client to host packet arrives with the right sender");
        // a connection that cannot be made: SceneMultiplayer reports connection_failed
        Ok(cp2.CreateClient(404) == Error.Ok, "client towards nobody starts");
        cm2.MultiplayerPeer = cp2;
        for (int i = 0; i < 6; i++) cm2.Poll();
        Ok(failed2, "an unreachable host makes SceneMultiplayer raise connection_failed");
        // host closes: the client is told
        hp.Close();
        for (int i = 0; i < 6; i++) cm.Poll();
        Ok(serverDisc, "host closing makes the client raise server_disconnected");
        cm.MultiplayerPeer = null; hm.MultiplayerPeer = null; cm2.MultiplayerPeer = null;
    }
}
