using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace ContainmentProof;

internal static class Native
{
    internal const uint Suspended = 4;
    internal const uint Breakaway = 0x01000000;
    internal const uint NoWindow = 0x08000000;
    private const uint Extended = 0x00080000;
    private const uint UnicodeEnvironment = 0x400;

    [StructLayout(LayoutKind.Sequential)]
    internal struct ProcessInfo
    {
        internal IntPtr Process, Thread;
        internal uint Pid, Tid;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Startup
    {
        internal int Size;
        internal IntPtr Reserved, Desktop, Title;
        internal int X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        internal short Show, ReservedSize;
        internal IntPtr ReservedBytes, In, Out, Error;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupEx
    {
        internal Startup Startup;
        internal IntPtr Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities
    {
        internal IntPtr Sid, Capabilities;
        internal uint Count, Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        internal long ProcessTime, JobTime;
        internal uint Flags;
        internal UIntPtr MinWorkingSet, MaxWorkingSet;
        internal uint ActiveProcessLimit;
        internal UIntPtr Affinity;
        internal uint Priority, Scheduling;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits
    {
        internal BasicLimits Basic;
        internal IoCounters Io;
        internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        internal uint Flags, Type;
        internal string Target;
        internal string? Comment;
        internal long LastWritten;
        internal uint Size;
        internal IntPtr Blob;
        internal uint Persist, AttributeCount;
        internal IntPtr Attributes;
        internal string? Alias, User;
    }

    internal static IntPtr CreateContainer(string name)
    {
        int hr = CreateAppContainerProfile(name, name, "Kora synthetic R02 proof", IntPtr.Zero, 0, out var sid);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
        return sid;
    }

    internal static void DeleteContainer(string name)
    {
        int hr = DeleteAppContainerProfile(name);
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    internal static string SidString(IntPtr sid) => new SecurityIdentifier(sid).Value;
    internal static void ReleaseSid(IntPtr sid) => FreeSid(sid);

    internal static IntPtr Job()
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        Check(job != IntPtr.Zero);
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
        if (!SetInformationJobObject(job, 9, ref limits, Marshal.SizeOf<ExtendedLimits>()))
        {
            int error = Marshal.GetLastWin32Error();
            Close(job);
            throw new Win32Exception(error);
        }
        return job;
    }

    internal static ProcessInfo Launch(string exe, string arguments, string directory,
        IntPtr sid, IReadOnlyDictionary<string, string> environment, IntPtr job)
    {
        var startup = new StartupEx();
        IntPtr capabilities = IntPtr.Zero;
        IntPtr env = IntPtr.Zero;
        try
        {
            uint flags = Suspended | NoWindow | UnicodeEnvironment;
            startup.Startup.Size = Marshal.SizeOf<Startup>();
            if (sid != IntPtr.Zero)
            {
                nuint bytes = 0;
                _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref bytes);
                startup.Attributes = Marshal.AllocHGlobal(checked((int)bytes));
                Check(InitializeProcThreadAttributeList(startup.Attributes, 1, 0, ref bytes));
                capabilities = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityCapabilities>());
                Marshal.StructureToPtr(new SecurityCapabilities { Sid = sid }, capabilities, false);
                Check(UpdateProcThreadAttribute(startup.Attributes, 0, (IntPtr)0x00020009,
                    capabilities, (nuint)Marshal.SizeOf<SecurityCapabilities>(), IntPtr.Zero, IntPtr.Zero));
                startup.Startup.Size = Marshal.SizeOf<StartupEx>();
                flags |= Extended;
            }
            string block = string.Join('\0', environment.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Select(p => $"{p.Key}={p.Value}")) + "\0\0";
            env = Marshal.StringToHGlobalUni(block);
            Check(CreateProcessW(exe, new StringBuilder($"\"{exe}\" {arguments}"),
                IntPtr.Zero, IntPtr.Zero, false, flags, env, directory, ref startup, out var process));
            try
            {
                Check(AssignProcessToJobObject(job, process.Process));
                Check(ResumeThread(process.Thread) != uint.MaxValue);
                return process;
            }
            catch
            {
                Check(TerminateProcess(process.Process, 125));
                _ = WaitForSingleObject(process.Process, 5000);
                Close(process.Thread);
                Close(process.Process);
                throw;
            }
        }
        finally
        {
            if (startup.Attributes != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(startup.Attributes);
                Marshal.FreeHGlobal(startup.Attributes);
            }
            if (capabilities != IntPtr.Zero) Marshal.FreeHGlobal(capabilities);
            if (env != IntPtr.Zero) Marshal.FreeHGlobal(env);
        }
    }

    internal static Probe StartChild(string exe, string arguments, bool breakaway, out int pid)
    {
        var startup = new StartupEx { Startup = new Startup { Size = Marshal.SizeOf<Startup>() } };
        bool ok = CreateProcessW(exe, new StringBuilder($"\"{exe}\" {arguments}"),
            IntPtr.Zero, IntPtr.Zero, false, NoWindow | (breakaway ? Breakaway | Suspended : 0),
            IntPtr.Zero, null, ref startup, out var child);
        int error = Marshal.GetLastWin32Error();
        pid = ok ? checked((int)child.Pid) : 0;
        if (ok)
        {
            if (breakaway)
            {
                // A surprising breakaway must not leave an untracked running process.
                Check(TerminateProcess(child.Process, 125));
                _ = WaitForSingleObject(child.Process, 5000);
                pid = 0;
            }
            Close(child.Thread);
            Close(child.Process);
        }
        return new(breakaway ? "child.breakaway" : "child.normal",
            ok ? "Allowed" : error == 5 ? "Denied" : "Unknown", ok ? null : error);
    }

    internal static List<int> JobPids(IntPtr job)
    {
        IntPtr buffer = Marshal.AllocHGlobal(4096);
        try
        {
            Check(QueryInformationJobObject(job, 3, buffer, 4096, out _));
            int count = Marshal.ReadInt32(buffer, 4);
            if (count > (4096 - 8) / IntPtr.Size) throw new InvalidDataException("Job PID buffer exceeded");
            return Enumerable.Range(0, count)
                .Select(i => checked((int)Marshal.ReadIntPtr(buffer, 8 + i * IntPtr.Size))).ToList();
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static bool Exited(IntPtr process) => WaitForSingleObject(process, 0) switch
    {
        0 => true,
        258 => false,
        _ => throw new Win32Exception(Marshal.GetLastWin32Error()),
    };
    internal static int ExitCode(IntPtr process)
    {
        Check(GetExitCodeProcess(process, out uint code));
        return unchecked((int)code);
    }
    internal static void Close(IntPtr handle) { if (handle != IntPtr.Zero) Check(CloseHandle(handle)); }

    internal static TokenFacts Token()
    {
        Check(OpenProcessToken(GetCurrentProcess(), 8, out var token));
        try
        {
            bool container = TokenInt(token, 29) != 0;
            string? sid = null;
            if (container)
            {
                _ = GetTokenInformation(token, 31, IntPtr.Zero, 0, out int length);
                if (length <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                IntPtr buffer = Marshal.AllocHGlobal(length);
                try
                {
                    Check(GetTokenInformation(token, 31, buffer, length, out _));
                    sid = SidString(Marshal.ReadIntPtr(buffer));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            using var identity = WindowsIdentity.GetCurrent();
            _ = GetTokenInformation(token, 25, IntPtr.Zero, 0, out int integrityLength);
            if (integrityLength <= 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr integrity = Marshal.AllocHGlobal(integrityLength);
            try
            {
                Check(GetTokenInformation(token, 25, integrity, integrityLength, out _));
                return new(container, identity.User?.Value ?? throw new InvalidDataException("No user SID"),
                    sid, TokenInt(token, 20) != 0, SidString(Marshal.ReadIntPtr(integrity)));
            }
            finally { Marshal.FreeHGlobal(integrity); }
        }
        finally { Close(token); }
    }

    private static int TokenInt(IntPtr token, int information)
    {
        IntPtr buffer = Marshal.AllocHGlobal(4);
        try
        {
            Check(GetTokenInformation(token, information, buffer, 4, out _));
            return Marshal.ReadInt32(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    internal static void CreateCredential(string target)
    {
        byte[] bytes = Encoding.Unicode.GetBytes("Kora-R02-SYNTHETIC-NOT-A-SECRET");
        IntPtr blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new Credential { Type = 1, Target = target, Size = (uint)bytes.Length,
                Blob = blob, Persist = 1, User = "synthetic" };
            Check(CredWriteW(ref credential, 0));
        }
        finally { Marshal.FreeHGlobal(blob); }
    }

    internal static Probe ReadCredential(string target)
    {
        if (!CredReadW(target, 1, 0, out var credential))
        {
            int error = Marshal.GetLastWin32Error();
            // Absence in this token's credential namespace is isolation evidence, not ACCESS_DENIED.
            return new("credential.synthetic", error is 5 or 1312 ? "Denied" :
                error == 1168 ? "Isolated" : "Unknown", error);
        }
        try { return new("credential.synthetic", "Allowed"); }
        finally { CredFree(credential); }
    }

    internal static void DeleteCredential(string target) => Check(CredDeleteW(target, 1, 0));
    internal static Probe OpenDesktop()
    {
        var desktop = OpenInputDesktop(0, false, 0x0100);
        if (desktop == IntPtr.Zero)
        {
            int error = Marshal.GetLastWin32Error();
            return new("desktop.input.open", error == 5 ? "Denied" : "Unknown", error);
        }
        Check(CloseDesktop(desktop));
        return new("desktop.input.open", "Allowed", Detail: "Handle only; no desktop switch or computer control");
    }
    internal static void HardLink(string alias, string target) =>
        Check(CreateHardLinkW(alias, target, IntPtr.Zero));
    internal static Probe Rename(string source, string destination)
    {
        if (MoveFileExW(source, destination, 0)) return new("filesystem.protected.rename", "Allowed");
        int error = Marshal.GetLastWin32Error();
        return new("filesystem.protected.rename", error == 5 ? "Denied" : "Unknown", error);
    }
    private static void Check(bool success)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int CreateAppContainerProfile(string name, string display, string description,
        IntPtr capabilities, uint count, out IntPtr sid);
    [DllImport("userenv.dll", CharSet = CharSet.Unicode)]
    private static extern int DeleteAppContainerProfile(string name);
    [DllImport("advapi32.dll")]
    private static extern IntPtr FreeSid(IntPtr sid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObjectW(IntPtr attributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr job, int information,
        ref ExtendedLimits limits, int length);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(IntPtr job, int information, IntPtr buffer,
        int length, out int returned);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, IntPtr attribute,
        IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes,
        IntPtr threadAttributes, bool inherit, uint flags, IntPtr environment, string? directory,
        ref StartupEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr process, uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr token, int information, IntPtr buffer, int length,
        out int returned);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref Credential credential, uint flags);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, uint type, uint flags, out IntPtr credential);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, uint type, uint flags);
    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr credential);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLinkW(string file, string existing, IntPtr attributes);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(string existing, string destination, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr desktop);
}
