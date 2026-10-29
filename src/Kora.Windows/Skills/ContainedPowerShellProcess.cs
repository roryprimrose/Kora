using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Skills;

internal sealed partial class ContainedPowerShellProcess : IContainedPowerShellProcess
{
    private readonly BundledWorkerProfile profile;
    private readonly string bootstrap;
    private AnonymousPipeServerStream? input;
    private AnonymousPipeServerStream? output;
    private AnonymousPipeServerStream? error;
    private FileStream? image;
    private SafeFileHandle? job;
    private SafeProcessHandle? suspendedProcess;
    private Process? process;
    private bool assignedToJob;
    private int started;
    private bool disposed;

    private ContainedPowerShellProcess(BundledWorkerProfile profile, string bootstrap)
    { this.profile = profile; this.bootstrap = bootstrap; }

    public async Task StartAsync(Func<bool> current, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Interlocked.Exchange(ref started, 1) != 0 || !Path.IsPathFullyQualified(profile.Executable))
        { throw new InvalidOperationException("The exact contained process cannot be started again or from a relative image."); }
        cancellationToken.ThrowIfCancellationRequested();
        if (!current()) { throw new InvalidOperationException("Worker source authority retired before image admission."); }
        image = new(profile.Executable, new FileStreamOptions
        { Mode = FileMode.Open, Access = FileAccess.Read, Share = FileShare.Read, Options = FileOptions.Asynchronous });
        var digest = await SHA256.HashDataAsync(image, cancellationToken).ConfigureAwait(false);
        if (!current() || !string.Equals(Convert.ToHexStringLower(digest), profile.ExecutableDigest, StringComparison.Ordinal))
        { throw new InvalidDataException("The qualified protected PowerShell image or source no longer matches."); }
        input = new(PipeDirection.Out, HandleInheritability.Inheritable);
        output = new(PipeDirection.In, HandleInheritability.Inheritable);
        error = new(PipeDirection.In, HandleInheritability.Inheritable);
        job = CreateJobObject(0, null);
        if (job.IsInvalid) { throw Failure(); }
        var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
        if (!SetInformationJobObject(job, 9, in limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
        { throw Failure(); }
        cancellationToken.ThrowIfCancellationRequested();
        if (!current()) { throw new InvalidOperationException("Worker source authority retired before process creation."); }
        await Task.Run(() => Start(profile, bootstrap, job, input.ClientSafePipeHandle,
            output.ClientSafePipeHandle, error.ClientSafePipeHandle, current, cancellationToken), cancellationToken).ConfigureAwait(false);
        input.DisposeLocalCopyOfClientHandle();
        output.DisposeLocalCopyOfClientHandle();
        error.DisposeLocalCopyOfClientHandle();
    }

    internal static IContainedPowerShellProcess Create(BundledWorkerProfile profile, string bootstrap) => new ContainedPowerShellProcess(profile, bootstrap);
    public Stream Input => input ?? throw new InvalidOperationException("The worker transport has not started.");
    public Stream Output => output ?? throw new InvalidOperationException("The worker transport has not started.");
    public Stream Error => error ?? throw new InvalidOperationException("The worker transport has not started.");

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (job is { IsInvalid: false } && !TerminateJobObject(job, 1)) { throw Failure(); }
        if (!assignedToJob && suspendedProcess is { IsInvalid: false }
            && !TerminateProcess(suspendedProcess, 1) && WaitForSingleObject(suspendedProcess, 0) != 0)
        { throw Failure(); }
    }

    public async Task<int> JoinAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (process is not null) { await process.WaitForExitAsync().ConfigureAwait(false); }
        else if (suspendedProcess is not null)
        {
            if (await Task.Run(() => WaitForSingleObject(suspendedProcess, 10000)).ConfigureAwait(false) != 0)
            { throw new IOException("The partially admitted worker did not physically join."); }
        }
        if (job is null || job.IsInvalid) { return 0; }
        // A main-process exit is not proof that descendants or their pipe handles terminated.
        if (!TerminateJobObject(job, 1)) { throw Failure(); }
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            if (!QueryInformationJobObject(job, 1, out var accounting, (uint)Marshal.SizeOf<Accounting>(), 0))
            { throw Failure(); }
            if (accounting.ActiveProcesses == 0) { return process?.ExitCode ?? 0; }
            if (deadline.Elapsed >= TimeSpan.FromSeconds(10))
            { throw new IOException("The contained PowerShell tree could not be confirmed quiescent."); }
            await Task.Delay(TimeSpan.FromMilliseconds(10)).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) { return; }
        try { Stop(); await JoinAsync().ConfigureAwait(false); }
        finally
        {
            disposed = true;
            job?.Dispose();
            suspendedProcess?.Dispose();
            process?.Dispose();
            if (input is not null) { await input.DisposeAsync().ConfigureAwait(false); }
            if (output is not null) { await output.DisposeAsync().ConfigureAwait(false); }
            if (error is not null) { await error.DisposeAsync().ConfigureAwait(false); }
            if (image is not null) { await image.DisposeAsync().ConfigureAwait(false); }
        }
    }

    private unsafe void Start(BundledWorkerProfile profile, string bootstrap, SafeFileHandle job,
        SafePipeHandle input, SafePipeHandle output, SafePipeHandle error, Func<bool> current, CancellationToken cancellationToken)
    {
        if (!ConvertStringSidToSid(profile.AppContainerSid, out var sid)) { throw Failure(); }
        nint list = 0;
        nint environment = 0;
        var initialized = false;
        try
        {
            nuint size = 0;
            _ = InitializeProcThreadAttributeList(0, 2, 0, ref size);
            if (size == 0) { throw Failure(); }
            list = Marshal.AllocHGlobal(checked((int)size));
            if (!InitializeProcThreadAttributeList(list, 2, 0, ref size)) { throw Failure(); }
            initialized = true;
            var capabilities = new SecurityCapabilities { Sid = sid };
            if (!UpdateProcThreadAttribute(list, 0, 0x20009, (nint)(&capabilities),
                (nuint)sizeof(SecurityCapabilities), 0, 0))
            { throw Failure(); }
            nint* handles = stackalloc nint[3] { input.DangerousGetHandle(), output.DangerousGetHandle(), error.DangerousGetHandle() };
            if (!UpdateProcThreadAttribute(list, 0, 0x20002, (nint)handles, (nuint)(3 * sizeof(nint)), 0, 0))
            { throw Failure(); }
            var startup = new Startup
            {
                Size = Marshal.SizeOf<Startup>(),
                Attributes = list,
                Flags = 0x100,
                Input = handles[0],
                Output = handles[1],
                Error = handles[2],
            };
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            environment = Marshal.StringToHGlobalUni("POWERSHELL_TELEMETRY_OPTOUT=1\0POWERSHELL_UPDATECHECK=Off\0"
                + "PSModulePath=\0SystemRoot=" + windows + "\0WINDIR=" + windows + "\0\0");
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(bootstrap));
            var command = ('"' + profile.Executable + "\" -NoLogo -NoProfile -NonInteractive -EncodedCommand " + encoded + '\0').ToCharArray();
            fixed (char* commandLine = command)
            {
                if (!CreateProcess(profile.Executable, commandLine, 0, 0, true,
                    0x00080000 | 0x00000400 | 0x00000004 | 0x08000000, environment,
                    Path.GetDirectoryName(profile.Executable)!, ref startup, out var info))
                { throw Failure(); }
                var processHandle = suspendedProcess = new SafeProcessHandle(info.Process, true);
                using var thread = new SafeFileHandle(info.Thread, true);
                var assigned = false;
                try
                {
                    if (!AssignProcessToJobObject(job, processHandle)) { throw Failure(); }
                    assigned = true;
                    assignedToJob = true;
                    process = Process.GetProcessById(checked((int)info.ProcessId));
                    if (!OpenProcessToken(processHandle, 0x0008, out var token)) { throw Failure(); }
                    using (token)
                    {
                        if (!GetTokenInformation(token, 29, out int contained, sizeof(int), out _) || contained != 1)
                        { throw new InvalidOperationException("The suspended worker is not an AppContainer."); }
                        if (!MatchesContainer(token, sid))
                        { throw new InvalidOperationException("The suspended worker has a different AppContainer identity."); }
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!current()) { throw new InvalidOperationException("Worker authority retired before suspended admission completed."); }
                    if (ResumeThread(thread) == uint.MaxValue) { throw Failure(); }
                }
                catch (Exception exception)
                {
                    var stopped = assigned ? TerminateJobObject(job, 1) : TerminateProcess(processHandle, 1);
                    if (!stopped || WaitForSingleObject(processHandle, 10000) != 0)
                    { throw new IOException("Failed worker admission also has unconfirmed process release.", exception); }
                    throw;
                }
            }
        }
        finally
        {
            if (initialized) { DeleteProcThreadAttributeList(list); }
            if (list != 0) { Marshal.FreeHGlobal(list); }
            if (environment != 0) { Marshal.FreeHGlobal(environment); }
            _ = LocalFree(sid);
        }
    }

    private static Win32Exception Failure() => new(Marshal.GetLastPInvokeError());

    private static bool MatchesContainer(SafeAccessTokenHandle token, nint expected)
    {
        _ = GetTokenData(token, 31, 0, 0, out var required);
        if (required == 0 || required > 4096) { throw Failure(); }
        var buffer = Marshal.AllocHGlobal(checked((int)required));
        try
        {
            if (!GetTokenData(token, 31, buffer, required, out _)) { throw Failure(); }
            return EqualSid(Marshal.ReadIntPtr(buffer), expected);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityCapabilities { internal nint Sid; internal nint Capabilities; internal uint Count; internal uint Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Startup
    {
        internal int Size;
        internal nint Reserved, Desktop, Title;
        internal uint X, Y, Width, Height, CharactersX, CharactersY, Fill, Flags;
        internal ushort Show, ReservedCount;
        internal nint ReservedBytes, Input, Output, Error, Attributes;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { internal nint Process, Thread; internal uint ProcessId, ThreadId; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimits
    {
        internal long ProcessTime, JobTime;
        internal uint Flags;
        internal nuint MinimumWorkingSet, MaximumWorkingSet;
        internal uint ActiveProcessLimit;
        internal nuint Affinity;
        internal uint PriorityClass, SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters { internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimits { internal BasicLimits Basic; internal IoCounters Io; internal nuint ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Accounting
    {
        internal long UserTime, KernelTime, PeriodUserTime, PeriodKernelTime;
        internal uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateJobObject(nint attributes, string? name);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetInformationJobObject(SafeFileHandle job, int kind, in ExtendedLimits limits, uint size);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryInformationJobObject(SafeFileHandle job, int kind, out Accounting accounting, uint size, nint returned);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateJobObject(SafeFileHandle job, uint exitCode);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TerminateProcess(SafeProcessHandle process, uint exitCode);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeProcessHandle process, uint milliseconds);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint ResumeThread(SafeFileHandle thread);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(nint list, int count, uint flags, ref nuint size);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateProcThreadAttribute(nint list, uint flags, nuint attribute, nint value, nuint size, nint previous, nint returned);
    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(nint list);
    [LibraryImport("advapi32.dll", EntryPoint = "ConvertStringSidToSidW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ConvertStringSidToSid(string text, out nint sid);
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(SafeAccessTokenHandle token, int kind, out int value, uint size, out uint required);
    [LibraryImport("advapi32.dll", EntryPoint = "GetTokenInformation", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenData(SafeAccessTokenHandle token, int kind, nint value, uint size, out uint required);
    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EqualSid(nint left, nint right);
    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateProcess(string application, char* commandLine, nint processAttributes,
        nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment,
        string directory, ref Startup startup, out ProcessInfo process);
}