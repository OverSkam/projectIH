using System;
using System.Text;

namespace AppStein;

/// <summary>
/// Room codes: an IPv4 address + UDP port packed into 12 characters (XXXX-XXXX-XXXX, Crockford base32).
/// Layout: 4 address bytes, 2 port bytes, 1 CRC-8 byte = 56 bits, lightly scrambled (XOR with a fixed key, then multiplication by an odd
/// constant modulo 2^56, so neighbouring addresses do not look alike and one typo breaks the checks), plus a 4-bit fold of the scrambled value.
/// The code only hides the address from a casual glance; it is not a secret. Pure logic, no Godot dependency.
/// </summary>
public static class RoomCode
{
    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    const ulong Mask56 = (1UL << 56) - 1;
    const ulong Key = 0x5A3C96E1B7D4F2UL & Mask56;
    const ulong Mul = 0x9E3779B97F4A7DUL | 1UL;            // odd => invertible mod 2^56
    static readonly ulong MulInv = Inverse(Mul) & Mask56;

    static ulong Inverse(ulong a)
    {
        ulong x = a;                                         // Newton iteration, doubles the number of correct low bits each step
        for (int i = 0; i < 6; i++) x *= 2UL - a * x;
        return x;
    }

    static byte Crc8(byte[] d, int n)
    {
        byte c = 0x5D;
        for (int i = 0; i < n; i++)
        {
            c ^= d[i];
            for (int b = 0; b < 8; b++) c = (byte)((c & 0x80) != 0 ? (c << 1) ^ 0x07 : c << 1);
        }
        return c;
    }

    static ulong Fold4(ulong v56)
    {
        ulong f = 0x9;
        for (int i = 0; i < 14; i++) f ^= (v56 >> (4 * i)) & 0xF;
        return f & 0xF;
    }

    /// <summary>12 characters in three groups, e.g. "K7QM-4T2P-9XDA". Returns null for an unusable address or port.</summary>
    public static string Encode(string ipv4, int port)
    {
        if (!TryParseIpv4(ipv4, out var ip) || port < 1 || port > 65535) return null;
        var b = new byte[7];
        Array.Copy(ip, b, 4); b[4] = (byte)(port >> 8); b[5] = (byte)port; b[6] = Crc8(b, 6);
        ulong v = 0;
        for (int i = 0; i < 7; i++) v = (v << 8) | b[i];
        v = ((v ^ Key) * Mul) & Mask56;
        v |= Fold4(v) << 56;
        var sb = new StringBuilder(14);
        for (int i = 11; i >= 0; i--)
        {
            sb.Append(Alphabet[(int)((v >> (5 * i)) & 31)]);
            if (i == 8 || i == 4) sb.Append('-');
        }
        return sb.ToString();
    }

    /// <summary>Forgiving decode: case-insensitive, spaces and dashes ignored, O is read as 0, I and L as 1. False on a typo (checksum) or wrong length.</summary>
    public static bool TryDecode(string text, out string ipv4, out int port)
    {
        ipv4 = null; port = 0;
        if (text == null) return false;
        ulong v = 0; int n = 0;
        foreach (char raw in text)
        {
            if (raw == ' ' || raw == '-' || raw == '\t' || raw == '_') continue;
            char c = char.ToUpperInvariant(raw);
            if (c == 'O') c = '0'; else if (c == 'I' || c == 'L') c = '1';
            int d = Alphabet.IndexOf(c);
            if (d < 0) return false;
            v = (v << 5) | (uint)d;
            if (++n > 12) return false;
        }
        if (n != 12) return false;
        if ((v >> 60) != 0) return false;
        ulong body = v & Mask56;
        if (Fold4(body) != (v >> 56)) return false;
        ulong plain = ((body * MulInv) & Mask56) ^ Key;
        var b = new byte[7];
        for (int i = 6; i >= 0; i--) { b[i] = (byte)(plain & 0xFF); plain >>= 8; }
        if (Crc8(b, 6) != b[6]) return false;
        int p = (b[4] << 8) | b[5];
        if (p == 0) return false;
        ipv4 = $"{b[0]}.{b[1]}.{b[2]}.{b[3]}"; port = p;
        return true;
    }

    public static bool TryParseIpv4(string s, out byte[] ip)
    {
        ip = null;
        if (s == null) return false;
        var parts = s.Trim().Split('.');
        if (parts.Length != 4) return false;
        var r = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            if (parts[i].Length == 0 || parts[i].Length > 3) return false;
            foreach (char ch in parts[i]) if (ch < '0' || ch > '9') return false;
            int v = int.Parse(parts[i]);
            if (v > 255) return false;
            r[i] = (byte)v;
        }
        ip = r;
        return true;
    }

    /// <summary>True for private, loopback, link-local and carrier-grade-NAT ranges (not reachable from the internet).</summary>
    public static bool IsPrivateOrShared(string ipv4)
    {
        if (!TryParseIpv4(ipv4, out var b)) return true;
        return b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224;
    }

    /// <summary>
    /// What the player typed in the "Room code" field: a room code, a raw IPv4 address, "IP:port", or a host name (anything with a dot or "localhost").
    /// On success host/port are filled (port falls back to defaultPort); otherwise error is a sentence for the player.
    /// </summary>
    public static bool Resolve(string input, int defaultPort, out string host, out int port, out string error)
    {
        host = null; port = defaultPort; error = null;
        string s = (input ?? "").Trim();
        if (s.Length == 0) { error = "Type the room code first."; return false; }
        bool looksLikeAddress = s.Contains('.') || s.Contains(':') || s.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        if (!looksLikeAddress)
        {
            if (TryDecode(s, out host, out port)) return true;
            error = "That code is not valid. Check it for typos and try again.";
            return false;
        }
        string h = s; int pt = defaultPort;
        int colon = s.LastIndexOf(':');
        if (colon >= 0)
        {
            h = s.Substring(0, colon).Trim();
            if (!int.TryParse(s.Substring(colon + 1).Trim(), out pt) || pt < 1 || pt > 65535) { error = "The port after the colon is not valid."; return false; }
        }
        bool numeric = h.Length > 0 && IsDigitsAndDots(h);
        if (numeric ? !TryParseIpv4(h, out _) : !IsHostName(h)) { error = "That address does not look right."; return false; }
        host = h; port = pt;
        return true;
    }

    static bool IsDigitsAndDots(string s) { foreach (char c in s) if (!(c == '.' || (c >= '0' && c <= '9'))) return false; return true; }

    static bool IsHostName(string s)
    {
        if (s.Length == 0 || s.Length > 253) return false;
        foreach (char c in s) if (!(char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-')) return false;
        return true;
    }
}
