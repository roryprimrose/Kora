using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Presentation;
using Kora.Windows.Storage;
using Microsoft.Win32.SafeHandles;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.Presentation;

public sealed partial class WindowsExactAnswerExportWriter(IApplicationDataPaths paths,
    ILogger<WindowsExactAnswerExportWriter> logger,
    IExactAnswerExportCheckpoint? checkpoint = null) : IExactAnswerExportWriter
{
    private readonly IExactAnswerExportCheckpoint? checkpoint = checkpoint;
    private readonly string[] protectedRoots =
    [
        paths.LocalRoot, paths.RoamingRoot, AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    ];

    public Task<IExactAnswerExportDestination> SelectAsync(string path, CancellationToken token) =>
        Task.Run<IExactAnswerExportDestination>(() => Select(path, token), token);

    private Destination Select(string path, CancellationToken token)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
        activity.Complete(HostOperationOutcome.Failed);
        LocalFilePolicy.ValidatePath(path);
        if (protectedRoots.Any(root => string.IsNullOrWhiteSpace(root) || LocalFilePolicy.IsWithin(path, root))
            || GetDriveType(path[..3]) != 3)
        { throw new InvalidDataException("Export requires a new file on an admitted local fixed drive outside protected storage."); }
        var handles = new List<SafeFileHandle>();
        try
        {
            var parent = System.IO.Path.GetDirectoryName(path)!;
            var current = path[..3];
            var components = parent.Length == 3 ? [] : parent[3..].Split('\\');
            for (var index = -1; index < components.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                if (index >= 0) { current = current.TrimEnd('\\') + "\\" + components[index]; }
                // Deny delete sharing on every ancestor, retaining its exact identity through publication.
                var handle = CreateFile(current, 0x81, 3, nint.Zero, 3, 0x02200000, nint.Zero);
                handles.Add(handle);
                if (handle.IsInvalid) { throw new IOException("Export destination ancestry could not be held."); }
                ValidateDirectory(handle, current, index < 0);
            }
            RequireAbsent(path);
            var selected = new Destination(this, path, handles);
            handles = [];
            activity.Complete(HostOperationOutcome.Completed);
            return selected;
        }
        finally
        {
            foreach (var handle in handles) { handle.Dispose(); }
        }
    }

    private static void RequireAbsent(string path)
    {
        var attributes = GetFileAttributes(path);
        if (attributes != uint.MaxValue || Marshal.GetLastPInvokeError() != 2)
        { throw new IOException("Export is create-only. Choose a different new destination; existing or unavailable targets are refused."); }
    }

    private static void ValidateDirectory(SafeFileHandle handle, string path, bool driveRoot)
    {
        var info = ReadInfo(handle);
        var forbidden = FileAttributes.ReparsePoint | FileAttributes.Device | FileAttributes.Offline | FileAttributes.Encrypted
            | (driveRoot ? (FileAttributes)0 : FileAttributes.Hidden | FileAttributes.System);
        if (!info.Attributes.HasFlag(FileAttributes.Directory) || (info.Attributes & forbidden) != (FileAttributes)0
            || !string.Equals(FinalPath(handle).TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Reparse, hidden/system, escaped or noncanonical export ancestry is refused."); }
    }

    private sealed class Destination(WindowsExactAnswerExportWriter owner, string path, List<SafeFileHandle> handles) : IExactAnswerExportDestination
    {
        private readonly string parentIdentity = WindowsFileIdentity.Read(handles[^1]);
        private int consumed;
        public string Path => path;

        private void Validate(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var current = path[..3];
            for (var index = 0; index < handles.Count; index++)
            {
                if (index > 0) { current = System.IO.Path.Combine(current, path[3..].Split('\\')[index - 1]); }
                ValidateDirectory(handles[index], current, index == 0);
            }
            if (!string.Equals(parentIdentity, WindowsFileIdentity.Read(handles[^1]), StringComparison.Ordinal))
            { throw new InvalidDataException("The selected destination identity changed."); }
            RequireAbsent(path);
        }

        public Task<ExactAnswerExportOutcome> PublishAsync(AdmittedDetailContent source,
            Func<CancellationToken, Task> revalidate, Func<bool> admitted, CancellationToken token) =>
            Task.Run(() => Publish(source, revalidate, admitted, token), CancellationToken.None);

        private async Task<ExactAnswerExportOutcome> Publish(AdmittedDetailContent source,
            Func<CancellationToken, Task> revalidate, Func<bool> admitted, CancellationToken token)
        {
            using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
            activity.Complete(HostOperationOutcome.Failed);
            if (Interlocked.Exchange(ref consumed, 1) != 0)
            { throw new InvalidOperationException("The selected export destination is single-use."); }
            if (source.Origin != DetailContentOrigin.RetainedAssistant)
            { throw new InvalidOperationException("Only a separately reviewed exact retained ASSISTANT answer can be exported."); }
            var bytes = new UTF8Encoding(false, true).GetBytes(source.Source);
            var verification = new byte[bytes.Length];
            var published = false;
            SafeFileHandle? staged = null;
            var outcome = ExactAnswerExportOutcome.NotWritten;
            try
            {
                Validate(token);
                await revalidate(token).ConfigureAwait(false);
                if (owner.checkpoint is { } before) { await before.ReachedAsync(ExactAnswerExportPhase.BeforeStaging, token).ConfigureAwait(false); }
                Validate(token);
                await revalidate(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!admitted()) { throw new InvalidOperationException("Private export admission changed before staging."); }
                var temporary = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, ".kora-export-" + Guid.NewGuid().ToString("N"));
                staged = CreateFile(temporary, 0xc0010000, 0, nint.Zero, 1, 0x44200080, nint.Zero);
                if (staged.IsInvalid) { throw new IOException("The private export staging file could not be created."); }
                await using var stream = new FileStream(staged, FileAccess.ReadWrite, 4096, isAsync: true);
                await stream.WriteAsync(bytes, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
                if (owner.checkpoint is { } stagedCheckpoint) { await stagedCheckpoint.ReachedAsync(ExactAnswerExportPhase.Staged, token).ConfigureAwait(false); }
                Validate(token);
                await revalidate(token).ConfigureAwait(false);
                Validate(token);
                var info = ReadInfo(staged);
                if (info.Links != 1 || info.Attributes.HasFlag(FileAttributes.ReparsePoint)
                    || !string.Equals(FinalPath(staged), temporary, StringComparison.OrdinalIgnoreCase))
                { throw new InvalidDataException("Export staging identity changed."); }
                token.ThrowIfCancellationRequested();
                if (!admitted()) { throw new InvalidOperationException("Private export admission changed before publication."); }
                RenameNew(staged, path);
                published = true;
                KeepPublished(staged);
                if (owner.checkpoint is { } publishedCheckpoint) { await publishedCheckpoint.ReachedAsync(ExactAnswerExportPhase.Published, CancellationToken.None).ConfigureAwait(false); }
                stream.Flush(flushToDisk: true);
                // Once committed, finish verification even if cancellation arrives. Never claim rollback.
                stream.Position = 0;
                if (owner.checkpoint is { } readback) { await readback.ReachedAsync(ExactAnswerExportPhase.Readback, CancellationToken.None).ConfigureAwait(false); }
                await stream.ReadExactlyAsync(verification, CancellationToken.None).ConfigureAwait(false);
                if (stream.Length != bytes.Length || !bytes.AsSpan().SequenceEqual(verification)
                    || ReadInfo(staged).Links != 1
                    || !string.Equals(FinalPath(staged), path, StringComparison.OrdinalIgnoreCase))
                { throw new IOException("The published export could not be certified by exact readback."); }
                outcome = ExactAnswerExportOutcome.Saved;
            }
            catch (OperationCanceledException)
            { outcome = published ? ExactAnswerExportOutcome.Uncertain : ExactAnswerExportOutcome.Cancelled; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                owner.Failure(exception.GetType().Name, published);
                outcome = published ? ExactAnswerExportOutcome.Uncertain : ExactAnswerExportOutcome.NotWritten;
            }
            finally
            {
                // Unpublished staging uses delete-on-close. The publication operation clears it only
                // after a successful no-replace rename, while the same exclusive handle is still held.
                staged?.Dispose();
                CryptographicOperations.ZeroMemory(bytes);
                CryptographicOperations.ZeroMemory(verification);
            }
            activity.Complete(outcome switch
            {
                ExactAnswerExportOutcome.Saved => HostOperationOutcome.Completed,
                ExactAnswerExportOutcome.Cancelled => HostOperationOutcome.Cancelled,
                _ => HostOperationOutcome.Failed,
            });
            return outcome;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref consumed, 1);
            for (var index = handles.Count - 1; index >= 0; index--) { handles[index].Dispose(); }
            return ValueTask.CompletedTask;
        }
    }

    private static unsafe void RenameNew(SafeFileHandle file, string path)
    {
        var name = Encoding.Unicode.GetBytes(path);
        var lengthOffset = IntPtr.Size * 2;
        var nameOffset = lengthOffset + 4;
        var buffer = new byte[nameOffset + name.Length];
        BitConverter.TryWriteBytes(buffer.AsSpan(lengthOffset), name.Length);
        name.CopyTo(buffer, nameOffset);
        fixed (byte* pointer = buffer)
        {
            if (!SetFileInformationByHandle(file, 3, pointer, (uint)buffer.Length))
            { throw new IOException("Create-only export publication was refused; the destination may already exist."); }
        }
    }

    private static unsafe void KeepPublished(SafeFileHandle file)
    {
        // FileDispositionInfoEx + ON_CLOSE without DELETE clears CreateFile's
        // delete-on-close state; FileDispositionInfo(false) does not clear that flag.
        var disposition = 8;
        if (!SetFileInformationByHandle(file, 21, &disposition, 4))
        { throw new IOException("Export publication durability is unconfirmed."); }
    }

    private static unsafe HandleInfo ReadInfo(SafeFileHandle handle)
    {
        HandleInfo info;
        return GetFileInformationByHandle(handle, &info) ? info : throw new IOException("Export metadata could not be verified.");
    }

    private static unsafe string FinalPath(SafeFileHandle handle)
    {
        var buffer = new char[512];
        uint length;
        fixed (char* pointer = buffer) { length = GetFinalPathNameByHandle(handle, pointer, (uint)buffer.Length, 0); }
        if (length == 0 || length >= buffer.Length) { throw new IOException("The canonical export path is unavailable."); }
        var path = new string(buffer, 0, (int)length);
        return path.StartsWith(@"\\?\", StringComparison.Ordinal) ? path[4..]
            : throw new InvalidDataException("The destination is not a canonical local file.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInfo
    {
        internal FileAttributes Attributes;
        internal uint CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint sharing, nint security, uint creation, uint flags, nint template);
    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string root);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFileAttributesW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetFileAttributes(string path);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetFileInformationByHandle(SafeFileHandle handle, HandleInfo* information);
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle handle, char* path, uint length, uint flags);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool SetFileInformationByHandle(SafeFileHandle handle, int informationClass, void* information, uint size);
}
