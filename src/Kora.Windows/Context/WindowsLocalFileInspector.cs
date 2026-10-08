using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Windows.Storage;
using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Context;

public sealed partial class WindowsLocalFileInspector(IApplicationDataPaths paths) : ILocalFileInspector
{
    private readonly string[] protectedRoots =
    [
        paths.LocalRoot, paths.RoamingRoot, AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    ];

    public Task<ILocalFileSelection> InspectAsync(string selectedPath, CancellationToken cancellationToken)
    {
        // Metadata opens hold every ancestor against rename/delete until confirmation or revocation.
        return Task.Run<ILocalFileSelection>(() => Inspect(selectedPath, cancellationToken), cancellationToken);
    }

    private Selection Inspect(string path, CancellationToken token)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
        activity.Complete(HostOperationOutcome.Failed);
        LocalFilePolicy.ValidatePath(path);
        if (protectedRoots.Any(root => string.IsNullOrWhiteSpace(root) || LocalFilePolicy.IsWithin(path, root))
            || GetDriveType(path[..3]) != 3)
        {
            throw new InvalidDataException("Protected storage and non-fixed drives are unavailable for local inspection.");
        }
        var handles = new List<SafeFileHandle>();
        try
        {
            var components = path[3..].Split('\\');
            var current = path[..3];
            for (var index = -1; index < components.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                if (index >= 0) { current = current.TrimEnd('\\') + "\\" + components[index]; }
                var file = index == components.Length - 1;
                var handle = CreateFile(current, file ? 0x80000000u : 0x80u,
                    file ? 1u : 3u, nint.Zero, 3, file ? 0x40200000u : 0x02200000u, nint.Zero);
                handles.Add(handle);
                if (handle.IsInvalid) { throw new IOException("The selected source could not be opened with stable sharing."); }
                var info = ReadInfo(handle);
                var forbidden = FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.Offline
                    | FileAttributes.Encrypted;
                if (index >= 0) { forbidden |= FileAttributes.Hidden | FileAttributes.System; }
                if ((info.Attributes & forbidden) != (FileAttributes)0 || file == info.Attributes.HasFlag(FileAttributes.Directory)
                    || !string.Equals(FinalPath(handle).TrimEnd('\\'), current.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    || (file && info.Links != 1))
                {
                    throw new InvalidDataException("Reparse, hidden/system, escaped, hard-linked or noncanonical sources are not admitted.");
                }
            }
            token.ThrowIfCancellationRequested();
            var selected = new Selection(handles, path);
            if (selected.Metadata.ByteLength > LocalFilePolicy.MaximumBytes)
            {
                throw new InvalidDataException("The selected file exceeds 256 KiB.");
            }
            handles = [];
            activity.Complete(HostOperationOutcome.Completed);
            return selected;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        finally
        {
            foreach (var handle in handles) { handle.Dispose(); }
        }
    }

    private sealed class Selection : ILocalFileSelection
    {
        private readonly List<SafeFileHandle> handles;
        private readonly SafeFileHandle file;
        private int disposed;
        private int readStarted;
        internal Selection(List<SafeFileHandle> handles, string path)
        {
            this.handles = handles;
            file = handles[^1];
            Metadata = Observe(file, path);
        }
        public LocalFileMetadata Metadata { get; }

        public async Task<byte[]> ReadAsync(CancellationToken cancellationToken)
        {
            using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
            activity.Complete(HostOperationOutcome.Failed);
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            if (Interlocked.Exchange(ref readStarted, 1) != 0)
            {
                throw new InvalidOperationException("A reviewed file can be read only once.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (Observe(file, Metadata.CanonicalPath) != Metadata)
            {
                throw new InvalidDataException("The reviewed source identity changed before confirmation.");
            }
            var bytes = new byte[checked((int)Metadata.ByteLength)];
            var verification = new byte[4096];
            var extra = new byte[1];
            try
            {
                await using var stream = new FileStream(file, FileAccess.Read, 4096, isAsync: true);
                await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
                // Sharing locks prevent normal writers/replacement; also reject differing bytes
                // from previously established mappings or other filesystem mechanisms.
                stream.Position = 0;
                for (var offset = 0; offset < bytes.Length; offset += verification.Length)
                {
                    var count = Math.Min(verification.Length, bytes.Length - offset);
                    await stream.ReadExactlyAsync(verification.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    if (!verification.AsSpan(0, count).SequenceEqual(bytes.AsSpan(offset, count)))
                    {
                        throw new InvalidDataException("The source bytes changed during capture; no mixed revision was admitted.");
                    }
                }
                if (await stream.ReadAsync(extra, cancellationToken).ConfigureAwait(false) != 0
                    || Observe(file, Metadata.CanonicalPath) != Metadata)
                {
                    throw new InvalidDataException("The source changed during read; no mixed revision was admitted.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                activity.Complete(HostOperationOutcome.Completed);
                return bytes;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(bytes);
                if (cancellationToken.IsCancellationRequested) { activity.Complete(HostOperationOutcome.Cancelled); }
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(verification);
                CryptographicOperations.ZeroMemory(extra);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) { return; }
            for (var index = handles.Count - 1; index >= 0; index--) { handles[index].Dispose(); }
        }
    }

    private static LocalFileMetadata Observe(SafeFileHandle handle, string path)
    {
        var info = ReadInfo(handle);
        if (info.Links != 1 || info.Attributes.HasFlag(FileAttributes.ReparsePoint)
            || !string.Equals(FinalPath(handle), path, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The source identity is no longer canonical.");
        }
        var length = checked((long)(((ulong)info.SizeHigh << 32) | info.SizeLow));
        var lastWrite = DateTimeOffset.FromFileTime(checked((long)(((ulong)info.WriteHigh << 32) | info.WriteLow)));
        try { return new(path, WindowsFileIdentity.Read(handle), length, lastWrite); }
        catch (Win32Exception exception) { throw new IOException("The native source identity is unavailable.", exception); }
    }

    private static unsafe HandleInfo ReadInfo(SafeFileHandle handle)
    {
        HandleInfo info;
        return GetFileInformationByHandle(handle, &info) ? info
            : throw new IOException("Native file metadata could not be verified.");
    }

    private static unsafe string FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        uint length;
        fixed (char* pointer = buffer)
        {
            length = GetFinalPathNameByHandle(handle, pointer, (uint)buffer.Length, 0);
        }
        if (length == 0 || length >= buffer.Length) { throw new IOException("The final source path is unavailable."); }
        var path = new string(buffer, 0, (int)length);
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..]
            : throw new InvalidDataException("The final source is not a canonical fixed-drive file.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInfo
    {
        internal FileAttributes Attributes;
        internal uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint sharing,
        nint security, uint creation, uint flags, nint template);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string root);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetFileInformationByHandle(SafeFileHandle handle, HandleInfo* information);

    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle handle, char* path, uint length, uint flags);
}
