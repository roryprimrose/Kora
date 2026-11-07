using System.IO.Compression;
using System.Security.Cryptography;
using Kora.Core.Context;
using Kora.Core.KnowledgeSources;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.KnowledgeSources;

public sealed partial class WindowsKnowledgeSourceStore
{
    private const int MaximumExpandedSnapshotBytes = 128 * 1024 * 1024;
    private readonly List<ValidatedSnapshot> validatedSnapshots = [];
    internal int IndexReadValidations { get; private set; }

    private KnowledgeSourceRevision ReadSnapshot(SqliteConnection connection, KnowledgeSource current,
        CancellationToken token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT snapshot FROM knowledge_sources WHERE id=$id";
        command.Parameters.AddWithValue("$id", current.Id.ToString("D"));
        var bytes = command.ExecuteScalar() as byte[]
            ?? throw new InvalidDataException("The committed source snapshot is missing.");
        try
        {
            if (bytes.Length != current.StoredBytes) { throw new InvalidDataException("Source inventory and body disagree."); }
            token.ThrowIfCancellationRequested();
            var digest = Convert.ToHexString(SHA256.HashData(bytes));
            var retained = validatedSnapshots.SingleOrDefault(item => item.Revision.Reference.SourceId == current.Id
                && string.Equals(item.BodyDigest, digest, StringComparison.Ordinal)
                && item.Revision.Review.Metadata.Matches(current.Folder));
            if (retained is not null) { return retained.Revision; }
            RetireValidatedSnapshot(current.Id);
            var revision = Decode(current, bytes, token);
            IndexReadValidations++;
            RetainValidatedSnapshot(revision, digest);
            return revision;
        }

        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private void RetireValidatedSnapshot(Guid sourceId) =>
        validatedSnapshots.RemoveAll(item => item.Revision.Reference.SourceId == sourceId);

    private void RetainValidatedSnapshot(KnowledgeSourceRevision revision, string digest)
    {
        RetireValidatedSnapshot(revision.Reference.SourceId);
        if (validatedSnapshots.Count == 2) { validatedSnapshots.RemoveAt(0); }
        validatedSnapshots.Add(new(revision, digest));
    }

    private sealed record ValidatedSnapshot(KnowledgeSourceRevision Revision, string BodyDigest);

    private static byte[] Compress(byte[] plaintext, CancellationToken token)
    {
        if (plaintext.Length > MaximumExpandedSnapshotBytes)
        { throw new InvalidDataException("The complete expanded source/index exceeds its bound."); }
        using var output = new MemoryStream();
        try
        {
            using (var compressor = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (var offset = 0; offset < plaintext.Length; offset += 8192)
                {
                    token.ThrowIfCancellationRequested();
                    compressor.Write(plaintext, offset, Math.Min(8192, plaintext.Length - offset));
                }
            }
            token.ThrowIfCancellationRequested();
            return output.ToArray();
        }
        finally { CryptographicOperations.ZeroMemory(output.GetBuffer()); }
    }

    private static byte[] Expand(byte[] compressed, CancellationToken token)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var decompressor = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        try
        {
            int read;
            while ((read = decompressor.Read(buffer)) != 0)
            {
                token.ThrowIfCancellationRequested();
                if (output.Length + read > MaximumExpandedSnapshotBytes)
                { throw new InvalidDataException("The expanded source/index exceeds its bound."); }
                output.Write(buffer, 0, read);
            }
            token.ThrowIfCancellationRequested();
            return output.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            CryptographicOperations.ZeroMemory(output.GetBuffer());
        }
    }

    private sealed record IndexData(string Version, string PolicyVersion, string Digest, LocalFileLexicalIndex.Chunk[] Chunks);
}
