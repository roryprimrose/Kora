using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Security.Cryptography;
using System.Text;
using Kora.Core.Dependencies;
using Kora.Core.Mcp;
using Kora.Windows.Storage;
using Microsoft.Win32.SafeHandles;
using McpProcessAuthorization = Kora.Core.Mcp.McpConnectionIdentity.McpProcessAuthorization;
using IMcpProcessPreparation = Kora.Core.Mcp.IMcpConnectionBroker.IMcpProcessPreparation;
using McpProcessFailure = Kora.Windows.Mcp.IWindowsMcpProcessLease.McpProcessFailure;
using McpProcessException = Kora.Windows.Mcp.IWindowsMcpProcessLease.McpProcessException;

namespace Kora.Windows.Mcp;

internal sealed partial class WindowsMcpProcessLease(Process process, List<SafeFileHandle> handles) : IWindowsMcpProcessLease
{
    private bool released;
    public Stream Input => process.StandardInput.BaseStream;
    public Stream Output => process.StandardOutput.BaseStream;
    public Stream Error => process.StandardError.BaseStream;
    public bool IsOwned => !released && !process.SafeHandle.IsClosed && !process.SafeHandle.IsInvalid;
    public bool HasExited => IsOwned && process.HasExited;
    public Task WaitForExitAsync(CancellationToken token) => process.WaitForExitAsync(token);
    public Task CloseInputAsync() => Input.DisposeAsync().AsTask();

    public Task TerminateRootAsync()
    {
        if (!IsOwned) { throw new McpProcessException(McpProcessFailure.ShutdownUnconfirmed); }
        try { if (!process.HasExited) { process.Kill(entireProcessTree: false); } }
        catch (Win32Exception) { throw new McpProcessException(McpProcessFailure.ShutdownUnconfirmed); }
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (released) { return; }
        if (!HasExited) { throw new McpProcessException(McpProcessFailure.ShutdownUnconfirmed); }
        await Input.DisposeAsync().ConfigureAwait(false);
        await Output.DisposeAsync().ConfigureAwait(false);
        await Error.DisposeAsync().ConfigureAwait(false);
        process.Dispose();
        foreach (var handle in handles) { handle.Dispose(); }
        released = true;
    }

    internal static async Task<IMcpProcessPreparation> PrepareAsync(McpConnectionIdentity identity,
        IApplicationDataPaths paths, TimeProvider time, CancellationToken token, Action<IMcpProcessPreparation>? released = null)
    {
        if (identity.Stdio is not { } profile) { throw new InvalidDataException("An exact stdio profile is required."); }
        var handles = new List<SafeFileHandle>();
        try
        {
            OpenPath(profile.Executable, true, handles);
            OpenPath(profile.WorkingDirectory, false, handles);
            var executable = handles.First(handle => !IsDirectory(handle));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long offset = 0;
            int count;
            while ((count = await RandomAccess.ReadAsync(executable, buffer, offset, token).ConfigureAwait(false)) != 0)
            {
                token.ThrowIfCancellationRequested();
                hash.AppendData(buffer.AsSpan(0, count));
                offset += count;
            }
            var native = Convert.ToHexStringLower(hash.GetHashAndReset()) + ":"
                + string.Join(':', handles.Select(WindowsFileIdentity.Read));
            return new Preparation(identity, native, handles, paths, time, released);
        }
        catch
        {
            foreach (var handle in handles) { handle.Dispose(); }
            throw;
        }
    }

    private sealed class Preparation(McpConnectionIdentity identity, string nativeIdentity,
        List<SafeFileHandle> handles, IApplicationDataPaths paths, TimeProvider time,
        Action<IMcpProcessPreparation>? released) : IMcpProcessPreparation
    {
        private readonly Lock gate = new();
        private bool consumed;
        private bool disposed;
        private McpStdioConnection? connection;
        public McpConnectionIdentity Identity => identity;
        public string NativeIdentity => nativeIdentity;

        public async Task<IMcpConnection> ConnectAsync(McpProcessAuthorization authorization, CancellationToken token)
        {
            lock (gate)
            {
                if (consumed || disposed) { throw new InvalidOperationException("The prepared MCP process is single-use."); }
                consumed = true;
            }
            if (authorization.Identity != identity || !string.Equals(authorization.NativeIdentity, nativeIdentity, StringComparison.Ordinal))
            { throw new InvalidOperationException("The exact held native MCP identity lacks launch consent."); }
            await authorization.ValidateLaunchAsync(token).ConfigureAwait(false);
            await authorization.ValidateCleanupAsync(token).ConfigureAwait(false);
            var profile = identity.Stdio!;
            var storage = new RestrictedStorageDirectory(paths, includeKeys: false,
                partitionName: "McpProcess-" + Guid.NewGuid().ToString("N"));
            storage.CreateNew();
            OpenPath(storage.Root, false, handles);
            storage.Verify();
            var info = CreateStartInfo(profile, storage.Root);
            foreach (var handle in handles)
            {
                if (handle.IsClosed || handle.IsInvalid || IsReparse(handle))
                { throw new InvalidDataException("The held native MCP launch identity changed."); }
            }
            await authorization.ValidateLaunchAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            var process = new Process { StartInfo = info };
            try
            {
                if (!process.Start()) { throw new InvalidOperationException("Windows did not acknowledge the exact MCP process start."); }
            }
            catch (Win32Exception exception)
            {
                process.Dispose();
                throw new IOException("Windows could not start the reviewed MCP executable.", exception);
            }
            var lease = new WindowsMcpProcessLease(process, handles);
            connection = new(identity, lease, authorization, time);
            return connection;
        }

        public async ValueTask DisposeAsync()
        {
            lock (gate) { if (disposed) { return; } }
            if (connection is not null) { await connection.DisposeAsync().ConfigureAwait(false); }
            else
            {
                lock (gate)
                {
                    if (disposed) { return; }
                    disposed = true;
                    foreach (var handle in handles) { handle.Dispose(); }
                }
            }
            lock (gate) { disposed = true; }
            released?.Invoke(this);
        }
    }

    internal static ProcessStartInfo CreateStartInfo(McpStdioEndpoint profile, string temporaryDirectory)
    {
        var info = new ProcessStartInfo(profile.Executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = profile.WorkingDirectory,
        };
        foreach (var argument in profile.Arguments) { info.ArgumentList.Add(argument); }
        info.Environment.Clear();
        foreach (var item in profile.Environment) { info.Environment.Add(item.Key, item.Value); }
        info.Environment.Add("TEMP", temporaryDirectory);
        info.Environment.Add("TMP", temporaryDirectory);
        return info;
    }

    private static void OpenPath(string path, bool file, List<SafeFileHandle> handles)
    {
        if (GetDriveType(path[..3]) != 3) { throw new InvalidDataException("MCP launch requires a local fixed drive."); }
        var current = path[..3];
        var parts = path[3..].Split('\\');
        for (var index = -1; index < parts.Length; index++)
        {
            if (index >= 0) { current = Path.Combine(current, parts[index]); }
            var leafFile = file && index == parts.Length - 1;
            var handle = CreateFile(current, 0x80000000u,
                leafFile ? 1u : 3u, nint.Zero, 3, 0x02200000u | (leafFile ? 0x40000000u : 0), nint.Zero);
            handles.Add(handle);
            if (handle.IsInvalid || IsReparse(handle) || IsDirectory(handle) == leafFile)
            { throw new InvalidDataException("The MCP executable/directory identity is unavailable or traverses a reparse point."); }
            var name = new ushort[32768];
            var length = GetFinalPathNameByHandle(handle, name, (uint)name.Length, 0);
            if (length == 0 || length >= name.Length
                || !string.Equals(new string(name.Take((int)length).Select(value => (char)value).ToArray()).Replace(@"\\?\", "", StringComparison.Ordinal).TrimEnd('\\'),
                    current.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            { throw new InvalidDataException("The held MCP launch path is not the exact canonical local path."); }
        }
    }

    private static bool IsReparse(SafeFileHandle handle) => Attributes(handle).HasFlag(FileAttributes.ReparsePoint);
    private static bool IsDirectory(SafeFileHandle handle) => Attributes(handle).HasFlag(FileAttributes.Directory);
    private static FileAttributes Attributes(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandleEx(handle, 9, out var value, Marshal.SizeOf<AttributeTag>()))
        { throw new IOException("The held MCP native file attributes are unavailable."); }
        return (FileAttributes)value.Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AttributeTag { internal uint Attributes; internal uint Tag; }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);
    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string root);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static partial uint GetFinalPathNameByHandle(SafeFileHandle handle,
        [Out, MarshalUsing(CountElementName = nameof(length))] ushort[] path, uint length, uint flags);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out AttributeTag value, int size);
}
