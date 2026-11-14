using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace AppStein;

public enum RoomNetState { Off, Checking, Ready, Failed }

/// <summary>
/// Makes a hosted room reachable from the internet, best effort: opens the UDP port on the home router with UPnP (on a worker thread, discovery takes seconds),
/// asks a public "what is my IP" service if the router did not tell us our address, and keeps the two room codes (internet + same Wi-Fi).
/// Hosting itself never waits for this. Every failure ends in <see cref="RoomNetState.Failed"/>, never in an exception or a hang.
/// Skipped completely (no network traffic) in automated / headless runs and with --noupnp.
/// NOT verifiable without a real router: see docs/ARCHITECTURE.md.
/// </summary>
public partial class PortOpener : Node
{
    public static readonly string[] IpServices = { "https://api.ipify.org", "https://ifconfig.me/ip" };

    public RoomNetState State { get; private set; } = RoomNetState.Off;
    public string PublicIp { get; private set; }
    public string LanIp { get; private set; }
    public int Port { get; private set; } = Net.DefaultPort;
    public event Action Changed;

    public string InternetCode => PublicIp != null ? RoomCode.Encode(PublicIp, Port) : null;
    public string LanCode => LanIp != null ? RoomCode.Encode(LanIp, Port) : null;

    /// <summary>True when no network traffic may happen: automated runs, headless, or --noupnp.</summary>
    public static bool Skip => Util.HasArg("noupnp") || Util.Automated || DisplayServer.GetName() == "headless";

    int _gen;
    readonly object _lock = new();
    Upnp _upnp; int _mappedPort;
    Task _worker, _cleanup;

    public void Start(int port)
    {
        Stop();
        Port = port;
        LanIp = FindLanIp();
        string fake = Util.Arg("fakeupnp", "");   // screenshot staging only
        if (fake != "")
        {
            PublicIp = fake == "failed" ? null : "203.0.113.45"; LanIp = "192.168.1.23";
            State = fake == "checking" ? RoomNetState.Checking : fake == "ready" ? RoomNetState.Ready : RoomNetState.Failed;
            Changed?.Invoke();
            return;
        }
        if (Skip) { State = RoomNetState.Off; Changed?.Invoke(); return; }
        State = RoomNetState.Checking;
        Changed?.Invoke();
        int gen = ++_gen;
        _worker = Task.Run(() => Work(port, gen));
    }

    /// <summary>The host stopped hosting: forget the codes and take the port mapping off the router (in the background).</summary>
    public void Stop()
    {
        _gen++;
        State = RoomNetState.Off; PublicIp = null;
        Upnp u; int p;
        lock (_lock) { u = _upnp; p = _mappedPort; _upnp = null; _mappedPort = 0; }
        if (u != null) _cleanup = Task.Run(() => Unmap(u, p));
        Changed?.Invoke();
    }

    /// <summary>At exit: give the removal a short moment to finish, never hang.</summary>
    public void WaitForCleanup(int ms)
    {
        try { Task.WaitAll(new[] { _worker, _cleanup }.Where(t => t != null).ToArray(), ms); } catch { }
    }

    // ------------------------------------------------------------ worker thread
    void Work(int port, int gen)
    {
        bool mapped = false; string ext = null;
        try
        {
            var u = new Upnp();
            int r = u.Discover(2000, 2, "InternetGatewayDevice");
            var gw = u.GetGateway();
            if (r == 0 && gw != null && gw.IsValidGateway())
            {
                int m = u.AddPortMapping(port, port, "App Stein", "UDP", 0);
                if (m != 0) m = u.AddPortMapping(port, port, "App Stein", "UDP", 86400);   // some routers refuse "forever"
                mapped = m == 0;
                if (mapped)
                {
                    lock (_lock) { _upnp = u; _mappedPort = port; }
                    try { ext = u.QueryExternalAddress(); } catch { ext = null; }
                }
            }
        }
        catch (Exception e) { GD.Print("NET: UPnP failed: " + e.Message); }
        if (gen != _gen && mapped)   // hosting already stopped while we were busy: undo at once
        {
            Upnp u2; int p2;
            lock (_lock) { u2 = _upnp; p2 = _mappedPort; _upnp = null; _mappedPort = 0; }
            if (u2 != null) Unmap(u2, p2);
            return;
        }
        Callable.From(() => OnWorkerDone(gen, mapped, ext)).CallDeferred();
    }

    static void Unmap(Upnp u, int port)
    {
        try { u.DeletePortMapping(port, "UDP"); GD.Print("NET: UPnP mapping removed"); } catch (Exception e) { GD.Print("NET: UPnP removal failed: " + e.Message); }
    }

    // ------------------------------------------------------------ main thread
    void OnWorkerDone(int gen, bool mapped, string ext)
    {
        if (gen != _gen) return;
        bool extUsable = !string.IsNullOrEmpty(ext) && !RoomCode.IsPrivateOrShared(ext);
        bool doubleNat = !string.IsNullOrEmpty(ext) && !extUsable;   // the router itself sits behind another router: the mapping is useless
        GD.Print($"NET: UPnP mapped={mapped} external={(string.IsNullOrEmpty(ext) ? "none" : ext)}");
        if (mapped && extUsable) { Finish(gen, ext, true); return; }
        LookupPublicIp(gen, 0, ip => Finish(gen, ip, mapped && !doubleNat));
    }

    void Finish(int gen, string ip, bool ready)
    {
        if (gen != _gen) return;
        PublicIp = ip;
        State = ready && ip != null ? RoomNetState.Ready : RoomNetState.Failed;
        GD.Print($"NET: room state {State} public={(ip ?? "unknown")}");
        Changed?.Invoke();
    }

    /// <summary>Asks the public services in turn. Sends only the bare request.</summary>
    void LookupPublicIp(int gen, int index, Action<string> done)
    {
        if (gen != _gen) return;
        if (index >= IpServices.Length) { done(null); return; }
        try
        {
            var http = new HttpRequest { Timeout = 5 };
            AddChild(http);
            http.RequestCompleted += (result, code, headers, body) =>
            {
                http.QueueFree();
                string text = result == (long)HttpRequest.Result.Success && code == 200 ? System.Text.Encoding.UTF8.GetString(body).Trim() : "";
                if (RoomCode.TryParseIpv4(text, out _) && !RoomCode.IsPrivateOrShared(text)) done(text);
                else LookupPublicIp(gen, index + 1, done);
            };
            if (http.Request(IpServices[index]) != Error.Ok) { http.QueueFree(); LookupPublicIp(gen, index + 1, done); }
        }
        catch (Exception e) { GD.Print("NET: IP lookup failed: " + e.Message); LookupPublicIp(gen, index + 1, done); }
    }

    /// <summary>Best guess of this PC's address on the home network (192.168.x.x first, then 10.x, then 172.16-31).</summary>
    public static string FindLanIp()
    {
        var l = new List<string>();
        try
        {
            foreach (var a in IP.GetLocalAddresses())
                if (RoomCode.TryParseIpv4(a, out var b) && b[0] != 127 && !(b[0] == 169 && b[1] == 254) && b[0] != 0) l.Add(a);
        }
        catch { }
        return l.OrderBy(a => a.StartsWith("192.168.") ? 0 : a.StartsWith("10.") ? 1 : a.StartsWith("172.") ? 2 : 3).FirstOrDefault();
    }
}
