using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Kora.Rt1;

namespace Kora.Mg1;

internal sealed partial class OwnedRuntimePause : IDisposable
{
    private readonly SafeProcessHandle handle;
    private bool resumed;
    private OwnedRuntimePause(SafeProcessHandle handle) => this.handle = handle;

    internal static unsafe OwnedRuntimePause SuspendOnlyOwnedChild()
    {
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
        var entry = new ProcessEntry { Size = (uint)sizeof(ProcessEntry) };
        var matches = new List<SafeProcessHandle>();
        try
        {
            if (!Process32FirstW(snapshot, ref entry)) throw new Win32Exception(Marshal.GetLastPInvokeError());
            do
            {
                if (entry.ParentId != Environment.ProcessId) continue;
                var process = OpenProcess(0x1800, false, entry.ProcessId);
                if (process.IsInvalid) { process.Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
                var path = new char[32768];
                var length = (uint)path.Length;
                fixed (char* buffer = path)
                {
                    if (!QueryFullProcessImageNameW(process, 0, buffer, ref length))
                    { process.Dispose(); throw new Win32Exception(Marshal.GetLastPInvokeError()); }
                }
                if (string.Equals(new string(path, 0, (int)length), Candidate.Executable, StringComparison.OrdinalIgnoreCase))
                    matches.Add(process);
                else process.Dispose();
            } while (Process32NextW(snapshot, ref entry));
            if (Marshal.GetLastPInvokeError() != 18) throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (matches.Count != 1) throw new InvalidOperationException("Runtime suspension requires exactly one verified direct owned child.");
            var target = matches[0];
            var status = NtSuspendProcess(target);
            if (status != 0) throw new InvalidOperationException("Owned runtime suspension failed: " + status);
            matches.Clear();
            return new OwnedRuntimePause(target);
        }
        finally { foreach (var process in matches) process.Dispose(); }
    }
    public void Dispose()
    {
        if (resumed) return;
        var status = NtResumeProcess(handle);
        resumed = true;
        handle.Dispose();
        if (status != 0) throw new InvalidOperationException("Owned runtime resume failed: " + status);
    }
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ProcessEntry
    {
        internal uint Size, Usage, ProcessId;
        internal nuint Heap;
        internal uint ModuleId, Threads, ParentId;
        internal int Priority;
        internal uint Flags;
        internal fixed char Executable[260];
    }
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint pid);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32FirstW(SafeFileHandle snapshot, ref ProcessEntry entry);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32NextW(SafeFileHandle snapshot, ref ProcessEntry entry);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageNameW(SafeProcessHandle process, uint flags, char* name, ref uint size);
    [LibraryImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NtSuspendProcess(SafeProcessHandle process);
    [LibraryImport("ntdll.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int NtResumeProcess(SafeProcessHandle process);
}
