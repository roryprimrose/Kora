using System.Globalization;
using System.Security.Cryptography;

using Kora.Core.Dependencies;

namespace Kora.Windows.Storage;

internal sealed class WindowsEncryptedArtifactStore
{
    internal const int MaximumReconciliationEntries = 256;
    internal const long MaximumReconciliationBytes = 64 * 1024 * 1024;
    private readonly RestrictedStorageDirectory directory;
    // The caller owns the key and must quiesce outstanding operations before disposing it.
    private readonly WindowsStorageKey key;
    private readonly IStoragePublicationCheckpoint? checkpoint;

    internal WindowsEncryptedArtifactStore(
        IApplicationDataPaths paths, WindowsStorageKey key, IStoragePublicationCheckpoint? checkpoint = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        directory = new RestrictedStorageDirectory(paths);
        this.key = key;
        this.checkpoint = checkpoint;
    }

    internal async Task<ArtifactReference> PublishAsync(
        ArtifactIdentity identity, ReadOnlyMemory<byte> plaintext, CancellationToken cancellationToken)
    {
        using var operation = new StorageOperation("storage.artifact.publish", cancellationToken);
        ArgumentNullException.ThrowIfNull(identity);
        cancellationToken.ThrowIfCancellationRequested();
        identity.Validate();
        if (plaintext.Length > ArtifactEnvelope.MaximumPlaintextBytes)
        {
            throw new InvalidDataException("The artifact exceeds its plaintext bound.");
        }
        using var lease = directory.AcquireLease();
        await WindowsStorageKeyStore.ValidateLoadedKeyAsync(directory, key, cancellationToken).ConfigureAwait(false);
        var published = GetPath(identity.ArtifactId, staged: false);
        var staging = GetPath(identity.ArtifactId, staged: true);
        if (File.Exists(published) || File.Exists(staging))
        {
            throw new InvalidDataException("The artifact identity already has a published or interrupted outcome.");
        }

        var envelope = ArtifactEnvelope.Encrypt(key, identity, plaintext.Span);
        var reference = new ArtifactReference(identity, key.Id, plaintext.Length,
            Convert.ToHexString(SHA256.HashData(plaintext.Span)));
        await StorageFilePublication.WriteStagingAsync(directory, staging, envelope,
            StoragePublicationKind.Artifact, checkpoint, cancellationToken).ConfigureAwait(false);
        StorageFilePublication.Publish(directory, staging, published);
        operation.Complete();
        return reference;
    }

    internal async Task<byte[]> ReadAsync(ArtifactReference reference, CancellationToken cancellationToken)
    {
        using var operation = new StorageOperation("storage.artifact.read", cancellationToken);
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        reference.Validate();
        using var lease = directory.AcquireLease();
        await WindowsStorageKeyStore.ValidateLoadedKeyAsync(directory, key, cancellationToken).ConfigureAwait(false);
        var envelope = await StorageFilePublication.ReadBoundedAsync(directory,
            GetPath(reference.Identity.ArtifactId, staged: false),
            ArtifactEnvelope.MaximumEnvelopeBytes, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var plaintext = ArtifactEnvelope.Decrypt(key, envelope, reference);
        operation.Complete();
        return plaintext;
    }

    internal async Task<ArtifactReconciliation> ReconcileAsync(
        IEnumerable<ArtifactReference> committedReferences, CancellationToken cancellationToken)
    {
        using var operation = new StorageOperation("storage.artifact.reconcile", cancellationToken);
        ArgumentNullException.ThrowIfNull(committedReferences);
        cancellationToken.ThrowIfCancellationRequested();
        var references = new Dictionary<Guid, ArtifactReference>();
        foreach (var reference in committedReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            reference.Validate();
            if (references.Count == MaximumReconciliationEntries
                || !references.TryAdd(reference.Identity.ArtifactId, reference))
            {
                throw new InvalidDataException("Artifact reconciliation references exceed the bound or repeat an identity.");
            }
        }

        using var lease = directory.AcquireLease();
        await WindowsStorageKeyStore.ValidateLoadedKeyAsync(directory, key, cancellationToken).ConfigureAwait(false);
        var entries = Directory.EnumerateFileSystemEntries(directory.Artifacts)
            .Take(MaximumReconciliationEntries + 1).ToArray();
        if (entries.Length > MaximumReconciliationEntries)
        {
            throw new InvalidDataException("Artifact reconciliation inventory exceeds its entry bound.");
        }

        var issues = new List<ArtifactRecoveryIssue>();
        var publishedIds = new HashSet<Guid>();
        long examinedBytes = 0;
        foreach (var path in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            directory.VerifyFile(path);
            var (id, staged) = ParseFileName(path);
            var length = new FileInfo(path).Length;
            if (length > MaximumReconciliationBytes - examinedBytes)
            {
                throw new InvalidDataException("Artifact reconciliation exceeds its total byte bound.");
            }
            examinedBytes += length;
            if (!staged)
            {
                publishedIds.Add(id);
            }
            references.TryGetValue(id, out var reference);
            try
            {
                var envelope = await StorageFilePublication.ReadBoundedAsync(directory, path,
                    ArtifactEnvelope.MaximumEnvelopeBytes, cancellationToken).ConfigureAwait(false);
                if (ArtifactEnvelope.ReadIdentity(envelope).ArtifactId != id)
                {
                    throw new InvalidDataException("The artifact file name and authenticated identity differ.");
                }
                var plaintext = ArtifactEnvelope.Decrypt(key, envelope, staged ? null : reference);
                CryptographicOperations.ZeroMemory(plaintext);
                if (staged || reference is null)
                {
                    issues.Add(new ArtifactRecoveryIssue(id,
                        staged ? ArtifactRecoveryKind.Staged : ArtifactRecoveryKind.Orphan));
                }
            }
            catch (InvalidDataException)
            {
                issues.Add(new ArtifactRecoveryIssue(id, ArtifactRecoveryKind.Corrupt));
            }
            catch (CryptographicException)
            {
                issues.Add(new ArtifactRecoveryIssue(id, ArtifactRecoveryKind.Corrupt));
            }
        }

        foreach (var id in references.Keys)
        {
            if (!publishedIds.Contains(id))
            {
                issues.Add(new ArtifactRecoveryIssue(id, ArtifactRecoveryKind.Missing));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        operation.Complete();
        // Recovery is observation only. It never publishes artifacts, deletes files, or replays actions.
        return new ArtifactReconciliation(issues.AsReadOnly(), entries.Length, examinedBytes);
    }

    private string GetPath(Guid id, bool staged)
    {
        var extension = staged ? ".pending" : ".gcm";
        return Path.Combine(directory.Artifacts, string.Concat(id.ToString("N", CultureInfo.InvariantCulture), extension));
    }

    private static (Guid Id, bool Staged) ParseFileName(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        if (extension is not (".pending" or ".gcm")
            || !Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out var id)
            || id == Guid.Empty)
        {
            throw new InvalidDataException("The artifact inventory contains an unknown managed file.");
        }
        return (id, string.Equals(extension, ".pending", StringComparison.Ordinal));
    }
}
