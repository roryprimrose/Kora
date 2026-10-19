using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Dependencies;

/// <summary>Read-only identity checks. Never discovers a substitute process, starts a server or installs a runtime.</summary>
internal static partial class WindowsOllamaToolProcessObservation
{
    internal static async Task<bool> IsCurrentAsync(WindowsOllamaToolEndpointBinding binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(binding.ProcessId);
            using var current = Process.GetCurrentProcess();
            if (process.HasExited || process.SessionId != current.SessionId
                || process.StartTime.ToUniversalTime() != binding.ProcessStartedAt.UtcDateTime
                || !string.Equals(process.MainModule?.FileName, binding.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                || !HasCurrentUser(process.SafeHandle) || !OwnsOnlyLoopbackListener(binding.ProcessId))
            { return false; }
            await using var executable = new FileStream(binding.ExecutablePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = await SHA256.HashDataAsync(executable, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return string.Equals(Convert.ToHexString(digest), binding.ExecutableSha256, StringComparison.OrdinalIgnoreCase)
                && !process.HasExited && process.StartTime.ToUniversalTime() == binding.ProcessStartedAt.UtcDateTime
                && OwnsOnlyLoopbackListener(binding.ProcessId);
        }
        catch (Exception exception) when (exception is Win32Exception or ArgumentException or InvalidOperationException
            or IOException or UnauthorizedAccessException)
        {
            // Inaccessible, vanished or changed identity is explicit unavailability, never permission to use loopback.
            return false;
        }
    }

    private static bool HasCurrentUser(SafeProcessHandle process)
    {
        if (!OpenProcessToken(process, 0x0008, out var token)) { return false; }
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        using (var current = WindowsIdentity.GetCurrent(TokenAccessLevels.Query))
        { return identity.User is not null && identity.User == current.User; }
    }

    private static unsafe bool OwnsOnlyLoopbackListener(int processId)
    {
        const uint insufficientBuffer = 122;
        const uint ipv4 = 2;
        const uint ownerPidListener = 3;
        uint size = 0;
        if (GetExtendedTcpTable(null, ref size, false, ipv4, ownerPidListener, 0) != insufficientBuffer
            || size is < 4 or > 1_048_576)
        { return false; }
        var bytes = new byte[size];
        fixed (byte* buffer = bytes)
        {
            if (GetExtendedTcpTable(buffer, ref size, false, ipv4, ownerPidListener, 0) != 0)
            { return false; }
        }
        return MatchesListener(bytes.AsSpan(0, checked((int)size)), processId);
    }

    internal static bool MatchesListener(ReadOnlySpan<byte> table, int processId)
    {
        const int rowSize = 24;
        if (table.Length < 4) { return false; }
        var count = BinaryPrimitives.ReadUInt32LittleEndian(table);
        if (count > (table.Length - 4) / rowSize) { return false; }
        var found = false;
        for (var index = 0; index < count; index++)
        {
            var row = table.Slice(4 + index * rowSize, rowSize);
            if (BinaryPrimitives.ReadUInt16BigEndian(row[8..]) != 11434) { continue; }
            if (found || BinaryPrimitives.ReadUInt32LittleEndian(row[4..]) != 0x0100007F
                || BinaryPrimitives.ReadUInt32LittleEndian(row[20..]) != processId)
            { return false; }
            found = true;
        }
        return found;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [LibraryImport("iphlpapi.dll")]
    private static unsafe partial uint GetExtendedTcpTable(byte* table, ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order, uint family, uint tableClass, uint reserved);
}
