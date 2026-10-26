using System.ComponentModel;
using System.Runtime.InteropServices;

using Kora.Windows.Coordination;
using Kora.Windows.Storage;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Artifacts;

internal sealed partial class WindowsOwnedDefinitionFileAccess
{
    internal static string InspectInstructionOriginal(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var file = OpenFile(path, 0x20080, 5);
        VerifyInstructionOriginal(file, path);
        return WindowsFileIdentity.Read(file);
    }

    private static void VerifyInstructionOriginal(SafeFileHandle file, string path)
    {
        VerifyFile(file, path);
        var info = Info(file);
        // Only ordinary local source attributes have source-preserving replacement semantics.
        if ((info.Attributes & ~(0x20u | 0x80u | 0x2000u)) != 0
            || !string.Equals(CoordinationNative.CanonicalPath(file), path, StringComparison.Ordinal))
        { throw new InvalidDataException("The instruction original is nonlocal, protected, or a case/path alias. Use the separate Kora-copy route for an admitted compatible source."); }
        var streams = new byte[512];
        if (!GetFileInformationByHandleEx(file, 7, streams, (uint)streams.Length))
        { throw NativeFailure("Original stream metadata cannot be verified; use the separate Kora-copy route instead."); }
        if (BitConverter.ToUInt32(streams, 0) != 0 || BitConverter.ToUInt32(streams, 4) != 14
            || !streams.AsSpan(24, 14).SequenceEqual(System.Text.Encoding.Unicode.GetBytes("::$DATA")))
        { throw new InvalidDataException("Named or unsupported source streams cannot be round-tripped in place. Use the separate Kora-copy route instead; the original is unchanged."); }
    }

    private static SafeFileHandle OpenInstructionOriginalForReplacement(string path)
    {
        var original = OpenFile(path, 0x80020000, 5);
        try { VerifyInstructionOriginal(original, path); return original; }
        catch { original.Dispose(); throw; }
    }

    private static void PreserveInstructionSecurity(SafeFileHandle original, SafeFileHandle staged)
    {
        var status = GetSecurityInfo(original, 1, 7, out _, out _, out _, out _, out var descriptor);
        if (status != 0)
        { throw new IOException("Original security metadata could not be captured; no replacement is approved.", new Win32Exception((int)status)); }
        try
        {
            if (!SetKernelObjectSecurity(staged, 7, descriptor))
            { throw NativeFailure("Original security metadata cannot be preserved; use the separate Kora-copy route instead."); }
            var info = Info(original);
            var basic = new byte[40];
            BitConverter.GetBytes(((long)info.CreatedHigh << 32) | info.CreatedLow).CopyTo(basic, 0);
            BitConverter.GetBytes(info.Attributes).CopyTo(basic, 32);
            if (!SetFileInformationByHandle(staged, 0, basic, (uint)basic.Length))
            { throw NativeFailure("Original source attributes cannot be preserved; use the separate Kora-copy route instead."); }
        }
        finally { LocalFree(descriptor); }
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetKernelObjectSecurity(SafeFileHandle handle, uint information, nint descriptor);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, byte[] information, uint size);
}