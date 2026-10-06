using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Kora.Windows.Storage;

internal static partial class CurrentUserKeyProtection
{
    private const int UiForbidden = 1;
    private const int MaximumNativeBlobBytes = 4096;

    internal static byte[] Protect(byte[] plaintext)
    {
        return Transform(plaintext, protect: true);
    }

    internal static byte[] Unprotect(byte[] ciphertext)
    {
        return Transform(ciphertext, protect: false);
    }

    private static byte[] Transform(byte[] bytes, bool protect)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Storage key custody requires Windows CurrentUser DPAPI.");
        }

        if (bytes.Length is 0 or > MaximumNativeBlobBytes)
        {
            throw new InvalidDataException("The DPAPI input exceeds the key wrapper bound.");
        }

        var entropyBytes = "Kora.Windows.Storage.KeyWrapper.v1"u8.ToArray();
        DataBlob input = default;
        DataBlob entropy = default;
        DataBlob output = default;
        try
        {
            input = Allocate(bytes);
            entropy = Allocate(entropyBytes);
            // LocalMachine is deliberately never supplied. No UI or scope fallback is permitted.
            var succeeded = protect
                ? CryptProtectData(ref input, null, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero, UiForbidden, out output);
            if (!succeeded)
            {
                throw new CryptographicException("CurrentUser key protection failed.",
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }

            if (output.Length is <= 0 or > MaximumNativeBlobBytes || output.Data == IntPtr.Zero)
            {
                throw new InvalidDataException("DPAPI returned an invalid key wrapper.");
            }

            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            Clear(input);
            if (input.Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(input.Data);
            }
            Clear(entropy);
            if (entropy.Data != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(entropy.Data);
            }
            if (output.Data != IntPtr.Zero)
            {
                Clear(output);
                _ = LocalFree(output.Data);
            }
        }
    }

    private static DataBlob Allocate(byte[] bytes)
    {
        var data = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, data, bytes.Length);
        return new DataBlob { Length = bytes.Length, Data = data };
    }

    private static void Clear(DataBlob blob)
    {
        if (blob.Length is > 0 and <= MaximumNativeBlobBytes && blob.Data != IntPtr.Zero)
        {
            Marshal.Copy(new byte[blob.Length], 0, blob.Data, blob.Length);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        internal int Length;
        internal IntPtr Data;
    }

    [LibraryImport("crypt32.dll", EntryPoint = "CryptProtectData", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [LibraryImport("crypt32.dll", EntryPoint = "CryptUnprotectData", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob entropy,
        IntPtr reserved, IntPtr prompt, int flags, out DataBlob output);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
