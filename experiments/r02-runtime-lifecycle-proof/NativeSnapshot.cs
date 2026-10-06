using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace Kora.Rt2;

internal sealed record ProcessRow(int Id, int ParentId);
internal sealed record SocketRow(int ProcessId, string Protocol, string LocalAddress, int LocalPort,
    string? RemoteAddress, int? RemotePort, uint State);

internal static class NativeSnapshot
{
    internal static IReadOnlyList<ProcessRow> Processes()
    {
        using var snapshot = NativeMethods.CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var entry = new NativeMethods.ProcessEntry { Size = (uint)Marshal.SizeOf<NativeMethods.ProcessEntry>() };
        var rows = new List<ProcessRow>();
        if (!NativeMethods.Process32FirstW(snapshot, ref entry))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        do { rows.Add(new ProcessRow(checked((int)entry.Id), checked((int)entry.ParentId))); }
        while (NativeMethods.Process32NextW(snapshot, ref entry));
        var error = Marshal.GetLastWin32Error();
        if (error != 18) throw new Win32Exception(error);
        return rows;
    }

    internal static IReadOnlyList<SocketRow> Sockets()
    {
        var rows = new List<SocketRow>();
        foreach (var family in new[] { 2u, 23u })
        {
            rows.AddRange(ParseTable(Table(family, false), family == 23, false));
            rows.AddRange(ParseTable(Table(family, true), family == 23, true));
        }
        return rows;
    }

    private static byte[] Table(uint family, bool udp)
    {
        uint size = 0;
        uint Read(nint buffer, ref uint length) => udp
            ? NativeMethods.GetExtendedUdpTable(buffer, ref length, false, family, 1, 0)
            : NativeMethods.GetExtendedTcpTable(buffer, ref length, false, family, 5, 0);
        var result = Read(0, ref size);
        if (result != 122 && result != 0) throw new Win32Exception(checked((int)result));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (size is < 4 or > 16_777_216) throw new InvalidDataException("Unbounded IP Helper table.");
            var buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                var capacity = size;
                result = Read(buffer, ref size);
                if (result == 122) continue;
                if (result != 0) throw new Win32Exception(checked((int)result));
                if (size > capacity) throw new InvalidDataException("IP Helper exceeded its buffer.");
                var bytes = new byte[size];
                Marshal.Copy(buffer, bytes, 0, bytes.Length);
                return bytes;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new IOException("IP Helper table changed during all bounded reads.");
    }

    internal static IReadOnlyList<SocketRow> ParseTable(ReadOnlySpan<byte> table, bool ipv6, bool udp)
    {
        var width = udp ? (ipv6 ? 28 : 12) : (ipv6 ? 56 : 24);
        if (table.Length < 4) throw new InvalidDataException("Missing socket table header.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(table);
        if (count > (table.Length - 4) / width) throw new InvalidDataException("Truncated socket table.");
        var rows = new List<SocketRow>();
        for (var index = 0; index < count; index++)
        {
            var row = table.Slice(checked(4 + (int)index * width), width);
            var localOffset = ipv6 || udp ? 0 : 4;
            var portOffset = ipv6 ? 20 : (udp ? 4 : 8);
            var local = new System.Net.IPAddress(row.Slice(localOffset, ipv6 ? 16 : 4)).ToString();
            var localPort = BinaryPrimitives.ReadUInt16BigEndian(row.Slice(portOffset, 2));
            var pidOffset = udp ? (ipv6 ? 24 : 8) : (ipv6 ? 52 : 20);
            var pid = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(row[pidOffset..]));
            var state = udp ? 0 : BinaryPrimitives.ReadUInt32LittleEndian(row[(ipv6 ? 48 : 0)..]);
            var remote = udp ? null : new System.Net.IPAddress(row.Slice(ipv6 ? 24 : 12, ipv6 ? 16 : 4)).ToString();
            int? remotePort = udp ? null : BinaryPrimitives.ReadUInt16BigEndian(row.Slice(ipv6 ? 44 : 16, 2));
            rows.Add(new SocketRow(pid, udp ? "UDP" : "TCP", local, localPort, remote, remotePort, state));
        }
        return rows;
    }
}

// Toolhelp's fixed character array and SafeHandle marshalling use the reviewed
// Windows ABI; these imports resolve only from System32.
#pragma warning disable SYSLIB1054
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ProcessEntry
    {
        internal uint Size;
        internal uint Usage;
        internal uint Id;
        internal nuint Heap;
        internal uint Module;
        internal uint Threads;
        internal uint ParentId;
        internal int Priority;
        internal uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string Name;
    }

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    internal static extern SnapshotHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32FirstW(SnapshotHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool Process32NextW(SnapshotHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    internal static extern uint GetExtendedTcpTable(nint table, ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order, uint family, uint tableClass, uint reserved);
    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    internal static extern uint GetExtendedUdpTable(nint table, ref uint size,
        [MarshalAs(UnmanagedType.Bool)] bool order, uint family, uint tableClass, uint reserved);
}
#pragma warning restore SYSLIB1054

internal sealed class SnapshotHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SnapshotHandle() : base(true) { }
    protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
}
