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
    internal static Task<bool> IsCurrentAsync(WindowsOllamaToolEndpointBinding binding, CancellationToken token) =>
        IsCurrentAsync(new WindowsOllamaRuntimeIdentity(binding.ProcessId, binding.ProcessStartedAt,
            binding.ExecutablePath, binding.ExecutableSha256), token);

    internal static async Task<bool> IsCurrentAsync(WindowsOllamaRuntimeIdentity binding, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var process = Process.GetProcessById(binding.ProcessId);
            using var current = Process.GetCurrentProcess();
            if (process.HasExited || process.SessionId != current.SessionId
                || process.StartTime.ToUniversalTime() != binding.StartedAt.UtcDateTime
                || !string.Equals(process.MainModule?.FileName, binding.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                || !HasCurrentUser(process.SafeHandle) || !OwnsOnlyLoopbackListener(binding.ProcessId))
            { return false; }
            await using var executable = new FileStream(binding.ExecutablePath, FileMode.Open, FileAccess.Read,
                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var digest = await SHA256.HashDataAsync(executable, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return string.Equals(Convert.ToHexString(digest), binding.ExecutableSha256, StringComparison.OrdinalIgnoreCase)
                && !process.HasExited && process.StartTime.ToUniversalTime() == binding.StartedAt.UtcDateTime
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

    private static bool OwnsOnlyLoopbackListener(int processId)
    {
        var ipv4 = ReadListeners(2);
        var ipv6 = ReadListeners(23);
        return ipv4 is not null && ipv6 is not null && MatchesListener(ipv4, processId) && HasNoIpv6Listener(ipv6);
    }

    private static unsafe byte[]? ReadListeners(uint family)
    {
        const uint insufficientBuffer = 122;
        const uint ownerPidListener = 3;
        uint size = 0;
        if (GetExtendedTcpTable(null, ref size, false, family, ownerPidListener, 0) != insufficientBuffer
            || size is < 4 or > 1_048_576)
        { return null; }
        var bytes = new byte[size];
        fixed (byte* buffer = bytes)
        {
            if (GetExtendedTcpTable(buffer, ref size, false, family, ownerPidListener, 0) != 0)
            { return null; }
        }
        return bytes.AsSpan(0, checked((int)size)).ToArray();
    }

    internal static bool HasNoIpv6Listener(ReadOnlySpan<byte> table)
    {
        const int rowSize = 56;
        if (table.Length < 4) { return false; }
        var count = BinaryPrimitives.ReadUInt32LittleEndian(table);
        if (count > (table.Length - 4) / rowSize) { return false; }
        for (var index = 0; index < count; index++)
        {
            // This exact profile authorizes only the IPv4 endpoint, never an additional dual-stack listener.
            if (BinaryPrimitives.ReadUInt16BigEndian(table[(4 + index * rowSize + 20)..]) == 11434)
            { return false; }
        }
        return true;
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
