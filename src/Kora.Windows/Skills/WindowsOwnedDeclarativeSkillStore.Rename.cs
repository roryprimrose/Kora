using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Skills;
using Kora.Windows.Artifacts;
using Kora.Windows.Storage;
using Microsoft.Win32.SafeHandles;

namespace Kora.Windows.Skills;

public sealed partial class WindowsOwnedDeclarativeSkillStore
{
    private const int MaximumRenameBytes = 192 * 1024;
    private static readonly JsonSerializerOptions RenameJson = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private readonly RestrictedStorageDirectory renameStorage = RestrictedStorageDirectory.ForOwnedSkillRenames(paths);
    private readonly HashSet<string> observedRenameIntents = new(StringComparer.Ordinal);
    private string? renameDirectoryIdentity;
    private string RenameDirectory => Path.Combine(paths.LocalRoot, "OwnedSkillRenames");
    private sealed record RenameIntent(int Format, byte[] Manifest, byte[] Instructions, string NewId,
        string Root, string SourceParent, string DestinationParent, string SourceDirectory, string SourceManifest,
        string SourceInstructions, string DestinationDirectory, string DestinationManifest, string DestinationInstructions)
    {
        [JsonIgnore] public string? FileIdentity { get; init; }
        internal OwnedSkillRename Proposal() => new(new(OwnedDeclarativeSkillDocument.Parse(Manifest, Instructions),
            SourceDirectory, SourceManifest, SourceInstructions), NewId);
        internal OwnedDeclarativeSkillSnapshot Destination() => new(Proposal().Renamed,
            DestinationDirectory, DestinationManifest, DestinationInstructions);
    }

    public Task<IReadOnlyList<OwnedSkillRename>> PendingRenames(CancellationToken token) =>
        Task.Run<IReadOnlyList<OwnedSkillRename>>(() =>
        {
            lock (gate)
            {
                using var pins = PinRoot(token);
                using var lease = directory.AcquireBoundedLease(requireExisting: false, token);
                return ReadRenameIntents(token).Select(intent => intent.Proposal()).ToArray();
            }
        }, token);

    public Task<OwnedDeclarativeSkillSnapshot> Rename(OwnedSkillRename proposal, bool recovery, Func<bool> eligible,
        Func<Func<OwnedDeclarativeSkillSnapshot>, Task<OwnedDeclarativeSkillSnapshot>> commit, CancellationToken token) =>
        Task.Run(async () => await commit(() => RenameCore(proposal, recovery, eligible, token)).ConfigureAwait(false), token);

    private OwnedDeclarativeSkillSnapshot RenameCore(OwnedSkillRename proposal, bool recovery, Func<bool> eligible, CancellationToken token)
    {
        lock (gate)
        {
            using var pins = PinRoot(token);
            using var lease = directory.AcquireBoundedLease(requireExisting: false, token);
            RequireCurrent(eligible, token);
            var old = proposal.Original.Document;
            var next = proposal.Renamed;
            var intents = ReadRenameIntents(token);
            var intent = intents.SingleOrDefault(value => Affects(value, old.Id, old.Version));
            if (recovery)
            {
                if (intent is null || !intent.Proposal().Original.IsSameRevision(proposal.Original)
                    || !string.Equals(intent.NewId, next.Id, StringComparison.Ordinal))
                { throw new InvalidOperationException("The exact retained rename changed. Review recovery again; nothing was retargeted."); }
            }
            else if (intents.Any(value => Affects(value, old.Id, old.Version) || Affects(value, next.Id, next.Version)))
            { throw new InvalidOperationException("An affected package has retained rename intent. Review Recover rename; do not retry."); }
            if (!recovery) { RequireOriginal(proposal, token); }

            var sourceParentPath = ExactChild(directory.Root, old.Id, SharedSkillCatalogue.MaximumPackages + 1)
                ?? throw new InvalidDataException("The exact source parent is absent.");
            var sourceParent = pins.PinDirectory(sourceParentPath);
            directory.VerifyOwnedSkillDirectory(sourceParentPath);
            var destinationParentPath = ExactChild(directory.Root, next.Id, SharedSkillCatalogue.MaximumPackages + 1);
            if (destinationParentPath is null)
            {
                if (recovery) { throw new InvalidDataException("The retained destination parent is missing. No replacement parent was created."); }
                if (Directory.EnumerateFileSystemEntries(directory.Root).Take(SharedSkillCatalogue.MaximumPackages + 2).Count()
                    >= SharedSkillCatalogue.MaximumPackages + 1)
                { throw new InvalidDataException("The bounded skill ID inventory has no room for a new rename destination."); }
                destinationParentPath = Path.Combine(directory.Root, next.Id);
                directory.CreateOwnedSkillDirectory(destinationParentPath);
            }
            var destinationParent = pins.PinDirectory(destinationParentPath);
            directory.VerifyOwnedSkillDirectory(destinationParentPath);
            var sourceParentId = WindowsFileIdentity.Read(sourceParent);
            var destinationParentId = WindowsFileIdentity.Read(destinationParent);
            if (intent is not null && (!string.Equals(intent.SourceParent, sourceParentId, StringComparison.Ordinal)
                || !string.Equals(intent.DestinationParent, destinationParentId, StringComparison.Ordinal)))
            { throw new InvalidDataException("A retained rename parent was replaced. Explicit external inspection is required."); }
            var target = ExactChild(destinationParentPath, next.Version, SharedSkillCatalogue.MaximumPackages);
            if (!recovery && target is not null)
            { throw new InvalidOperationException("The destination package or alias already exists. Choose another ID; nothing was overwritten."); }
            OwnedSkillRenameEffect? effect = recovery ? OwnedSkillRenameEffect.IntentMayExist : null;
            try
            {
                OwnedDeclarativeSkillSnapshot saved;
                if (target is null)
                {
                    RequireOriginal(proposal, token);
                    saved = PublishCore(next, eligible, token, staged =>
                    {
                        RequireOriginal(proposal, token);
                        var updated = new RenameIntent(1, old.ManifestBytes.ToArray(), old.InstructionBytes.ToArray(), next.Id,
                            rootIdentity!, sourceParentId, destinationParentId, proposal.Original.DirectoryIdentity,
                            proposal.Original.ManifestIdentity, proposal.Original.InstructionsIdentity,
                            staged.DirectoryIdentity, staged.ManifestIdentity, staged.InstructionsIdentity);
                        effect = OwnedSkillRenameEffect.IntentMayExist;
                        intent = WriteRenameIntent(updated, intent, eligible, token);
                        Checkpoint?.Invoke("rename-intent-durable");
                        RequireOriginal(proposal, token);
                    });
                }
                else
                {
                    saved = ReadPackage(target, token);
                    if (!intent!.Destination().IsSameRevision(saved))
                    { throw new InvalidDataException("The retained destination identity or complete bytes changed. Nothing was overwritten or deleted."); }
                }
                effect = OwnedSkillRenameEffect.DestinationPublished;
                RequireCurrent(eligible, token);
                Checkpoint?.Invoke("rename-destination-verified");
                if (!intent!.Destination().IsSameRevision(saved))
                { throw new InvalidDataException("The destination does not match the retained exact staged package."); }
                RequireIntentCurrent(intent, token);
                if (!recovery) { RequireOriginal(proposal, token); }
                effect = OwnedSkillRenameEffect.SourceRetirementStarted;
                RetireRenameSource(proposal, recovery, eligible, token);
                Checkpoint?.Invoke("rename-source-retired");
                RequireCurrent(eligible, token);
                var readback = ReadPackage(Destination(next), token);
                if (!saved.IsSameRevision(readback)
                    || ExactChild(sourceParentPath, old.Version, SharedSkillCatalogue.MaximumPackages) is not null)
                { throw new IOException("Complete destination identity/bytes or original-name absence is unconfirmed."); }
                RequireIntentCurrent(intent, token);
                RequireCurrent(eligible, token);
                OwnedSkillPackagePublication.DeleteExactFile(Path.Combine(RenameDirectory, IntentName(intent)), intent.FileIdentity!);
                observedRenameIntents.Remove(IntentName(intent));
                effect = OwnedSkillRenameEffect.NativeReadbackVerified;
                Checkpoint?.Invoke("rename-intent-retired");
                RequireCurrent(eligible, token);
                return readback;
            }
            catch (Exception exception) when (effect is not null && exception is not OutOfMemoryException)
            { throw new OwnedSkillRenameUnconfirmedException(effect.Value, exception); }
        }
    }

    private void RequireOriginal(OwnedSkillRename proposal, CancellationToken token)
    {
        var old = proposal.Original.Document;
        var parent = ExactChild(directory.Root, old.Id, SharedSkillCatalogue.MaximumPackages + 1)
            ?? throw new InvalidDataException("The original parent is absent.");
        var path = ExactChild(parent, old.Version, SharedSkillCatalogue.MaximumPackages)
            ?? throw new InvalidDataException("The original package is absent.");
        if (!proposal.Original.IsSameRevision(ReadPackage(path, token)))
        { throw new InvalidOperationException("The original package identity or complete bytes changed. Start a fresh review."); }
    }

    private void RetireRenameSource(OwnedSkillRename proposal, bool recovery, Func<bool> eligible, CancellationToken token)
    {
        var old = proposal.Original.Document;
        var parent = Path.Combine(directory.Root, old.Id);
        var source = ExactChild(parent, old.Version, SharedSkillCatalogue.MaximumPackages);
        if (source is null)
        {
            if (!recovery) { throw new InvalidDataException("The original source disappeared before retirement."); }
            return;
        }
        using var pins = new OwnedSkillPackagePublication();
        var handle = pins.PinDirectory(source, forRename: true);
        directory.VerifyOwnedSkillDirectory(source);
        if (!string.Equals(WindowsFileIdentity.Read(handle), proposal.Original.DirectoryIdentity, StringComparison.Ordinal))
        { throw new InvalidDataException("The original package directory was replaced. No external package was deleted."); }
        var names = Directory.EnumerateFileSystemEntries(source).Take(3).Select(Path.GetFileName).ToArray();
        if ((!recovery && names.Length != 2) || names.Any(name => name is not ("manifest.yaml" or "instructions.md")))
        { throw new InvalidDataException("Unexpected source members were retained, not recursively deleted."); }
        var files = new List<SafeFileHandle>();
        try
        {
            foreach (var name in names)
            {
                var path = Path.Combine(source, name!);
                var file = OwnedSkillPackagePublication.OpenForDeletion(path);
                files.Add(file);
                directory.VerifyFile(path);
                var manifest = name is "manifest.yaml";
                if (!string.Equals(WindowsFileIdentity.Read(file),
                    manifest ? proposal.Original.ManifestIdentity : proposal.Original.InstructionsIdentity, StringComparison.Ordinal)
                    || !ReadBytes(file, SharedSkillSnapshot.MaximumBytes, token).AsSpan()
                        .SequenceEqual((manifest ? old.ManifestBytes : old.InstructionBytes).Span))
                { throw new InvalidDataException("An original component changed. No substituted component was deleted."); }
            }
            Checkpoint?.Invoke("rename-before-retirement");
            foreach (var file in files)
            {
                RequireCurrent(eligible, token);
                OwnedSkillPackagePublication.MarkDeleted(file);
                file.Dispose();
                Checkpoint?.Invoke("rename-component-retired");
            }
            RequireCurrent(eligible, token);
            OwnedSkillPackagePublication.MarkDeleted(handle);
        }
        finally { foreach (var file in files) { file.Dispose(); } }
    }

    private static string IntentName(RenameIntent intent) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(intent.Proposal().Original.Document.Id + "\0" + intent.Proposal().Original.Document.Version))) + ".json";

    private static bool Affects(RenameIntent intent, string id, string version)
    {
        var original = intent.Proposal().Original.Document;
        return string.Equals(original.Version, version, StringComparison.Ordinal)
            && (string.Equals(original.Id, id, StringComparison.Ordinal) || string.Equals(intent.NewId, id, StringComparison.Ordinal));
    }

    private void RequireNoPendingRename(string id, string version, CancellationToken token)
    {
        if (ReadRenameIntents(token).Any(intent => Affects(intent, id, version)))
        { throw new InvalidOperationException("This identity has retained rename intent. Use List pending / Recover rename; use and publication remain unavailable."); }
    }

    private RenameIntent[] ReadRenameIntents(CancellationToken token)
    {
        try { _ = File.GetAttributes(RenameDirectory); }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            if (renameDirectoryIdentity is not null || observedRenameIntents.Count != 0)
            { throw new InvalidDataException("Previously observed private rename storage disappeared. No held identity was readmitted.", exception); }
            return [];
        }
        using var pinned = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(RenameDirectory, false, token);
        renameStorage.Verify();
        if (renameDirectoryIdentity is not null && !string.Equals(renameDirectoryIdentity, pinned.Identity, StringComparison.Ordinal))
        { throw new InvalidDataException("The private rename storage identity changed. Explicit inspection is required."); }
        renameDirectoryIdentity = pinned.Identity;
        var entries = Directory.EnumerateFileSystemEntries(RenameDirectory).Take(33).ToArray();
        if (entries.Length > 32) { throw new InvalidDataException("The private rename intent inventory exceeds its bound."); }
        if (!observedRenameIntents.IsSubsetOf(entries.Select(path => Path.GetFileName(path))))
        { throw new InvalidDataException("Previously observed rename intent disappeared. No held identity was readmitted."); }
        return entries.Select(path =>
        {
            var file = WindowsOwnedDefinitionFileAccess.ReadDefinitionFile(path, MaximumRenameBytes, token)
                ?? throw new InvalidDataException("A retained rename intent disappeared.");
            RenameIntent intent;
            try
            {
                intent = JsonSerializer.Deserialize<RenameIntent>(file.Bytes, RenameJson)
                    ?? throw new InvalidDataException("A retained rename intent is empty.");
            }
            catch (JsonException exception) { throw new InvalidDataException("A retained rename intent is malformed.", exception); }
            if (intent.Format != 1 || intent.Manifest is null || intent.Instructions is null
                || new[] { intent.NewId, intent.Root, intent.SourceParent, intent.DestinationParent, intent.SourceDirectory,
                    intent.SourceManifest, intent.SourceInstructions, intent.DestinationDirectory, intent.DestinationManifest,
                    intent.DestinationInstructions }.Any(string.IsNullOrWhiteSpace)
                || !string.Equals(intent.Root, rootIdentity, StringComparison.Ordinal)
                || !string.Equals(Path.GetFileName(path), IntentName(intent), StringComparison.Ordinal)
                || !JsonSerializer.SerializeToUtf8Bytes(intent, RenameJson).AsSpan().SequenceEqual(file.Bytes))
            { throw new InvalidDataException("The private rename intent is noncanonical, corrupt or bound to another root."); }
            _ = intent.Proposal();
            observedRenameIntents.Add(Path.GetFileName(path));
            return intent with { FileIdentity = file.Identity };
        }).ToArray();
    }

    private void RequireIntentCurrent(RenameIntent expected, CancellationToken token)
    {
        var actual = ReadRenameIntents(token).SingleOrDefault(intent => string.Equals(IntentName(intent), IntentName(expected), StringComparison.Ordinal));
        if (actual is null || !string.Equals(actual.FileIdentity, expected.FileIdentity, StringComparison.Ordinal)
            || !JsonSerializer.SerializeToUtf8Bytes(actual, RenameJson).AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(expected, RenameJson)))
        { throw new InvalidDataException("Retained rename intent changed. Explicit inspection is required."); }
    }

    private RenameIntent WriteRenameIntent(RenameIntent intent, RenameIntent? expected, Func<bool> eligible, CancellationToken token)
    {
        using var pinned = WindowsOwnedDefinitionFileAccess.PinDefinitionDirectory(RenameDirectory, true, token);
        renameStorage.Verify();
        if (expected is null && ReadRenameIntents(token).Length >= 32)
        { throw new InvalidDataException("Resolve an existing rename before creating another retained intent."); }
        if (expected is not null) { RequireIntentCurrent(expected, token); }
        var bytes = JsonSerializer.SerializeToUtf8Bytes(intent, RenameJson);
        if (bytes.Length > MaximumRenameBytes) { throw new InvalidDataException("The complete private rename intent exceeds its bound."); }
        observedRenameIntents.Add(IntentName(intent));
        WindowsOwnedDefinitionFileAccess.WriteDefinitionFile(RenameDirectory, pinned, IntentName(intent), bytes,
            expected is not null, eligible, token);
        var saved = ReadRenameIntents(token).Single(value => string.Equals(IntentName(value), IntentName(intent), StringComparison.Ordinal));
        if (!JsonSerializer.SerializeToUtf8Bytes(saved, RenameJson).AsSpan().SequenceEqual(bytes))
        { throw new IOException("Private rename intent readback is unconfirmed."); }
        return saved;
    }
}
