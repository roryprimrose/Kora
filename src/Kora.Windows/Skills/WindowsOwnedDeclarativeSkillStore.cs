using System.Security.Principal;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Skills;
using Kora.Windows.Storage;
using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Skills;

public sealed partial class WindowsOwnedDeclarativeSkillStore(IApplicationDataPaths paths)
    : IOwnedDeclarativeSkillStore, IOwnedSkillRenameStore, IOwnedSkillDeletionStore
{
    private readonly RestrictedStorageDirectory directory = RestrictedStorageDirectory.ForOwnedSkills(paths);
    private readonly Lock gate = new();
    private string? rootIdentity;
    internal Action<string>? Checkpoint { get; set; }

    public string Destination(OwnedDeclarativeSkillDocument document)
    {
        OwnedDeclarativeSkillDocument.RequireIdentity(document.Id, document.Version);
        return Path.Combine(directory.Root, document.Id, document.Version);
    }

    public Task<IReadOnlyList<OwnedDeclarativeSkillSnapshot>> List(CancellationToken token) => Task.Run<IReadOnlyList<OwnedDeclarativeSkillSnapshot>>(() =>
    {
        lock (gate)
        {
            using var pins = PinRoot(token);
            using var lease = directory.AcquireBoundedLease(requireExisting: false, token);
            return Catalogue(pins, token).Packages;
        }
    }, token);

    public Task<OwnedDeclarativeSkillSnapshot?> Read(string id, string version, CancellationToken token) => Task.Run(() =>
    {
        OwnedDeclarativeSkillDocument.RequireIdentity(id, version);
        lock (gate)
        {
            using var pins = PinRoot(token);
            using var lease = directory.AcquireBoundedLease(requireExisting: false, token);
            RequireNoPendingRename(id, version, token);
            var idPath = ExactChild(directory.Root, id, SharedSkillCatalogue.MaximumPackages + 1);
            if (idPath is null) { return null; }
            pins.PinDirectory(idPath);
            directory.VerifyOwnedSkillDirectory(idPath);
            var versionPath = ExactChild(idPath, version, SharedSkillCatalogue.MaximumPackages);
            if (versionPath is null) { return null; }
            var saved = ReadPackage(versionPath, token);
            if (!string.Equals(saved.Document.Id, id, StringComparison.Ordinal)
                || !string.Equals(saved.Document.Version, version, StringComparison.Ordinal))
            { throw new InvalidDataException("The saved package manifest does not match the exact requested identity."); }
            return saved;
        }
    }, token);

    private static string? ExactChild(string parent, string name, int maximum)
    {
        var entries = Directory.EnumerateFileSystemEntries(parent).Take(maximum + 1).ToArray();
        if (entries.Length > maximum) { throw new InvalidDataException("The owned skill name inventory exceeds its bound."); }
        var selected = entries.SingleOrDefault(path => string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase));
        if (selected is not null && !string.Equals(Path.GetFileName(selected), name, StringComparison.Ordinal))
        { throw new InvalidDataException("The exact owned skill path has a noncanonical alias."); }
        return selected;
    }

    public Task<OwnedDeclarativeSkillSnapshot> Publish(OwnedDeclarativeSkillDocument document, Func<bool> eligible,
        Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token) =>
        Task.Run(async () => await commit(() => PublishCore(document, eligible, token)).ConfigureAwait(false), token);

    private OwnedDeclarativeSkillSnapshot PublishCore(OwnedDeclarativeSkillDocument document, Func<bool> eligible, CancellationToken token,
        Action<OwnedDeclarativeSkillSnapshot>? beforeCommit = null, OwnedSkillRename? rename = null)
    {
        using var activity = HostActivity.Current is null ? null : HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
        try
        {
            lock (gate)
            {
                if (deletionHolds.Contains(Destination(document)))
                { throw new InvalidOperationException("This exact owned ID/version is retired or has unconfirmed deletion. Use a new identity/version; do not recreate or retry it."); }
                using var pins = PinRoot(token);
                using var lease = beforeCommit is null ? directory.AcquireBoundedLease(requireExisting: false, token) : null;
                if (beforeCommit is null) { RequireNoPendingRename(document.Id, document.Version, token); }
                var existing = Catalogue(pins, token);
                var count = existing.Count + (rename is null ? 1 : 0);
                var bytes = existing.TotalBytes + PackageBytes(document);
                if (rename is not null)
                {
                    var reserved = existing.Pending.SingleOrDefault(p => p.Original.IsSameRevision(rename.Original));
                    var originalBytes = reserved is null ? PackageBytes(rename.Original.Document) : RenameBytes(reserved);
                    bytes = existing.TotalBytes - originalBytes + RenameBytes(rename);
                }
                RequireCatalogueCapacity(count, bytes);
                if (existing.Packages.Any(s => string.Equals(s.Document.Id, document.Id, StringComparison.Ordinal)
                    && string.Equals(s.Document.Version, document.Version, StringComparison.Ordinal)))
                { throw new InvalidOperationException("This skill version exists. Choose another ID/version or cancel."); }
                var idPath = Path.Combine(directory.Root, document.Id);
                if (!Directory.Exists(idPath))
                {
                    if (Directory.EnumerateFileSystemEntries(directory.Root).Take(SharedSkillCatalogue.MaximumPackages + 1).Count()
                        > SharedSkillCatalogue.MaximumPackages)
                    { throw new InvalidDataException("The skill ID inventory is full."); }
                    directory.CreateOwnedSkillDirectory(idPath);
                }
                var parent = pins.PinDirectory(idPath);
                directory.VerifyOwnedSkillDirectory(idPath);
                var stage = Path.Combine(idPath, ".owned-skill-" + Guid.NewGuid().ToString("N"));
                directory.CreateOwnedSkillDirectory(stage);
                using var stagePins = new OwnedSkillPackagePublication();
                SafeFileHandle? stageHandle = null;
                var createdFiles = new Dictionary<string, string>(StringComparer.Ordinal);
                var observed = false;
                try
                {
                    stageHandle = stagePins.PinDirectory(stage, forRename: true);
                    RequireCurrent(eligible, token);
                    Write(Path.Combine(stage, "manifest.yaml"), document.ManifestBytes, createdFiles, token);
                    Checkpoint?.Invoke("manifest-flushed");
                    Write(Path.Combine(stage, "instructions.md"), document.InstructionBytes, createdFiles, token);
                    Checkpoint?.Invoke("instructions-flushed");
                    using var manifestPin = OwnedSkillPackagePublication.OpenFile(Path.Combine(stage, "manifest.yaml"));
                    using var instructionsPin = OwnedSkillPackagePublication.OpenFile(Path.Combine(stage, "instructions.md"));
                    var staged = ReadPackage(stage, token, stageHandle);
                    RequireExact(document, staged.Document);
                    RequireCurrent(eligible, token);
                    directory.Verify();
                    directory.VerifyOwnedSkillDirectory(idPath);
                    directory.VerifyOwnedSkillDirectory(stage);
                    OwnedSkillPackagePublication.Verify(parent, idPath, directory: true);
                    OwnedSkillPackagePublication.Verify(stageHandle, stage, directory: true);
                    Checkpoint?.Invoke("before-commit");
                    RequireCurrent(eligible, token);
                    directory.VerifyOwnedSkillDirectory(idPath);
                    directory.VerifyOwnedSkillDirectory(stage);
                    directory.VerifyFile(Path.Combine(stage, "manifest.yaml"));
                    directory.VerifyFile(Path.Combine(stage, "instructions.md"));
                    OwnedSkillPackagePublication.Verify(manifestPin, Path.Combine(stage, "manifest.yaml"), directory: false);
                    OwnedSkillPackagePublication.Verify(instructionsPin, Path.Combine(stage, "instructions.md"), directory: false);
                    var finalStage = ReadPackage(stage, token, stageHandle);
                    if (!string.Equals(finalStage.ManifestIdentity, staged.ManifestIdentity, StringComparison.Ordinal)
                        || !string.Equals(finalStage.InstructionsIdentity, staged.InstructionsIdentity, StringComparison.Ordinal))
                    { throw new InvalidDataException("The staged package names no longer identify the reviewed files."); }
                    RequireExact(document, finalStage.Document);
                    // NTFS cannot rename a directory containing open child files. Keep root, ID and source
                    // directory handles pinned; join file handles, then verify both identities/bytes after the
                    // single no-replace directory commit. Any intervening file change is an uncertain publication.
                    manifestPin.Dispose();
                    instructionsPin.Dispose();
                    beforeCommit?.Invoke(staged);
                    RequireCurrent(eligible, token);
                    OwnedSkillPackagePublication.Commit(stageHandle, parent, document.Version);
                    observed = true;
                    Checkpoint?.Invoke("after-commit");
                    RequireCurrent(eligible, token);
                    var target = Destination(document);
                    OwnedSkillPackagePublication.Verify(stageHandle, target, directory: true);
                    var saved = ReadPackage(target, token, stageHandle);
                    if (!string.Equals(saved.DirectoryIdentity, staged.DirectoryIdentity, StringComparison.Ordinal)
                        || !string.Equals(saved.ManifestIdentity, staged.ManifestIdentity, StringComparison.Ordinal)
                        || !string.Equals(saved.InstructionsIdentity, staged.InstructionsIdentity, StringComparison.Ordinal))
                    { throw new IOException("The published package identity changed before durable readback."); }
                    RequireExact(document, saved.Document);
                    activity?.Complete(HostOperationOutcome.Completed);
                    return saved;
                }
                catch (Exception exception) when (observed && exception is not OutOfMemoryException)
                { throw new OwnedSkillPublicationUnconfirmedException(exception); }
                finally
                {
                    // Only an unpublished operation-created directory is eligible for cleanup, after all handles join.
                    // Uncertain published targets are never removed or silently retried.
                    if (!observed && stageHandle is not null) { CleanupStage(stage, stageHandle, createdFiles); }
                    stagePins.Dispose();
                }
            }
        }
        catch (OperationCanceledException) { activity?.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity?.Complete(HostOperationOutcome.Failed); throw; }
    }

    private void Write(string path, ReadOnlyMemory<byte> content, Dictionary<string, string> createdFiles, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var stream = directory.CreateNewFile(path, FileOptions.WriteThrough);
        createdFiles.Add(Path.GetFileName(path), WindowsFileIdentity.Read(stream.SafeFileHandle));
        stream.Write(content.Span);
        stream.Flush(flushToDisk: true);
        token.ThrowIfCancellationRequested();
    }

    private OwnedSkillPackagePublication PinRoot(CancellationToken token, bool requireExisting = false)
    {
        token.ThrowIfCancellationRequested();
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.RoamingRoot));
        if (!string.Equals(root, paths.RoamingRoot.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || root.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(root)!).DriveType != DriveType.Fixed
            || new[] { AppContext.BaseDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData) }
                .Where(p => !string.IsNullOrWhiteSpace(p)).Any(p => root.Equals(p.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    || root.StartsWith(p.TrimEnd('\\') + '\\', StringComparison.OrdinalIgnoreCase)))
        { throw new InvalidDataException("The owned roaming root must be canonical local application data outside protected code roots."); }
        var pins = new OwnedSkillPackagePublication();
        try
        {
            pins.PinAncestors(Path.GetDirectoryName(root)!);
            if (!Directory.Exists(root))
            {
                if (requireExisting || rootIdentity is not null) { throw new InvalidDataException("The bound roaming root is missing."); }
                using var identity = WindowsIdentity.GetCurrent();
                RestrictedStorageDirectory.CreateRestrictedDirectory(root,
                    identity.User ?? throw new InvalidOperationException("The profile owner is unavailable."));
            }
            pins.PinDirectory(root);
            if (!Directory.Exists(directory.Root))
            {
                if (requireExisting || rootIdentity is not null) { throw new InvalidDataException("The bound skill root is missing."); }
                directory.CreateNew();
            }
            directory.Verify();
            var handle = pins.PinDirectory(directory.Root);
            var identityValue = WindowsFileIdentity.Read(handle);
            if (rootIdentity is not null && !string.Equals(rootIdentity, identityValue, StringComparison.Ordinal))
            { throw new InvalidDataException("The bound owned skill root was replaced. Restart and inspect explicitly."); }
            rootIdentity = identityValue;
            return pins;
        }
        catch { pins.Dispose(); throw; }
    }

    private sealed record CatalogueInventory(OwnedDeclarativeSkillSnapshot[] Packages, OwnedSkillRename[] Pending)
    {
        internal int Count => Packages.Length + Pending.Length;
        internal long TotalBytes => Packages.Sum(s => (long)PackageBytes(s.Document)) + Pending.Sum(p => (long)RenameBytes(p));
    }

    private static int PackageBytes(OwnedDeclarativeSkillDocument document) =>
        document.ManifestBytes.Length + document.InstructionBytes.Length;

    private static int RenameBytes(OwnedSkillRename rename) =>
        Math.Max(PackageBytes(rename.Original.Document), PackageBytes(rename.Renamed));

    private static void RequireCatalogueCapacity(int count, long bytes)
    {
        if (count > SharedSkillCatalogue.MaximumPackages)
        { throw new InvalidDataException("The saved skill catalogue exceeds its immutable-version bound, including retained renames."); }
        if (bytes > SharedSkillCatalogue.MaximumTotalBytes)
        { throw new InvalidDataException("The saved skill catalogue exceeds its total byte bound, including retained renames."); }
    }

    private CatalogueInventory Catalogue(OwnedSkillPackagePublication pins, CancellationToken token)
    {
        var pending = ReadRenameIntents(token);
        var reservations = pending.Select(intent => intent.Proposal()).ToArray();
        var reservedBytes = reservations.Sum(p => (long)RenameBytes(p));
        RequireCatalogueCapacity(reservations.Length, reservedBytes);
        var result = new List<OwnedDeclarativeSkillSnapshot>();
        var roots = Directory.EnumerateFileSystemEntries(directory.Root).Take(SharedSkillCatalogue.MaximumPackages + 2).ToArray();
        if (roots.Length > SharedSkillCatalogue.MaximumPackages + 1)
        { throw new InvalidDataException("The skill ID inventory exceeds its bound."); }
        foreach (var idPath in roots)
        {
            if (Path.GetFileName(idPath) is "operation.lock") { continue; }
            var id = Path.GetFileName(idPath);
            OwnedDeclarativeSkillDocument.RequireIdentity(id, "1.0.0");
            pins.PinDirectory(idPath);
            directory.VerifyOwnedSkillDirectory(idPath);
            var entries = Directory.EnumerateFileSystemEntries(idPath).Take(SharedSkillCatalogue.MaximumPackages + 1).ToArray();
            if (entries.Length > SharedSkillCatalogue.MaximumPackages)
            { throw new InvalidDataException("The skill version directory inventory exceeds its bound."); }
            foreach (var versionPath in entries)
            {
                var version = Path.GetFileName(versionPath);
                if (version.StartsWith(".owned-skill-", StringComparison.Ordinal))
                {
                    if (version.Length != 45 || !Guid.TryParseExact(version[13..], "N", out _))
                    { throw new InvalidDataException("An unknown private staging entry was found."); }
                    pins.PinDirectory(versionPath);
                    directory.VerifyOwnedSkillDirectory(versionPath);
                    continue;
                }
                OwnedDeclarativeSkillDocument.RequireIdentity(id, version);
                if (pending.Any(intent => Affects(intent, id, version))) { continue; }
                var saved = ReadPackage(versionPath, token);
                if (!string.Equals(saved.Document.Id, id, StringComparison.Ordinal)
                    || !string.Equals(saved.Document.Version, version, StringComparison.Ordinal))
                { throw new InvalidDataException("The saved package manifest does not match its canonical destination."); }
                result.Add(saved);
                RequireCatalogueCapacity(result.Count + reservations.Length,
                    result.Sum(s => (long)PackageBytes(s.Document)) + reservedBytes);
            }
        }
        return new(result.OrderBy(s => s.Document.Id, StringComparer.Ordinal)
            .ThenBy(s => s.Document.Version, StringComparer.Ordinal).ToArray(), reservations);
    }

    private OwnedDeclarativeSkillSnapshot ReadPackage(string path, CancellationToken token, SafeFileHandle? pinnedDirectory = null)
    {
        using var packagePins = new OwnedSkillPackagePublication();
        var handle = pinnedDirectory ?? packagePins.PinDirectory(path);
        directory.VerifyOwnedSkillDirectory(path);
        var entries = Directory.EnumerateFileSystemEntries(path).Take(3).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        if (!entries.SequenceEqual(new[] { "instructions.md", "manifest.yaml" }, StringComparer.Ordinal))
        { throw new InvalidDataException("A saved skill must contain exactly manifest.yaml and instructions.md."); }
        var manifestPath = Path.Combine(path, "manifest.yaml");
        var instructionsPath = Path.Combine(path, "instructions.md");
        using var manifest = OwnedSkillPackagePublication.OpenFile(manifestPath);
        using var instructions = OwnedSkillPackagePublication.OpenFile(instructionsPath);
        directory.VerifyFile(manifestPath);
        directory.VerifyFile(instructionsPath);
        var metadata = ReadBytes(manifest, 4096, token);
        var guidance = ReadBytes(instructions, SharedSkillSnapshot.MaximumBytes - metadata.Length, token);
        var document = OwnedDeclarativeSkillDocument.Parse(metadata, guidance);
        directory.VerifyFile(manifestPath);
        directory.VerifyFile(instructionsPath);
        return new(document, WindowsFileIdentity.Read(handle), WindowsFileIdentity.Read(manifest), WindowsFileIdentity.Read(instructions));
    }

    private static byte[] ReadBytes(SafeFileHandle handle, int maximum, CancellationToken token)
    {
        var length = RandomAccess.GetLength(handle);
        if (length <= 0 || length > maximum) { throw new InvalidDataException("A skill file exceeds its exact byte bound."); }
        var bytes = new byte[(int)length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            token.ThrowIfCancellationRequested();
            var count = RandomAccess.Read(handle, bytes.AsSpan(offset), offset);
            if (count == 0) { throw new IOException("The pinned skill file changed during readback."); }
            offset += count;
        }
        return bytes;
    }

    private void CleanupStage(string stage, SafeFileHandle handle, Dictionary<string, string> createdFiles)
    {
        OwnedSkillPackagePublication.Verify(handle, stage, directory: true);
        directory.VerifyOwnedSkillDirectory(stage);
        foreach (var path in Directory.EnumerateFileSystemEntries(stage).Take(3))
        {
            if (!createdFiles.TryGetValue(Path.GetFileName(path), out var identity))
            { throw new InvalidDataException("Unexpected staging data was retained; no external entry was deleted."); }
            directory.VerifyFile(path);
            OwnedSkillPackagePublication.DeleteExactFile(path, identity);
        }
        OwnedSkillPackagePublication.MarkDeleted(handle);
    }

    private static void RequireCurrent(Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("Original skill authoring authority expired."); }
    }
    private static void RequireExact(OwnedDeclarativeSkillDocument expected, OwnedDeclarativeSkillDocument actual)
    {
        if (!expected.ManifestBytes.Span.SequenceEqual(actual.ManifestBytes.Span)
            || !expected.InstructionBytes.Span.SequenceEqual(actual.InstructionBytes.Span))
        { throw new IOException("Both durable skill files must match the exact reviewed bytes."); }
    }
}
