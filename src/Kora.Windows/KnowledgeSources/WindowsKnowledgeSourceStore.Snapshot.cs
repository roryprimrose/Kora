using System.Security.Cryptography;
using System.Text.Json;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Windows.Storage;

namespace Kora.Windows.KnowledgeSources;

public sealed partial class WindowsKnowledgeSourceStore
{
    private static byte[] Encode(KnowledgeSourceRevision snapshot, IReadOnlyList<ReadOnlyMemory<byte>> originals)
    {
        if (originals.Count != snapshot.Files.Count)
        { throw new InvalidDataException("Every exact snapshot item requires its original bytes."); }
        var files = new List<FileData>();
        byte[]? plaintext = null;
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            for (var index = 0; index < snapshot.Files.Count; index++)
            {
                var file = snapshot.Files[index];
                _ = LocalFileRevision.Restore(file.Review, originals[index].Span, file.AdmittedAt, file.Reference);
                files.Add(new(file.Review.Metadata, file.Reference, file.AdmittedAt, originals[index].ToArray()));
            }
            plaintext = JsonSerializer.SerializeToUtf8Bytes(new SnapshotData(2, snapshot.Review.SourceId,
                snapshot.Review.ReviewId, snapshot.Review.Metadata.CanonicalPath,
                snapshot.Review.Metadata.DirectoryIdentity, snapshot.Review.Request, files.ToArray(),
                snapshot.Review.Metadata.Scope, snapshot.Review.Metadata.Directories.ToArray(), snapshot.Reference), Json);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ciphertext = new byte[plaintext.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, snapshot.Review.SourceId.ToByteArray());
            var envelope = JsonSerializer.SerializeToUtf8Bytes(new Envelope(1, CurrentUserKeyProtection.Protect(key),
                nonce, tag, ciphertext), Json);
            if (envelope.Length > MaximumSnapshotBytes)
            { throw new InvalidDataException("The whole stored snapshot exceeds its bound; no partial source is published."); }
            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null) { CryptographicOperations.ZeroMemory(plaintext); }
            foreach (var file in files) { CryptographicOperations.ZeroMemory(file.Bytes); }
        }
    }

    private static KnowledgeSourceRevision Decode(KnowledgeSource current, byte[] bytes)
    {
        var envelope = Deserialize<Envelope>(System.Text.Encoding.UTF8.GetString(bytes));
        if (envelope.Version != 1 || envelope.Nonce.Length != 12 || envelope.Tag.Length != 16)
        { throw new InvalidDataException("Unknown or malformed private snapshot envelope."); }
        var key = CurrentUserKeyProtection.Unprotect(envelope.Key);
        var plaintext = new byte[envelope.Ciphertext.Length];
        SnapshotData? data = null;
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, current.Id.ToByteArray());
            data = Deserialize<SnapshotData>(System.Text.Encoding.UTF8.GetString(plaintext));
            if (data.Id != current.Id || data.ReviewId == Guid.Empty
                || !string.Equals(data.Path, current.Folder.CanonicalPath, StringComparison.Ordinal)
                || !string.Equals(data.Identity, current.Folder.DirectoryIdentity, StringComparison.Ordinal)
                || data.Files.Any(file => file is null)
                || !data.Files.Select(file => file.Metadata).SequenceEqual(current.Folder.Files)
                || data.Request.Origin != RequestOrigin.LocalUi || data.Request.InvocationId is not null
                || (data.Version == 1 ? data.Reference is not null : data.Reference is null))
            { throw new InvalidDataException("Private source metadata and exact snapshot membership disagree."); }
            var metadata = ReadMetadata(data.Version, data.Path, data.Identity,
                data.Files.Select(file => file.Metadata).ToArray(), data.Scope, data.Directories);
            if (!metadata.Matches(current.Folder))
            { throw new InvalidDataException("The retained traversal policy or directory inventory disagrees with its registry."); }
            var review = new KnowledgeSourceReview(data.ReviewId, data.Id, data.Request, metadata);
            var files = data.Files.Select(file => LocalFileRevision.Restore(
                new(data.ReviewId, data.Id, data.Request, file.Metadata), file.Bytes, file.AdmittedAt, file.Reference));
            return new(review, files, data.Reference);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
            if (data is not null)
            {
                foreach (var file in data.Files)
                {
                    if (file is not null) { CryptographicOperations.ZeroMemory(file.Bytes); }
                }
            }
        }
    }

    private sealed record Envelope(int Version, byte[] Key, byte[] Nonce, byte[] Tag, byte[] Ciphertext);
    private sealed record SnapshotData(int Version, Guid Id, Guid ReviewId, string Path, string Identity,
        HostRequest Request, FileData[] Files, KnowledgeSourceScope? Scope = null,
        KnowledgeSourceDirectory[]? Directories = null, KnowledgeSourceReference? Reference = null);
    private sealed record FileData(LocalFileMetadata Metadata, LocalFileReference Reference,
        DateTimeOffset AdmittedAt, byte[] Bytes);
}
