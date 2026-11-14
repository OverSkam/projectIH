using System;
using System.Collections.Generic;
using System.Text;

namespace AppStein;

/// <summary>
/// Room codes for Steam rooms: the Steam lobby ID shortened to "UXXX-XXXX-XXXX". Pure logic, no Godot / Steam dependency.
/// The first character is always U, a letter the IP room codes (RoomCode, Crockford base32 without I L O U) can never contain, so the two kinds
/// of code are never confused: RoomCode.TryDecode rejects every Steam code and SteamLobbyCode.TryDecode rejects every IP code.
/// Short form (12 characters): normal Steam lobby IDs (universe 1, type "chat", MMS-lobby instance) differ only in their low 32 bits, so the code carries those
/// 32 bits + a 23-bit check value, scrambled (XOR + odd multiplier mod 2^55) so that one typo breaks the check. Long form (19 characters, "U" + 18):
/// the full 64-bit ID + 26-bit check, used when an ID does not have the usual high bits. The code is not a secret: it is only the lobby ID in a typable form.
/// </summary>
public static class SteamLobbyCode
{
    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const char Marker = 'U';
    /// <summary>High 32 bits of an ordinary Steam matchmaking lobby ID (universe public, type chat, lobby + MMS instance flags).</summary>
    public const ulong UsualHigh = 0x01860000UL;
    const ulong Mask55 = (1UL << 55) - 1;
    const ulong Key = 0x2B7E1516A09F3DUL & Mask55;
    const ulong Mul = 0x5851F42D4C957FUL | 1UL;
    static readonly ulong MulInv = Inverse(Mul) & Mask55;

    static ulong Inverse(ulong a) { ulong x = a; for (int i = 0; i < 6; i++) x *= 2UL - a * x; return x; }

    static uint Crc32(ulong v, int bytes, uint seed)
    {
        uint c = 0xFFFFFFFF ^ seed;
        for (int i = 0; i < bytes; i++)
        {
            c ^= (byte)(v >> (8 * i));
            for (int b = 0; b < 8; b++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
        }
        return ~c;
    }

    /// <summary>The code for a lobby ID, or null for ID 0.</summary>
    public static string Encode(ulong lobbyId)
    {
        if (lobbyId == 0) return null;
        var sb = new StringBuilder(24);
        sb.Append(Marker);
        if ((lobbyId >> 32) == UsualHigh)
        {
            ulong acct = lobbyId & 0xFFFFFFFFUL;
            ulong check = Crc32(acct, 4, 0x4C4F4231) & 0x7FFFFF;                 // 23 bits
            ulong v = ((acct << 23) | check) & Mask55;
            v = ((v ^ Key) * Mul) & Mask55;
            for (int i = 10; i >= 0; i--) sb.Append(Alphabet[(int)((v >> (5 * i)) & 31)]);
        }
        else
        {
            UInt128 v = ((UInt128)lobbyId << 26) | (Crc32(lobbyId, 8, 0x4C4F4232) & 0x3FFFFFF);   // 90 bits
            for (int i = 17; i >= 0; i--) sb.Append(Alphabet[(int)((v >> (5 * i)) & 31)]);
        }
        return Group(sb.ToString());
    }

    static string Group(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++) { if (i > 0 && i % 4 == 0) sb.Append('-'); sb.Append(s[i]); }
        return sb.ToString();
    }

    /// <summary>True when the text starts with the Steam marker letter after cleaning (it may still contain a typo).</summary>
    public static bool LooksLikeLobbyCode(string text)
    {
        if (text == null) return false;
        foreach (char raw in text)
        {
            if (raw == ' ' || raw == '-' || raw == '\t' || raw == '_') continue;
            return char.ToUpperInvariant(raw) == Marker;
        }
        return false;
    }

    /// <summary>Forgiving decode (case, spaces, dashes ignored; O = 0, I and L = 1). False on a typo, a wrong length or an IP code.</summary>
    public static bool TryDecode(string text, out ulong lobbyId)
    {
        lobbyId = 0;
        if (text == null) return false;
        var digits = new List<int>();
        bool first = true;
        foreach (char raw in text)
        {
            if (raw == ' ' || raw == '-' || raw == '\t' || raw == '_') continue;
            char c = char.ToUpperInvariant(raw);
            if (first) { first = false; if (c != Marker) return false; continue; }
            if (c == 'O') c = '0'; else if (c == 'I' || c == 'L') c = '1';
            int d = Alphabet.IndexOf(c);
            if (d < 0) return false;
            digits.Add(d);
            if (digits.Count > 18) return false;
        }
        if (digits.Count == 11)
        {
            ulong v = 0; foreach (int d in digits) v = (v << 5) | (uint)d;
            if ((v >> 55) != 0) return false;
            ulong plain = ((v * MulInv) & Mask55) ^ Key;
            ulong acct = plain >> 23, check = plain & 0x7FFFFF;
            if (acct > 0xFFFFFFFFUL) return false;
            if ((Crc32(acct, 4, 0x4C4F4231) & 0x7FFFFF) != check) return false;
            lobbyId = (UsualHigh << 32) | acct;
            return lobbyId != 0;
        }
        if (digits.Count == 18)
        {
            UInt128 v = 0; foreach (int d in digits) v = (v << 5) | (uint)d;
            if ((v >> 90) != 0) return false;
            ulong id = (ulong)(v >> 26); uint check = (uint)(v & 0x3FFFFFF);
            if ((Crc32(id, 8, 0x4C4F4232) & 0x3FFFFFF) != check) return false;
            lobbyId = id;
            return id != 0;
        }
        return false;
    }
}
