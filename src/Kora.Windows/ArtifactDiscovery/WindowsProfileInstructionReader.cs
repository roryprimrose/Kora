using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

using Kora.Core.Artifacts;
using Kora.Core.Context;
using Kora.Windows.Coordination;
using Kora.Windows.Storage;

using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Artifacts;

public sealed class WindowsProfileInstructionReader : IProfileInstructionReader
{
    private readonly ILocalFileInspector inspector;
    private readonly string roaming;

    public WindowsProfileInstructionReader(ILocalFileInspector inspector)
        : this(inspector, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)) { }

    internal WindowsProfileInstructionReader(ILocalFileInspector inspector, string profileRoamingRoot)
    {
        this.inspector = inspector;
        LocalFilePolicy.ValidateFolderPath(profileRoamingRoot);
        roaming = profileRoamingRoot;
    }

    public async Task<IProfileInstructionSelection> OpenSelection(string selectedPath, CancellationToken token)
    {
        LocalFilePolicy.ValidatePath(selectedPath);
        if (!selectedPath.EndsWith(".instructions.md", StringComparison.Ordinal) || !SupportedLocation(selectedPath))
        { throw new InvalidDataException("Select one canonical .instructions.md file from the personal Code or Code - Insiders User prompts/profile prompts locations. Other roots and formats are unavailable."); }
        var selected = await inspector.InspectAsync(selectedPath, token).ConfigureAwait(false);
        try
        {
            if (!string.Equals(selected.Metadata.CanonicalPath, selectedPath, StringComparison.Ordinal)
                || selected.Metadata.ByteLength is <= 0 or > ArtifactDefinition.MaximumContentLength)
            { throw new InvalidDataException("The original canonical instruction identity or 64 KiB bound is invalid."); }
            using var handle = File.OpenHandle(selectedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            RequireExactIdentity(handle, selected.Metadata);
            RequireOwner(selectedPath);
            return new Selection(inspector, selected);
        }
        catch { selected.Dispose(); throw; }
    }

    private bool SupportedLocation(string path)
    {
        foreach (var editor in new[] { "Code", "Code - Insiders" })
        {
            var root = Path.Combine(roaming, editor, "User");
            if (!path.StartsWith(root + '\\', StringComparison.OrdinalIgnoreCase)) { continue; }
            var parts = path[(root.Length + 1)..].Split('\\');
            if (parts.Length == 2 && string.Equals(parts[0], "prompts", StringComparison.Ordinal)
                || parts.Length == 4 && string.Equals(parts[0], "profiles", StringComparison.Ordinal)
                    && string.Equals(parts[2], "prompts", StringComparison.Ordinal))
            { return true; }
        }
        return false;
    }

    private static void RequireOwner(string path)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = new FileInfo(path).GetAccessControl(AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
        if (identity.User is null || !identity.User.Equals(owner))
        { throw new InvalidDataException("The profile instruction must belong to the current Windows user."); }
    }

    private static void RequireExactIdentity(SafeFileHandle handle, LocalFileMetadata expected)
    {
        if (!string.Equals(WindowsFileIdentity.Read(handle), expected.FileIdentity, StringComparison.Ordinal)
            || !string.Equals(CoordinationNative.CanonicalPath(handle), expected.CanonicalPath, StringComparison.Ordinal)
            || RandomAccess.GetLength(handle) != expected.ByteLength)
        { throw new InvalidDataException("The selected personal instruction is not the exact canonical native source identity and size."); }
    }

    private sealed class Selection(ILocalFileInspector inspector, ILocalFileSelection original) : IProfileInstructionSelection
    {
        private int disposed;
        public Guid Id { get; } = Guid.NewGuid();
        public LocalFileMetadata Metadata => original.Metadata;

        public Task<byte[]> Read(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            return original.ReadAsync(token);
        }

        public async Task<InstructionWriteResult> WithCurrent(ProfileInstructionSnapshot expected, Func<bool> consentCurrent,
            Func<Func<bool>, Task<InstructionWriteResult>> publish, CancellationToken token)
        {
            void RequireCurrent()
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
                token.ThrowIfCancellationRequested();
                if (!consentCurrent() || expected.SourceId != Id || expected.Metadata != Metadata)
                { throw new InvalidOperationException("The exact profile source read consent or lease expired."); }
            }
            RequireCurrent();
            using var observed = await inspector.InspectAsync(Metadata.CanonicalPath, token).ConfigureAwait(false);
            RequireCurrent();
            if (observed.Metadata != Metadata)
            { throw new InvalidDataException("The admitted profile instruction was replaced or changed; select and consent again."); }
            RequireOwner(Metadata.CanonicalPath);
            var bytes = await observed.ReadAsync(token).ConfigureAwait(false);
            var expectedBytes = expected.Document.CopyBytes();
            var verification = new byte[expectedBytes.Length];
            try
            {
                RequireCurrent();
                if (!bytes.AsSpan().SequenceEqual(expectedBytes))
                { throw new InvalidDataException("The reviewed profile instruction bytes changed; nothing was copied."); }
                using var handle = File.OpenHandle(Metadata.CanonicalPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, FileOptions.RandomAccess);
                var checks = 1;
                bool Fresh()
                {
                    RequireCurrent();
                    if (++checks > ProfileInstructionSnapshot.MaximumFreshnessChecks)
                    { throw new InvalidOperationException("The bounded source freshness-read consent is exhausted."); }
                    RequireOwner(Metadata.CanonicalPath);
                    RequireExactIdentity(handle, Metadata);
                    var offset = 0;
                    while (offset < verification.Length)
                    {
                        token.ThrowIfCancellationRequested();
                        var count = RandomAccess.Read(handle, verification.AsSpan(offset), offset);
                        if (count == 0) { throw new IOException("The profile source changed during final verification."); }
                        offset += count;
                    }
                    if (!verification.AsSpan().SequenceEqual(expectedBytes))
                    { throw new InvalidDataException("The reviewed source bytes changed before publication."); }
                    return true;
                }
                // Ancestors and the original file stay pinned. The owned writer also calls
                // this bounded exact-byte guard after staging and immediately before rename.
                return await publish(Fresh).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
                CryptographicOperations.ZeroMemory(expectedBytes);
                CryptographicOperations.ZeroMemory(verification);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0) { original.Dispose(); }
        }
    }
}
