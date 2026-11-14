using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace AppStein;

/// <summary>
/// Headless tools: `-- --netcheck` self-check of room codes, address parsing and the password hash (exit code 1 on failure);
/// `-- --printcode=IP[:port]` prints the room code for an address (used by tools/net_test.sh).
/// </summary>
[ContentModule]
public static class NetTools
{
    public static void Register()
    {
        bool check = Util.HasArg("netcheck"), print = Util.HasArg("printcode");
        if (!check && !print) return;
        int fails = 0;
        if (print)
        {
            string a = Util.Arg("printcode", "127.0.0.1");
            string host = a; int port = Net.DefaultPort;
            int c = a.LastIndexOf(':');
            if (c >= 0) { host = a.Substring(0, c); int.TryParse(a.Substring(c + 1), out port); }
            string code = RoomCode.Encode(host, port);
            if (code == null) { Util.Fail("printcode", "bad address " + a); fails = 1; }
            else GD.Print("ROOMCODE: " + code);
        }
        if (check) fails += NetCheck.Run();
        var tree = Engine.GetMainLoop() as SceneTree;
        if (tree != null) tree.ProcessFrame += () => tree.Quit(fails == 0 ? 0 : 1);
    }
}

public static class NetCheck
{
    public static int Run()
    {
        int fails = 0;
        void Bad(string m) { fails++; Util.Fail("netcheck", m); }
        var rng = new Random(12345);
        string RandIp() => $"{rng.Next(1, 224)}.{rng.Next(256)}.{rng.Next(256)}.{rng.Next(1, 255)}";

        // ---- round trips
        int rt = 0, shapeBad = 0;
        var seen = new HashSet<string>();
        for (int i = 0; i < 5000; i++)
        {
            string ip = RandIp(); int port = i % 7 == 0 ? Net.DefaultPort : rng.Next(1, 65536);
            string code = RoomCode.Encode(ip, port);
            if (code == null || code.Length != 14 || code[4] != '-' || code[9] != '-') { shapeBad++; continue; }
            if (!RoomCode.TryDecode(code, out var ip2, out var port2) || ip2 != ip || port2 != port) { Bad($"round trip failed for {ip}:{port} -> {code} -> {ip2}:{port2}"); break; }
            seen.Add(code);
            rt++;
        }
        if (shapeBad > 0) Bad($"{shapeBad} codes did not have the XXXX-XXXX-XXXX shape");
        if (rt == 5000) Util.Pass("netcheck_roundtrip", $"{rt} random ip/port pairs");
        // the code must not look like the address: no digits-and-dots, neighbouring addresses differ in many characters
        string c1 = RoomCode.Encode("192.168.1.10", 7777), c2 = RoomCode.Encode("192.168.1.11", 7777);
        int diff = 0; for (int i = 0; i < c1.Length; i++) if (c1[i] != c2[i]) diff++;
        if (diff < 6) Bad($"neighbouring addresses give similar codes: {c1} / {c2}");
        else Util.Pass("netcheck_scrambled", $"{c1} vs {c2}");

        // ---- typo detection: change one character, transpose two neighbours
        int tried = 0, missed = 0;
        for (int i = 0; i < 4000; i++)
        {
            string code = RoomCode.Encode(RandIp(), rng.Next(1, 65536));
            var ch = code.ToCharArray();
            int pos; do pos = rng.Next(14); while (ch[pos] == '-');
            char orig = ch[pos], repl;
            const string alpha = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
            do repl = alpha[rng.Next(32)]; while (repl == orig);
            ch[pos] = repl;
            tried++;
            if (RoomCode.TryDecode(new string(ch), out var ipX, out _)) missed++;
        }
        if (missed > tried / 200) Bad($"single-character typos slip through too often: {missed}/{tried}");
        else Util.Pass("netcheck_typo", $"{tried - missed}/{tried} single-character typos caught");
        if (RoomCode.TryDecode("0000-0000-000", out _, out _)) Bad("too short code accepted");
        if (RoomCode.TryDecode("0000-0000-0000-0", out _, out _)) Bad("too long code accepted");
        if (RoomCode.TryDecode("UUUU-UUUU-UUUU", out _, out _)) Bad("invalid letters accepted");
        if (RoomCode.TryDecode("", out _, out _) || RoomCode.TryDecode(null, out _, out _)) Bad("empty code accepted");

        // ---- forgiving input
        string good = RoomCode.Encode("203.0.113.45", 7777);
        var variants = new List<string> { good, good.ToLowerInvariant(), good.Replace("-", ""), good.Replace("-", " "), "  " + good + "  ", good.ToLowerInvariant().Replace("-", " - ") };
        bool forgiving = true;
        foreach (var v in variants)
            if (!RoomCode.TryDecode(v, out var i3, out var p3) || i3 != "203.0.113.45" || p3 != 7777) { forgiving = false; Bad($"variant not accepted: '{v}'"); }
        // O/I/L: find a code with a 0 or 1 and confirm the look-alikes decode to the same thing
        int lookAlikes = 0;
        for (int i = 0; i < 300 && lookAlikes < 25; i++)
        {
            string ip = RandIp(); string code = RoomCode.Encode(ip, 7777);
            if (!code.Contains('0') && !code.Contains('1')) continue;
            string alt = code.Replace('0', 'o').Replace('1', i % 2 == 0 ? 'I' : 'l');
            if (!RoomCode.TryDecode(alt, out var ia, out var pa) || ia != ip || pa != 7777) { forgiving = false; Bad($"O/I/L not forgiven: {code} -> {alt}"); }
            lookAlikes++;
        }
        if (lookAlikes < 10) Bad("not enough codes with 0/1 to test look-alikes");
        if (forgiving) Util.Pass("netcheck_forgiving", $"case, spaces, dashes, O/I/L ({lookAlikes} look-alike codes)");

        // ---- raw address parsing
        bool parse = true;
        void Expect(string input, string host, int port, bool ok = true)
        {
            bool r = RoomCode.Resolve(input, 7777, out var h, out var p, out var err);
            if (r != ok || (ok && (h != host || p != port)) || (!ok && string.IsNullOrEmpty(err))) { parse = false; Bad($"Resolve('{input}') -> {r} {h}:{p} {err}"); }
        }
        Expect("192.168.1.20", "192.168.1.20", 7777);
        Expect(" 10.0.0.5:7000 ", "10.0.0.5", 7000);
        Expect("127.0.0.1:7911", "127.0.0.1", 7911);
        Expect("localhost", "localhost", 7777);
        Expect("example.com:8000", "example.com", 8000);
        Expect(good.ToLowerInvariant(), "203.0.113.45", 7777);
        Expect("999.1.1.1", null, 0, false);
        Expect("1.2.3", null, 0, false);
        Expect("1.2.3.4:0", null, 0, false);
        Expect("1.2.3.4:99999", null, 0, false);
        Expect("1.2.3.4:abc", null, 0, false);
        Expect("", null, 0, false);
        Expect("ABCD-EFGH-JKMN", null, 0, false);   // 12 characters but wrong checksum
        Expect("hello world!", null, 0, false);
        RoomCode.Resolve("ABCD-EFGH-JKMN", 7777, out _, out _, out var msg);
        if (!msg.Contains("not valid")) { parse = false; Bad("typo message should say 'not valid': " + msg); }
        if (parse) Util.Pass("netcheck_parse", "room code, raw IP, IP:port, host name, bad input");
        if (RoomCode.IsPrivateOrShared("203.0.113.45") || !RoomCode.IsPrivateOrShared("192.168.0.1") || !RoomCode.IsPrivateOrShared("100.64.1.1") || !RoomCode.IsPrivateOrShared("10.1.2.3"))
            Bad("IsPrivateOrShared wrong");
        else Util.Pass("netcheck_private_ranges");

        // ---- password hash helper
        var n1 = RoomAuth.NewNonce(); var n2 = RoomAuth.NewNonce();
        bool pw = true;
        if (n1.Length != RoomAuth.NonceLen || n1.SequenceEqual(n2)) { pw = false; Bad("nonces are not random"); }
        var h1 = RoomAuth.Hash(n1, "secret");
        if (h1.Length != 32) { pw = false; Bad("hash is not 32 bytes"); }
        if (!RoomAuth.Verify(n1, "secret", h1)) { pw = false; Bad("right password rejected"); }
        if (RoomAuth.Verify(n1, "Secret", RoomAuth.Hash(n1, "Secret")) == false) { pw = false; Bad("verify of own hash failed"); }
        if (RoomAuth.Verify(n1, "secret", RoomAuth.Hash(n1, "Secret"))) { pw = false; Bad("wrong password (case) accepted"); }
        if (RoomAuth.Verify(n1, "secret", RoomAuth.Hash(n2, "secret"))) { pw = false; Bad("hash for another nonce accepted (replay)"); }
        if (RoomAuth.Verify(n1, "secret", null) || RoomAuth.Verify(n1, "secret", new byte[5])) { pw = false; Bad("malformed answer accepted"); }
        if (!RoomAuth.Verify(n1, "", RoomAuth.Hash(n1, null))) { pw = false; Bad("empty password hash mismatch"); }
        if (System.Text.Encoding.UTF8.GetString(h1).Contains("secret")) { pw = false; Bad("hash contains the password"); }
        if (RoomAuth.Hash(n1, "pässwörd ✓").Length != 32 || !RoomAuth.Verify(n1, "pässwörd ✓", RoomAuth.Hash(n1, "pässwörd ✓"))) { pw = false; Bad("unicode password"); }
        if (pw) Util.Pass("netcheck_password", "nonce, SHA-256(nonce+password), constant-time verify, replay and malformed answers");

        if (fails == 0) Util.Pass("netcheck", "all room code and password checks");
        return fails;
    }
}
