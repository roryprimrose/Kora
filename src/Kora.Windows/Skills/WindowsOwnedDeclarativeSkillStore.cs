using System.Security.Principal;
using System.Collections.Immutable;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Skills;
using Kora.Windows.Storage;
using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Skills;

public sealed partial class WindowsOwnedDeclarativeSkillStore(IApplicationDataPaths paths) : IOwnedDeclarativeSkillStore, IOwnedSkillDeletionStore
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
            return Catalogue(pins, token);
        }
    }, token);

    public Task<OwnedDeclarativeSkillSnapshot?> Read(string id, string version, CancellationToken token,
        OwnedSkillReadScope scope = OwnedSkillReadScope.CompletePackage, OwnedDeclarativeSkillSnapshot? expectedPrimary = null) => Task.Run(() =>
    {
        OwnedDeclarativeSkillDocument.RequireIdentity(id, version);
        lock (gate)
        {
            using var pins = PinRoot(token);
            using var lease = directory.AcquireBoundedLease(requireExisting: false, token);
            var idPath = ExactChild(directory.Root, id, SharedSkillCatalogue.MaximumPackages + 1);
            if (idPath is null) { return null; }
            pins.PinDirectory(idPath);
            directory.VerifyOwnedSkillDirectory(idPath);
            var versionPath = ExactChild(idPath, version, SharedSkillCatalogue.MaximumPackages);
            if (versionPath is null) { return null; }
            var saved = ReadPackage(versionPath, token, scope: scope, expectedPrimary: expectedPrimary);
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
        Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token,
        OwnedDeclarativeSkillSnapshot? expectedSource = null) =>
        Task.Run(async () => await commit(() => PublishCore(document, eligible, expectedSource, token)).ConfigureAwait(false), token);

    private OwnedDeclarativeSkillSnapshot PublishCore(OwnedDeclarativeSkillDocument document, Func<bool> eligible,
        OwnedDeclarativeSkillSnapshot? expectedSource, CancellationToken token)
    {
        document.RequireComplete();
        using var activity = HostActivity.Current is null ? null : HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Storage);
        try
        {
            lock (gate)
            {
                if (deletionHolds.Contains(Destination(document)))
                { throw new InvalidOperationException("This exact owned ID/version is retired or has unconfirmed deletion. Use a new identity/version; do not recreate or retry it."); }
                using var pins = PinRoot(token);
                using var lease = directory.AcquireBoundedLease(requireExisting: false, token);
                using var sourcePins = new OwnedSkillPackagePublication();
                if (expectedSource is not null)
                {
                    expectedSource.Document.RequireComplete();
                    var sourcePath = Destination(expectedSource.Document);
                    sourcePins.PinDirectory(sourcePath);
                    _ = Inventory(sourcePath, expectedSource.Document.ReferenceDeclaration, sourcePins, token);
                    foreach (var file in expectedSource.Document.References.Select(item => item.RelativePath)
                        .Prepend("instructions.md").Prepend("manifest.yaml"))
                    { sourcePins.PinSourceFile(Path.Combine(sourcePath, file)); }
                    var actual = ReadPackage(sourcePath, token, expectedPrimary: expectedSource);
                    if (!expectedSource.IsSameRevision(actual))
                    { throw new InvalidDataException("The exact reopened package changed or was replaced; no new version was published."); }
                }
                var existing = Catalogue(pins, token);
                if (existing.Length >= SharedSkillCatalogue.MaximumPackages)
                { throw new InvalidDataException("The saved skill catalogue is full, including all immutable versions."); }
                if (existing.Sum(s => s.TotalBytes)
                    + document.TotalBytes > SharedSkillCatalogue.MaximumTotalBytes)
                { throw new InvalidDataException("The saved skill catalogue exceeds its total byte bound."); }
                if (existing.Any(s => string.Equals(s.Document.Id, document.Id, StringComparison.Ordinal)
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
                var createdDirectories = new Dictionary<string, string>(StringComparer.Ordinal);
                var contentPins = new List<SafeFileHandle>();
                var observed = false;
                try
                {
                    stageHandle = stagePins.PinDirectory(stage, forRename: true);
                    RequireCurrent(eligible, token);
                    Write(Path.Combine(stage, "manifest.yaml"), document.ManifestBytes, createdFiles, token);
                    Checkpoint?.Invoke("manifest-flushed");
                    Write(Path.Combine(stage, "instructions.md"), document.InstructionBytes, createdFiles, token);
                    Checkpoint?.Invoke("instructions-flushed");
                    foreach (var reference in document.References)
                    {
                        var parentPath = stage;
                        var parts = reference.RelativePath.Split('\\');
                        foreach (var part in parts[..^1])
                        {
                            parentPath = Path.Combine(parentPath, part);
                            if (createdDirectories.ContainsKey(parentPath)) { continue; }
                            directory.CreateOwnedSkillDirectory(parentPath);
                            using var child = new OwnedSkillPackagePublication();
                            createdDirectories.Add(parentPath, WindowsFileIdentity.Read(child.PinDirectory(parentPath)));
                        }
                        Write(Path.Combine(stage, reference.RelativePath), reference.Bytes.ToArray(), createdFiles, token);
                        Checkpoint?.Invoke("reference-flushed");
                    }
                    foreach (var file in createdFiles.Keys)
                    { contentPins.Add(OwnedSkillPackagePublication.OpenFile(file)); }
                    var manifestPin = contentPins[0];
                    var instructionsPin = contentPins[1];
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
                        || !string.Equals(finalStage.InstructionsIdentity, staged.InstructionsIdentity, StringComparison.Ordinal)
                        || !staged.IsSameRevision(finalStage))
                    { throw new InvalidDataException("The staged package names no longer identify the reviewed files."); }
                    RequireExact(document, finalStage.Document);
                    // NTFS cannot rename a directory containing open child files. Keep root, ID and source
                    // directory handles pinned; join file handles, then verify both identities/bytes after the
                    // single no-replace directory commit. Any intervening file change is an uncertain publication.
                    foreach (var pin in contentPins) { pin.Dispose(); }
                    OwnedSkillPackagePublication.Commit(stageHandle, parent, document.Version);
                    observed = true;
                    Checkpoint?.Invoke("after-commit");
                    RequireCurrent(eligible, token);
                    var target = Destination(document);
                    OwnedSkillPackagePublication.Verify(stageHandle, target, directory: true);
                    var saved = ReadPackage(target, token, stageHandle);
                    if (!string.Equals(saved.DirectoryIdentity, staged.DirectoryIdentity, StringComparison.Ordinal)
                        || !string.Equals(saved.ManifestIdentity, staged.ManifestIdentity, StringComparison.Ordinal)
                        || !string.Equals(saved.InstructionsIdentity, staged.InstructionsIdentity, StringComparison.Ordinal)
                        || !staged.IsSameRevision(saved))
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
                    foreach (var pin in contentPins) { pin.Dispose(); }
                    if (!observed && stageHandle is not null) { CleanupStage(stage, stageHandle, createdFiles, createdDirectories); }
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
        createdFiles.Add(path, WindowsFileIdentity.Read(stream.SafeFileHandle));
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

    private OwnedDeclarativeSkillSnapshot[] Catalogue(OwnedSkillPackagePublication pins, CancellationToken token)
    {
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
                var saved = ReadPackage(versionPath, token, scope: OwnedSkillReadScope.PrimaryDeclaration);
                if (!string.Equals(saved.Document.Id, id, StringComparison.Ordinal)
                    || !string.Equals(saved.Document.Version, version, StringComparison.Ordinal))
                { throw new InvalidDataException("The saved package manifest does not match its canonical destination."); }
                result.Add(saved);
                if (result.Count > SharedSkillCatalogue.MaximumPackages)
                { throw new InvalidDataException("The saved skill catalogue exceeds its immutable-version bound."); }
                if (result.Sum(s => s.TotalBytes) > SharedSkillCatalogue.MaximumTotalBytes)
                { throw new InvalidDataException("The saved skill catalogue exceeds its total byte bound."); }
            }
        }
        return result.OrderBy(s => s.Document.Id, StringComparer.Ordinal).ThenBy(s => s.Document.Version, StringComparer.Ordinal).ToArray();
    }

    private OwnedDeclarativeSkillSnapshot ReadPackage(string path, CancellationToken token, SafeFileHandle? pinnedDirectory = null,
        OwnedSkillReadScope scope = OwnedSkillReadScope.CompletePackage, OwnedDeclarativeSkillSnapshot? expectedPrimary = null)
    {
        using var packagePins = new OwnedSkillPackagePublication();
        var handle = pinnedDirectory ?? packagePins.PinDirectory(path);
        directory.VerifyOwnedSkillDirectory(path);
        var manifestPath = Path.Combine(path, "manifest.yaml");
        var instructionsPath = Path.Combine(path, "instructions.md");
        using var manifest = OwnedSkillPackagePublication.OpenFile(manifestPath);
        using var instructions = OwnedSkillPackagePublication.OpenFile(instructionsPath);
        directory.VerifyFile(manifestPath);
        directory.VerifyFile(instructionsPath);
        var metadata = ReadBytes(manifest, 4096, token);
        var guidance = ReadBytes(instructions, SharedSkillSnapshot.MaximumBytes - metadata.Length, token);
        if (scope is not (OwnedSkillReadScope.PrimaryDeclaration or OwnedSkillReadScope.CompletePackage))
        { throw new ArgumentOutOfRangeException(nameof(scope)); }
        var document = OwnedDeclarativeSkillDocument.Parse(metadata, guidance);
        if (expectedPrimary is not null && !expectedPrimary.IsSamePrimary(new(document,
            WindowsFileIdentity.Read(handle), WindowsFileIdentity.Read(manifest), WindowsFileIdentity.Read(instructions))))
        { throw new InvalidDataException("The exact reviewed primary declaration changed; no reference bodies were read."); }
        var entries = Inventory(path, document.ReferenceDeclaration, packagePins, token);
        var referenceIdentities = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        long storedBytes = metadata.Length + guidance.Length;
        foreach (var relative in document.ReferenceDeclaration?.Paths ?? [])
        {
            var referencePath = Path.Combine(path, relative);
            var attributes = File.GetAttributes(referencePath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint) || attributes.HasFlag(FileAttributes.Directory))
            { throw new InvalidDataException("A declared component is reparse or not a plain file."); }
            var length = new FileInfo(referencePath).Length;
            if (length is <= 0 or > SharedSkillReferenceDeclaration.MaximumReferenceBytes)
            { throw new InvalidDataException("A declared component exceeds its complete byte bound."); }
            storedBytes += length;
        }
        if (storedBytes > SharedSkillReferenceDeclaration.MaximumBundleBytes)
        { throw new InvalidDataException("The whole declared tree exceeds the aggregate byte bound."); }
        if (document.ReferenceDeclaration is { } declaration && scope == OwnedSkillReadScope.CompletePackage)
        {
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var referencePins = new List<SafeFileHandle>();
            try
            {
                var remaining = SharedSkillReferenceDeclaration.MaximumBundleBytes - metadata.Length - guidance.Length;
                foreach (var relative in declaration.Paths)
                {
                    Checkpoint?.Invoke("before-reference-read");
                    var referencePath = Path.Combine(path, relative);
                    var pin = OwnedSkillPackagePublication.OpenFile(referencePath);
                    referencePins.Add(pin);
                    directory.VerifyFile(referencePath);
                    var bytes = ReadBytes(pin, Math.Min(remaining, SharedSkillReferenceDeclaration.MaximumReferenceBytes), token);
                    remaining -= bytes.Length;
                    contents.Add(relative, bytes);
                    referenceIdentities.Add(relative, WindowsFileIdentity.Read(pin));
                }
                document = OwnedDeclarativeSkillDocument.Parse(metadata, guidance, contents);
                if (!entries.SequenceEqual(Inventory(path, declaration, packagePins, token), StringComparer.Ordinal))
                { throw new InvalidDataException("The selected package inventory changed during capture."); }
            }
            finally { foreach (var pin in referencePins) { pin.Dispose(); } }
        }
        directory.VerifyFile(manifestPath);
        directory.VerifyFile(instructionsPath);
        return new(document, WindowsFileIdentity.Read(handle), WindowsFileIdentity.Read(manifest), WindowsFileIdentity.Read(instructions))
        { ReferenceIdentities = referenceIdentities.ToImmutable(), StoredBytes = storedBytes };
    }

    private string[] Inventory(string root, SharedSkillReferenceDeclaration? declaration,
        OwnedSkillPackagePublication pins, CancellationToken token)
    {
        var expected = new HashSet<string>(StringComparer.Ordinal) { "manifest.yaml", "instructions.md" };
        foreach (var file in declaration?.Paths ?? [])
        {
            expected.Add(file);
            for (var split = file.IndexOf('\\'); split >= 0; split = file.IndexOf('\\', split + 1))
            { expected.Add(file[..split]); }
        }
        var found = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var current))
        {
            token.ThrowIfCancellationRequested();
            foreach (var entry in Directory.EnumerateFileSystemEntries(current).Take(expected.Count + 1))
            {
                var relative = Path.GetRelativePath(root, entry);
                if (!expected.Contains(relative) || !found.Add(relative))
                { throw new InvalidDataException("The exact declared tree contains unknown, aliased or excessive entries."); }
                if (expected.Any(item => item.StartsWith(relative + '\\', StringComparison.Ordinal)))
                {
                    pins.PinDirectory(entry);
                    directory.VerifyOwnedSkillDirectory(entry);
                    pending.Enqueue(entry);
                }
            }
        }
        if (!found.SetEquals(expected)) { throw new InvalidDataException("The exact declared package tree is incomplete."); }
        return found.Order(StringComparer.Ordinal).ToArray();
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

    private void CleanupStage(string stage, SafeFileHandle handle, Dictionary<string, string> createdFiles,
        Dictionary<string, string> createdDirectories)
    {
        OwnedSkillPackagePublication.Verify(handle, stage, directory: true);
        directory.VerifyOwnedSkillDirectory(stage);
        foreach (var path in createdFiles.Keys)
        {
            directory.VerifyFile(path);
            OwnedSkillPackagePublication.DeleteExactFile(path, createdFiles[path]);
        }
        foreach (var path in createdDirectories.Keys.OrderByDescending(item => item.Length))
        {
            using var pins = new OwnedSkillPackagePublication();
            var child = pins.PinDirectory(path, forRename: true);
            if (!string.Equals(createdDirectories[path], WindowsFileIdentity.Read(child), StringComparison.Ordinal)
                || Directory.EnumerateFileSystemEntries(path).Any())
            { throw new InvalidDataException("Unexpected staging data was retained; no external entry was deleted."); }
            OwnedSkillPackagePublication.MarkDeleted(child);
        }
        if (Directory.EnumerateFileSystemEntries(stage).Any())
        { throw new InvalidDataException("Unexpected staging data was retained; no external entry was deleted."); }
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
            || !expected.InstructionBytes.Span.SequenceEqual(actual.InstructionBytes.Span)
            || !actual.IsComplete || !string.Equals(expected.RevisionDigest, actual.RevisionDigest, StringComparison.Ordinal))
        { throw new IOException("Every durable skill component must match the exact reviewed bytes."); }
    }
}
