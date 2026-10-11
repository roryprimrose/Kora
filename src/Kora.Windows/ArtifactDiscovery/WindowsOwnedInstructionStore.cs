using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;

using Kora.Core.Artifacts;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Windows.Coordination;
using Kora.Windows.Storage;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Artifacts;

public sealed partial class WindowsOwnedInstructionStore(IApplicationDataPaths paths, ArtifactKind kind = ArtifactKind.Instruction) : IOwnedInstructionStore
{
    private readonly string directory = Path.Combine(paths.RoamingRoot, OwnedDefinitionKind.DirectoryName(kind));
    private readonly string extension = OwnedDefinitionKind.Extension(kind);
    private readonly int maximumFiles = OwnedDefinitionKind.MaximumFiles(kind);
    private readonly Lock gate = new();
    private string? binding;
    internal Action? AfterFinalComparison { get; set; }
    internal Action? AfterPublication { get; set; }

    public Task<IReadOnlyList<string>> List(CancellationToken token) => Task.Run(() => ListVerified(token), token);

    internal IReadOnlyList<string> ListVerified(CancellationToken token)
    {
        lock (gate)
        {
            using var pinned = PinRoot(token);
            var files = Directory.EnumerateFiles(directory, "*" + extension, SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).OfType<string>().ToArray();
            if (files.Length > maximumFiles) { throw new InvalidDataException("The owned kind inventory exceeds 128 files."); }
            return files.Select(file => OwnedInstructionNames.ReadName(file[..^extension.Length])).Order(StringComparer.Ordinal).ToArray();
        }
    }

    public Task<OwnedInstructionSnapshot?> Read(string name, CancellationToken token) => Task.Run(() => ReadVerified(name, token), token);

    internal OwnedInstructionSnapshot? ReadVerified(string name, CancellationToken token)
    {
        lock (gate)
        {
            using var pinned = PinRoot(token);
            return ReadCore(OwnedInstructionNames.ReadName(name), pinned, token);
        }
    }

    public Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected,
        ArtifactFileDocument? replacement, Func<bool> eligible,
        Func<Func<InstructionWriteResult>, Task<InstructionWriteResult>> commit, CancellationToken token) => Task.Run(async () => await commit(() => Publish(() =>
    {
        lock (gate)
        {
            name = OwnedInstructionNames.WriteName(name);
            replacement?.RequireEditableInstruction();
            if (replacement is not null && replacement.Definition.Kind != kind
                || expected is not null && expected.Document.Definition.Kind != kind)
            { throw new InvalidDataException("The reviewed definition kind does not match this host-owned store."); }
            using var pinned = PinRoot(token);
            var current = ReadCore(name, pinned, token);
            if (current is not null && !string.Equals(current.CanonicalFileName, name + extension, StringComparison.Ordinal))
            { throw new InvalidDataException("The selected filename is a case alias, not the original canonical owned write target."); }
            if (!Same(expected, current))
            { return new InstructionWriteResult(false, false, true, current); }
            RequireEligible(eligible, token);
            if (replacement is not null && current is not null
                && string.Equals(replacement.Definition.Digest, current.Document.Definition.Digest, StringComparison.Ordinal))
            { return new InstructionWriteResult(false, false, false, current, Unchanged: true); }
            if (replacement is not null && !replacement.Metadata.ContainsKey("kora-format"))
            { throw new InvalidDataException("New or changed owned instructions require the reviewed native v1 marker."); }
            if (current is null && replacement is not null
                && Directory.EnumerateFiles(directory, "*" + extension, SearchOption.TopDirectoryOnly).Take(maximumFiles).Count() >= maximumFiles)
            { throw new InvalidDataException("The owned instruction inventory is full; no new file was created."); }
            if (replacement is null)
            {
                if (current is null) { return new InstructionWriteResult(false, false, false, null, Unchanged: true); }
                using var target = OpenFile(Target(name), 0x80010000, 1);
                var reviewed = ReadHandle(name, pinned, target, token);
                if (!Same(current, reviewed)) { return new InstructionWriteResult(false, false, true, reviewed); }
                RequireEligible(eligible, token);
                var disposition = new byte[] { 1 };
                if (!SetFileInformationByHandle(target, 4, disposition, 1))
                { throw NativeFailure("The owned deletion could not be committed."); }
                return new InstructionWriteResult(true, true, false, null);
            }
            var temporary = Path.Combine(directory, $".instruction-{Guid.NewGuid():N}.tmp");
            var published = false;
            try
            {
                using var staged = CreatePrivateStaging(temporary);
                if (staged.IsInvalid) { throw NativeFailure("Private instruction staging could not be created."); }
                VerifyFile(staged, temporary);
                var bytes = replacement.CopyBytes();
                RandomAccess.Write(staged, bytes, 0);
                RandomAccess.FlushToDisk(staged);
                var final = ReadCore(name, pinned, token);
                if (!Same(current, final))
                { return new InstructionWriteResult(false, false, true, final); }
                RequireEligible(eligible, token);
                // Accepted optimistic window: only whole-byte replacement at this pinned directory name,
                // never following a subsequently substituted link or taking a caller-supplied destination.
                AfterFinalComparison?.Invoke();
                RequireEligible(eligible, token);
                Rename(staged, pinned.Last, name + extension, replace: current is not null, token);
                published = true;
            }
            finally
            {
                if (!published && File.Exists(temporary)) { File.Delete(temporary); }
            }
            try
            {
                AfterPublication?.Invoke();
                RequireEligible(eligible, token);
                var readBack = ReadCore(name, pinned, token)
                    ?? throw new IOException("Published instruction readback is unavailable.");
                if (!string.Equals(readBack.Document.Definition.Digest, replacement.Definition.Digest, StringComparison.Ordinal))
                { throw new IOException("Published instruction revision is unconfirmed."); }
                return new InstructionWriteResult(true, false, false, readBack);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                or InvalidDataException or InvalidOperationException or OperationCanceledException)
            { throw new InstructionPublicationUnconfirmedException(token.IsCancellationRequested, exception); }
        }
    })).ConfigureAwait(false), token);

    internal Task<InstructionWriteResult> Write(string name, OwnedInstructionSnapshot? expected,
        ArtifactFileDocument? replacement, Func<bool> eligible, CancellationToken token) =>
        Write(name, expected, replacement, eligible, operation => Task.FromResult(operation()), token);

    private static InstructionWriteResult Publish(Func<InstructionWriteResult> operation)
    {
        using var activity = HostActivity.Current is null ? null : HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
        try
        {
            var result = operation();
            activity?.Complete(result.Conflict ? HostOperationOutcome.Failed : HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException) { activity?.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity?.Complete(HostOperationOutcome.Failed); throw; }
    }

    private static bool Same(OwnedInstructionSnapshot? expected, OwnedInstructionSnapshot? current) =>
        expected is null ? current is null : current is not null
            && string.Equals(expected.Name, current.Name, StringComparison.Ordinal)
            && string.Equals(expected.DirectoryIdentity, current.DirectoryIdentity, StringComparison.Ordinal)
            && string.Equals(expected.FileIdentity, current.FileIdentity, StringComparison.Ordinal)
            && string.Equals(expected.CanonicalFileName, current.CanonicalFileName, StringComparison.Ordinal)
            && string.Equals(expected.Document.Definition.Digest, current.Document.Definition.Digest, StringComparison.Ordinal);

    private static void RequireEligible(Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("Owned instruction host/session/privacy admission changed."); }
    }

    private string Target(string name) => Path.Combine(directory, name + extension);

    private OwnedInstructionSnapshot? ReadCore(string name, Pinned pinned, CancellationToken token)
    {
        using var handle = CreateFile(Target(name), 0x80000000, 5, nint.Zero, 3, 0x42200000, nint.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            if (error == 2) { return null; }
            throw NativeFailure("The owned instruction is inaccessible.");
        }
        VerifyFile(handle, Target(name));
        var canonicalFile = Path.GetFileName(CoordinationNative.CanonicalPath(handle));
        var canonicalName = canonicalFile[..^extension.Length];
        var snapshot = ReadHandle(canonicalName, pinned, handle, token) with { CanonicalFileName = canonicalFile };
        if (kind == ArtifactKind.Prompt && snapshot.Document.Metadata.ContainsKey("kora-format")
            && !string.Equals(canonicalFile, canonicalName + extension, StringComparison.Ordinal))
        { throw new InvalidDataException("The managed prompt filename is not canonical; no alias review or write is permitted."); }
        return snapshot;
    }

    private OwnedInstructionSnapshot ReadHandle(string name, Pinned pinned, SafeFileHandle handle, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var length = RandomAccess.GetLength(handle);
        if (length is 0 or > ArtifactDefinition.MaximumContentLength)
        { throw new InvalidDataException("The original instruction exceeds its 64 KiB bound or is empty."); }
        var bytes = new byte[(int)length];
        var read = 0;
        while (read < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            var count = RandomAccess.Read(handle, bytes.AsSpan(read), read);
            if (count == 0) { throw new IOException("The instruction changed during the bounded read."); }
            read += count;
        }
        var document = ArtifactFileDocument.Parse(bytes, kind, "kora-profile", name);
        if (kind == ArtifactKind.Prompt && document.Metadata.ContainsKey("kora-format"))
        {
            OwnedInstructionNames.WriteName(name);
            document.RequireEditableInstruction();
        }
        return new(name, WindowsFileIdentity.Read(pinned.Last), WindowsFileIdentity.Read(handle), document);
    }

    private Pinned PinRoot(CancellationToken token)
    {
        var root = Path.GetFullPath(paths.RoamingRoot).TrimEnd('\\');
        if (root.Length < 3 || root[1] != ':' || root[2] != '\\'
            || !string.Equals(root, paths.RoamingRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || GetDriveType(root[..3]) != 3 || Protected(root))
        { throw new InvalidDataException("The owned data root is not an admitted canonical fixed local directory."); }
        var pinned = new Pinned();
        try
        {
            var current = root[..3];
            foreach (var component in new[] { string.Empty }.Concat(root[3..].Split('\\')))
            {
                token.ThrowIfCancellationRequested();
                if (component.Length > 0) { current = Path.Combine(current, component); }
                var handle = CreateFile(current, 0x80, 3, nint.Zero, 3, 0x02200000, nint.Zero);
                var error = Marshal.GetLastWin32Error();
                if (handle.IsInvalid && error is 2 or 3 && binding is null
                    && string.Equals(current, root, StringComparison.OrdinalIgnoreCase))
                {
                    handle.Dispose();
                    using var owner = WindowsIdentity.GetCurrent();
                    RestrictedStorageDirectory.CreateRestrictedDirectory(current,
                        owner.User ?? throw new InvalidOperationException("The application data owner is unknown."));
                    handle = CreateFile(current, 0x80, 3, nint.Zero, 3, 0x02200000, nint.Zero);
                }
                pinned.Add(handle);
                VerifyDirectory(handle, current);
            }
            if (!Directory.Exists(directory))
            {
                if (binding is not null) { throw new InvalidDataException("The previously bound instruction source is missing; no implicit recreation."); }
                using var ownerIdentity = WindowsIdentity.GetCurrent();
                RestrictedStorageDirectory.CreateRestrictedDirectory(directory,
                    ownerIdentity.User ?? throw new InvalidOperationException("The current storage owner is unknown."));
            }
            var owned = CreateFile(directory, 0x200A0, 3, nint.Zero, 3, 0x02200000, nint.Zero);
            pinned.Add(owned);
            VerifyDirectory(owned, directory);
            VerifyOwner(owned);
            var identity = WindowsFileIdentity.Read(owned);
            if (binding is not null && !string.Equals(binding, identity, StringComparison.Ordinal))
            { throw new InvalidDataException("The owned source directory was replaced. Restart and review its source identity."); }
            binding = identity;
            return pinned;
        }
        catch { pinned.Dispose(); throw; }
    }

    private static bool Protected(string root) => new[]
    {
        AppContext.BaseDirectory,
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    }.Where(path => !string.IsNullOrWhiteSpace(path)).Any(path =>
        root.Equals(path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
        || root.StartsWith(path.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase));

    private static SafeFileHandle OpenFile(string path, uint access, uint sharing)
    {
        var handle = CreateFile(path, access, sharing, nint.Zero, 3, 0x00200000, nint.Zero);
        try { VerifyFile(handle, path); return handle; }
        catch { handle.Dispose(); throw; }
    }

    private static void VerifyDirectory(SafeFileHandle handle, string path)
    {
        var info = Info(handle);
        if ((info.Attributes & 0x410) != 0x10
            || !string.Equals(CoordinationNative.CanonicalPath(handle).TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Reparse, replaced, or aliased owned parent is unavailable."); }
    }

    private static void VerifyFile(SafeFileHandle handle, string path)
    {
        var info = Info(handle);
        if ((info.Attributes & 0x410) != 0 || info.Links != 1
            || !string.Equals(CoordinationNative.CanonicalPath(handle), path, StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("Reparse, hard-linked, directory, or aliased instruction target is unavailable."); }
        VerifyOwner(handle);
    }

    private static FileInformation Info(SafeFileHandle handle)
    {
        if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var info))
        { throw NativeFailure("The owned storage identity is unavailable."); }
        return info;
    }

    private static IOException NativeFailure(string reason) => new(reason, new Win32Exception(Marshal.GetLastWin32Error()));

    private static void VerifyOwner(SafeFileHandle handle)
    {
        var status = GetSecurityInfo(handle, 1, 1, out var owner, out _, out _, out _, out var descriptor);
        if (status != 0) { throw new IOException("The instruction source owner is unavailable.", new Win32Exception((int)status)); }
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (identity.User is null || owner == nint.Zero || !identity.User.Equals(new SecurityIdentifier(owner)))
            { throw new UnauthorizedAccessException("The instruction source is not owned by the admitted local user."); }
        }
        finally { LocalFree(descriptor); }
    }

    private static SafeFileHandle CreatePrivateStaging(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("The instruction storage owner is unknown.");
        var security = new FileSecurity();
        security.SetOwner(user);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
        var descriptor = security.GetSecurityDescriptorBinaryForm();
        var memory = Marshal.AllocHGlobal(descriptor.Length);
        var attributes = Marshal.AllocHGlobal(Marshal.SizeOf<SecurityAttributes>());
        try
        {
            Marshal.Copy(descriptor, 0, memory, descriptor.Length);
            Marshal.StructureToPtr(new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = memory }, attributes, false);
            // NtSetInformationFile must finish before its native status buffer and cancellation ownership retire.
            return CreateFile(path, 0xC0010000, 0, attributes, 1, 0x00200000, nint.Zero);
        }
        finally { Marshal.FreeHGlobal(attributes); Marshal.FreeHGlobal(memory); }
    }

    private static void Rename(SafeFileHandle staged, SafeFileHandle root, string fileName, bool replace, CancellationToken token)
    {
        var name = System.Text.Encoding.Unicode.GetBytes(fileName);
        var offset = nint.Size == 8 ? 20 : 12;
        var buffer = new byte[offset + name.Length + 2];
        BitConverter.GetBytes(replace ? 3 : 0).CopyTo(buffer, 0);
        BitConverter.GetBytes((long)root.DangerousGetHandle()).AsSpan(0, nint.Size).CopyTo(buffer.AsSpan(nint.Size == 8 ? 8 : 4));
        BitConverter.GetBytes(name.Length).CopyTo(buffer, offset - 4);
        name.CopyTo(buffer, offset);
        using var thread = OpenThread(1, false, GetCurrentThreadId());
        if (thread.IsInvalid) { throw NativeFailure("The owned publication cancellation boundary is unavailable."); }
        using var cancellation = token.Register(() => CancelSynchronousIo(thread));
        token.ThrowIfCancellationRequested();
        var status = NtSetInformationFile(staged, out _, buffer, (uint)buffer.Length, 65);
        if (status == 0xc0000120 && token.IsCancellationRequested) { throw new OperationCanceledException(token); }
        if (!replace && status == 0xc0000035)
        { throw new InvalidOperationException("The new owned destination was created concurrently. Choose another new ID or cancel; no overwrite occurred."); }
        if (status != 0) { throw new IOException("Atomic owned publication failed.", new Win32Exception(unchecked((int)status))); }
    }

    private sealed class Pinned : IDisposable
    {
        private readonly List<SafeFileHandle> handles = [];
        internal SafeFileHandle Last => handles[^1];
        internal void Add(SafeFileHandle handle) => handles.Add(handle);
        public void Dispose() { foreach (var handle in handles) { handle.Dispose(); } }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        internal uint Attributes;
        internal uint CreatedLow;
        internal uint CreatedHigh;
        internal uint AccessedLow;
        internal uint AccessedHigh;
        internal uint ModifiedLow;
        internal uint ModifiedHigh;
        internal uint Volume;
        internal uint SizeHigh;
        internal uint SizeLow;
        internal uint Links;
        internal uint IndexHigh;
        internal uint IndexLow;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatus { internal nint Status; internal nint Information; }
    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes { internal int Length; internal nint Descriptor; internal int Inherit; }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFile(string path, uint access, uint sharing, nint security,
        uint disposition, uint flags, nint template);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle handle, int kind, byte[] data, uint size);
    [LibraryImport("ntdll.dll")]
    private static partial uint NtSetInformationFile(SafeFileHandle handle, out IoStatus status, byte[] data, uint size, int kind);
    [LibraryImport("kernel32.dll", EntryPoint = "GetDriveTypeW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint GetDriveType(string root);
    [LibraryImport("advapi32.dll")]
    private static partial uint GetSecurityInfo(SafeFileHandle handle, int objectType, uint information,
        out nint owner, out nint group, out nint dacl, out nint sacl, out nint descriptor);
    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeWaitHandle OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);
    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CancelSynchronousIo(SafeWaitHandle thread);
}
