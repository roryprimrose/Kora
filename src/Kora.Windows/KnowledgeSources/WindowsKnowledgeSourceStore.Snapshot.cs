using System.Security.Cryptography;
using System.Text.Json;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;
using Kora.Windows.Storage;

namespace Kora.Windows.KnowledgeSources;

public sealed partial class WindowsKnowledgeSourceStore
{
    private static byte[] Encode(KnowledgeSourceRevision snapshot, IReadOnlyList<ReadOnlyMemory<byte>> originals,
        CancellationToken token)
    {
        if (originals.Count != snapshot.Files.Count)
        { throw new InvalidDataException("Every exact snapshot item requires its original bytes."); }
        var files = new List<FileData>();
        byte[]? plaintext = null;
        byte[]? compressed = null;
        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            for (var index = 0; index < snapshot.Files.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var file = snapshot.Files[index];
                _ = LocalFileRevision.Restore(file.Review, originals[index].Span, file.AdmittedAt, file.Reference);
                files.Add(new(file.Review.Metadata, file.Reference, file.AdmittedAt, originals[index].ToArray()));
            }
            plaintext = JsonSerializer.SerializeToUtf8Bytes(new SnapshotData(4, snapshot.Review.SourceId,
                snapshot.Review.ReviewId, snapshot.Review.Metadata.CanonicalPath,
                snapshot.Review.Metadata.DirectoryIdentity, snapshot.Review.Request, files.ToArray(),
                snapshot.Review.Metadata.Scope, snapshot.Review.Metadata.Directories.ToArray(), snapshot.Reference,
                snapshot.Indexes!.Select(index => new IndexData(LocalFileLexicalIndex.Version, index.PolicyVersion,
                    index.Digest, index.Chunks.ToArray())).ToArray(), snapshot.Review.Metadata.CaptureLimits), Json);
            compressed = Compress(plaintext, token);
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ciphertext = new byte[compressed.Length];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, compressed, ciphertext, tag, snapshot.Review.SourceId.ToByteArray());
            var envelope = JsonSerializer.SerializeToUtf8Bytes(new Envelope(2, CurrentUserKeyProtection.Protect(key),
                nonce, tag, ciphertext), Json);
            if (envelope.Length > MaximumSnapshotBytes)
            { throw new InvalidDataException("The whole stored snapshot exceeds its bound; no partial source is published."); }
            return envelope;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null) { CryptographicOperations.ZeroMemory(plaintext); }
            if (compressed is not null) { CryptographicOperations.ZeroMemory(compressed); }
            foreach (var file in files) { CryptographicOperations.ZeroMemory(file.Bytes); }
        }
    }

    private static KnowledgeSourceRevision Decode(KnowledgeSource current, byte[] bytes, CancellationToken token)
    {
        var envelope = Deserialize<Envelope>(System.Text.Encoding.UTF8.GetString(bytes));
        if (envelope.Version is not (1 or 2) || envelope.Nonce.Length != 12 || envelope.Tag.Length != 16)
        { throw new InvalidDataException("Unknown or malformed private snapshot envelope."); }
        var key = CurrentUserKeyProtection.Unprotect(envelope.Key);
        var plaintext = new byte[envelope.Ciphertext.Length];
        byte[]? expanded = null;
        SnapshotData? data = null;
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, current.Id.ToByteArray());
            expanded = envelope.Version == 2 ? Expand(plaintext, token) : plaintext;
            data = Deserialize<SnapshotData>(System.Text.Encoding.UTF8.GetString(expanded));
            if (data.Id != current.Id || data.ReviewId == Guid.Empty
                || !string.Equals(data.Path, current.Folder.CanonicalPath, StringComparison.Ordinal)
                || !string.Equals(data.Identity, current.Folder.DirectoryIdentity, StringComparison.Ordinal)
                || data.Files.Any(file => file is null)
                || !data.Files.Select(file => file.Metadata).SequenceEqual(current.Folder.Files)
                || data.Request.Origin != RequestOrigin.LocalUi || data.Request.InvocationId is not null
                || (data.Version == 1 ? data.Reference is not null : data.Reference is null)
                || (data.Version is 3 or 4 ? envelope.Version != 2 || data.Indexes is null : data.Indexes is not null))
            { throw new InvalidDataException("Private source metadata and exact snapshot membership disagree."); }
            var metadata = ReadMetadata(data.Version switch { 3 => 2, 4 => 3, _ => data.Version }, data.Path, data.Identity,
                data.Files.Select(file => file.Metadata).ToArray(), data.Scope, data.Directories, data.CaptureLimits);
            if (!metadata.Matches(current.Folder))
            { throw new InvalidDataException("The retained traversal policy or directory inventory disagrees with its registry."); }
            var review = new KnowledgeSourceReview(data.ReviewId, data.Id, data.Request, metadata);
            var files = data.Files.Select(file =>
            {
                token.ThrowIfCancellationRequested();
                return LocalFileRevision.Restore(new(data.ReviewId, data.Id, data.Request, file.Metadata),
                    file.Bytes, file.AdmittedAt, file.Reference);
            }).ToArray();
            if (data.Indexes is not null && data.Indexes.Length != files.Length)
            { throw new InvalidDataException("The complete snapshot/index membership disagrees."); }
            var indexes = data.Indexes?.Select((index, position) =>
                index is null ? throw new InvalidDataException("A lexical index is missing.")
                    : LocalFileLexicalIndex.Restore(files[position], index.Version, index.PolicyVersion,
                        index.Digest, index.Chunks, token)).ToArray();
            return new(review, files, data.Reference, indexes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
            if (expanded is not null) { CryptographicOperations.ZeroMemory(expanded); }
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
        KnowledgeSourceDirectory[]? Directories = null, KnowledgeSourceReference? Reference = null,
        IndexData[]? Indexes = null, KnowledgeSourceCaptureLimits? CaptureLimits = null);
    private sealed record FileData(LocalFileMetadata Metadata, LocalFileReference Reference,
        DateTimeOffset AdmittedAt, byte[] Bytes);
}
