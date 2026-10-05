using System.Security.Cryptography;
using System.Text;

namespace StorageProof;

internal static class Envelope
{
    // Prototype v1: version, nonce, tag, ciphertext. Context binds location/role.
    public static byte[] Seal(byte[] key, byte[] value, string context)
    {
        var result = new byte[29 + value.Length];
        result[0] = 1;
        RandomNumberGenerator.Fill(result.AsSpan(1, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(result.AsSpan(1, 12), value, result.AsSpan(29),
            result.AsSpan(13, 16), Encoding.UTF8.GetBytes(context));
        return result;
    }

    public static byte[] Open(byte[] key, byte[] value, string context)
    {
        if (value.Length < 29 || value[0] != 1)
            throw new CryptographicException("Unsupported or truncated envelope.");
        var result = new byte[value.Length - 29];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(value.AsSpan(1, 12), value.AsSpan(29), value.AsSpan(13, 16),
            result, Encoding.UTF8.GetBytes(context));
        return result;
    }

    public static byte[] Token(byte[] key, string word) =>
        HMACSHA256.HashData(key, Encoding.UTF8.GetBytes("proof-index-v1:" + word.ToUpperInvariant()));
}

internal static class WindowsKey
{
    internal static DataProtectionScope Scope
    {
        get
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows.");
            return DataProtectionScope.CurrentUser;
        }
    }
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Kora-R02-synthetic-proof-v1");

    public static byte[] Wrap(byte[] key)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows.");
        return ProtectedData.Protect(key, Entropy, Scope);
    }

    public static byte[] Unwrap(byte[] blob)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI requires Windows.");
        return ProtectedData.Unprotect(blob, Entropy, Scope);
    }
}
