using System;
using System.Security.Cryptography;
using System.Text;

namespace AppStein;

/// <summary>Password handshake helpers (pure, no Godot): the server sends a random nonce, the client answers SHA-256(nonce + password). The password itself is never sent.</summary>
public static class RoomAuth
{
    public const int NonceLen = 16, HashLen = 32;

    public static byte[] NewNonce() => RandomNumberGenerator.GetBytes(NonceLen);

    public static byte[] Hash(byte[] nonce, string password)
    {
        var pw = Encoding.UTF8.GetBytes(password ?? "");
        var all = new byte[nonce.Length + pw.Length];
        Buffer.BlockCopy(nonce, 0, all, 0, nonce.Length);
        Buffer.BlockCopy(pw, 0, all, nonce.Length, pw.Length);
        return SHA256.HashData(all);
    }

    /// <summary>Constant-time comparison of the client's answer with the hash the server computes itself.</summary>
    public static bool Verify(byte[] nonce, string password, byte[] answer) =>
        answer != null && answer.Length == HashLen && CryptographicOperations.FixedTimeEquals(Hash(nonce, password), answer);
}
