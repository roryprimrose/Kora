using System.Security.Cryptography;
using System.Text;
using Kora.Core.Skills;
using Kora.Windows.Storage;

namespace Kora.Windows.Skills;

public sealed partial class WindowsOwnedDeclarativeSkillStore
{
    private readonly HashSet<string> deletionHolds = new(StringComparer.Ordinal);

    public Task<OwnedSkillDeletionSnapshot> PrepareDeletion(string id, string version, CancellationToken token) =>
        Task.Run(() =>
        {
            OwnedDeclarativeSkillDocument.RequireIdentity(id, version);
            lock (gate)
            {
                using var pins = PinRoot(token, requireExisting: true);
                using var lease = directory.AcquireBoundedLease(requireExisting: true, token);
                var target = Path.Combine(directory.Root, id, version);
                if (deletionHolds.Contains(target))
                { throw new InvalidOperationException("This source has a retired or unconfirmed deletion. Inspect/recover it explicitly; automatic retry is unavailable."); }
                var parent = ExactChild(directory.Root, id, SharedSkillCatalogue.MaximumPackages + 1)
                    ?? throw new InvalidDataException("The exact owned skill parent is absent; absence is not a deletion receipt.");
                var parentHandle = pins.PinDirectory(parent);
                directory.VerifyOwnedSkillDirectory(parent);
                var selected = ExactChild(parent, version, SharedSkillCatalogue.MaximumPackages)
                    ?? throw new InvalidDataException("The exact owned skill source is absent; absence is not a deletion receipt.");
                var saved = ReadPackage(selected, token);
                if (!string.Equals(saved.Document.Id, id, StringComparison.Ordinal)
                    || !string.Equals(saved.Document.Version, version, StringComparison.Ordinal) || saved.Document.ObservationWorkflow is not null)
                { throw new InvalidDataException("Deletion supports only exact native owned v1/v2/v3 sources, never reference/observation packages or other sources."); }
                return new OwnedSkillDeletionSnapshot(saved, target, rootIdentity!, WindowsFileIdentity.Read(parentHandle), CatalogueIdentity(token));
            }
        }, token);

    public Task ValidateDeletion(OwnedSkillDeletionSnapshot expected, CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            using var pins = PinRoot(token, requireExisting: true);
            using var lease = directory.AcquireBoundedLease(requireExisting: true, token);
            ValidateDeletionCore(expected, pins, token);
        }
    }, token);

    private void ValidateDeletionCore(OwnedSkillDeletionSnapshot expected, OwnedSkillPackagePublication pins, CancellationToken token)
    {
        var source = expected.Source;
        if (source.Document.ObservationWorkflow is not null || deletionHolds.Contains(expected.Destination)
            || !string.Equals(expected.Destination, Destination(source.Document), StringComparison.Ordinal)
            || !string.Equals(rootIdentity, expected.RootIdentity, StringComparison.Ordinal))
        { throw new InvalidDataException("The exact owned deletion destination is changed or held."); }
        var parent = Path.GetDirectoryName(expected.Destination)!;
        var parentHandle = pins.PinDirectory(parent);
        directory.VerifyOwnedSkillDirectory(parent);
        if (!string.Equals(WindowsFileIdentity.Read(parentHandle), expected.ParentIdentity, StringComparison.Ordinal)
            || !string.Equals(CatalogueIdentity(token), expected.CatalogueIdentity, StringComparison.Ordinal)
            || !source.IsSameRevision(ReadPackage(expected.Destination, token)))
        { throw new InvalidDataException("The owned source, parent or catalogue changed. Review again; nothing was deleted."); }
    }

    public Task Delete(OwnedSkillDeletionSnapshot expected, Func<bool> eligible,
        Func<Func<bool>, Task<bool>> commit, CancellationToken token) =>
        Task.Run(async () => await commit(() => DeleteCore(expected, eligible, token)).ConfigureAwait(false), token);

    private bool DeleteCore(OwnedSkillDeletionSnapshot expected, Func<bool> eligible, CancellationToken token)
    {
        lock (gate)
        {
            using var pins = PinRoot(token, requireExisting: true);
            using var lease = directory.AcquireBoundedLease(requireExisting: true, token);
            RequireCurrent(eligible, token);
            ValidateDeletionCore(expected, pins, token);
            using var package = new OwnedSkillPackagePublication();
            var manifestPath = Path.Combine(expected.Destination, "manifest.yaml");
            var instructionsPath = Path.Combine(expected.Destination, "instructions.md");
            using var manifest = OwnedSkillPackagePublication.OpenForDeletion(manifestPath);
            using var instructions = OwnedSkillPackagePublication.OpenForDeletion(instructionsPath);
            Checkpoint?.Invoke("before-delete");
            RequireCurrent(eligible, token);
            ValidateDeletionCore(expected, pins, token);
            var handle = package.PinDirectory(expected.Destination, forRename: true);
            OwnedSkillPackagePublication.Verify(handle, expected.Destination, directory: true);
            OwnedSkillPackagePublication.Verify(manifest, manifestPath, directory: false);
            OwnedSkillPackagePublication.Verify(instructions, instructionsPath, directory: false);
            directory.VerifyFile(manifestPath);
            directory.VerifyFile(instructionsPath);
            if (!string.Equals(WindowsFileIdentity.Read(handle), expected.Source.DirectoryIdentity, StringComparison.Ordinal)
                || !string.Equals(WindowsFileIdentity.Read(manifest), expected.Source.ManifestIdentity, StringComparison.Ordinal)
                || !string.Equals(WindowsFileIdentity.Read(instructions), expected.Source.InstructionsIdentity, StringComparison.Ordinal))
            { throw new InvalidDataException("The exact deletion file identities changed."); }
            var observed = false;
            try
            {
                RequireCurrent(eligible, token);
                OwnedSkillPackagePublication.MarkDeleted(manifest);
                observed = true;
                deletionHolds.Add(expected.Destination);
                manifest.Dispose();
                Checkpoint?.Invoke("after-delete-file");
                RequireCurrent(eligible, token);
                directory.Verify();
                directory.VerifyOwnedSkillDirectory(expected.Destination);
                directory.VerifyFile(instructionsPath);
                OwnedSkillPackagePublication.Verify(instructions, instructionsPath, directory: false);
                OwnedSkillPackagePublication.MarkDeleted(instructions);
                instructions.Dispose();
                OwnedSkillPackagePublication.MarkDeleted(handle);
                package.Dispose();
                Checkpoint?.Invoke("after-delete");
                RequireCurrent(eligible, token);
                if (ExactChild(Path.GetDirectoryName(expected.Destination)!, expected.Source.Document.Version,
                    SharedSkillCatalogue.MaximumPackages) is not null)
                { throw new IOException("Owned skill deletion absence could not be verified."); }
                return true;
            }
            catch (Exception exception) when (observed && exception is not OutOfMemoryException)
            { throw new OwnedSkillDeletionUnconfirmedException(exception); }
        }
    }

    private string CatalogueIdentity(CancellationToken token)
    {
        using var pins = new OwnedSkillPackagePublication();
        var catalogue = Catalogue(pins, token);
        var entries = new List<string>();
        foreach (var parent in Directory.EnumerateFileSystemEntries(directory.Root).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(parent) is "operation.lock") { continue; }
            token.ThrowIfCancellationRequested();
            entries.Add(Path.GetFileName(parent) + ":" + WindowsFileIdentity.Read(pins.PinDirectory(parent)));
            foreach (var child in Directory.EnumerateFileSystemEntries(parent).Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                entries.Add(Path.GetFileName(child) + ":" + WindowsFileIdentity.Read(pins.PinDirectory(child)));
            }
        }
        var identity = string.Join('\n', entries) + "\n" + string.Join('\n', catalogue.Select(source =>
            source.Document.Id + "/" + source.Document.Version + ":" + source.DirectoryIdentity + ":"
            + source.ManifestIdentity + ":" + source.InstructionsIdentity + ":" + source.Document.RevisionDigest));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }
}
