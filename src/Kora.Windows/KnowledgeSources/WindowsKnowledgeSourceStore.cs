using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Context;
using Kora.Core.Dependencies;
using Kora.Core.KnowledgeSources;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.KnowledgeSources;

public sealed partial class WindowsKnowledgeSourceStore(IApplicationDataPaths paths) : IKnowledgeSourceStore
{
    internal ISqliteTransactionCheckpoint? TransactionCheckpoint { get; init; }
    private const int MaximumSources = 16;
    private const int MaximumSnapshotBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };
    private readonly RestrictedSqliteDatabase database = new(paths, "KnowledgeSourcesV1", "sources.db", 1263424305,
    [
        """
        CREATE TABLE knowledge_sources(
            id TEXT PRIMARY KEY NOT NULL,
            version INTEGER NOT NULL CHECK(version>0),
            info TEXT NOT NULL CHECK(length(CAST(info AS BLOB)) BETWEEN 1 AND 65536),
            snapshot BLOB CHECK(length(snapshot) BETWEEN 1 AND 2097152)) STRICT
        """,
    ]);

    public Task<IReadOnlyList<KnowledgeSource>> ListAsync(CancellationToken cancellationToken) =>
        Run<IReadOnlyList<KnowledgeSource>>(connection => List(connection), cancellationToken);

    public Task<KnowledgeSource> BeginRefreshAsync(KnowledgeSource exact, CancellationToken cancellationToken) =>
        Run(connection =>
        {
            var current = Require(connection, exact);
            if (current.State == KnowledgeSourceState.Removing)
            { throw new InvalidOperationException("Finish removing the inventoried source before adding it again."); }
            var next = current with { Version = checked(current.Version + 1), State = KnowledgeSourceState.Unavailable,
                ChangedAt = DateTimeOffset.UtcNow };
            Update(connection, next, null, replaceSnapshot: false, cancellationToken);
            return next;
        }, cancellationToken);

    public Task<KnowledgeSource> PublishAsync(KnowledgeSource? expected, string name, KnowledgeSourceRevision snapshot,
        IReadOnlyList<ReadOnlyMemory<byte>> originalBytes, CancellationToken cancellationToken) =>
        Run(connection =>
        {
            var current = expected is null ? null : Require(connection, expected);
            var sources = List(connection);
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl)
                || sources.Any(item => item.Id != snapshot.Review.SourceId && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                || (current is null && sources.Count >= MaximumSources)
                || (current is not null && (current.State != KnowledgeSourceState.Unavailable || current.Id != snapshot.Review.SourceId
                    || !string.Equals(current.Folder.CanonicalPath, snapshot.Review.Metadata.CanonicalPath, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(current.Folder.DirectoryIdentity, snapshot.Review.Metadata.DirectoryIdentity, StringComparison.Ordinal)
                    || current.Folder.Scope != snapshot.Review.Metadata.Scope)))
            { throw new InvalidDataException("Named source publication requires unique bounded identity, exact root and unavailable previous revision."); }
            var bytes = Encode(snapshot, originalBytes);
            try
            {
                var next = new KnowledgeSource(snapshot.Review.SourceId, checked((current?.Version ?? 0) + 1), name,
                    snapshot.Review.Metadata, KnowledgeSourceState.Enabled, DateTimeOffset.UtcNow, bytes.Length);
                Update(connection, next, bytes, replaceSnapshot: true, cancellationToken, insert: current is null);
                return next;
            }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }, cancellationToken);

    public Task<KnowledgeSourceRevision> ReadAsync(KnowledgeSource exact, CancellationToken cancellationToken) =>
        Run(connection =>
        {
            var current = Require(connection, exact);
            if (current.State != KnowledgeSourceState.Enabled)
            { throw new InvalidOperationException("This source is disabled, refreshing or awaiting removal; no older snapshot is eligible."); }
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT snapshot FROM knowledge_sources WHERE id=$id";
            command.Parameters.AddWithValue("$id", current.Id.ToString("D"));
            var bytes = command.ExecuteScalar() as byte[]
                ?? throw new InvalidDataException("The committed source snapshot is missing.");
            if (bytes.Length != current.StoredBytes) { throw new InvalidDataException("Source inventory and body disagree."); }
            return Decode(current, bytes);
        }, cancellationToken);

    public Task DisableAsync(KnowledgeSource exact, CancellationToken cancellationToken) =>
        Run(connection =>
        {
            var current = Require(connection, exact);
            if (current.State == KnowledgeSourceState.Removing)
            { throw new InvalidOperationException("The source is already revoked for removal."); }
            Update(connection, current with { Version = checked(current.Version + 1),
                State = KnowledgeSourceState.Disabled, ChangedAt = DateTimeOffset.UtcNow },
                null, replaceSnapshot: false, cancellationToken);
            return true;
        }, cancellationToken);

    public Task RemoveAsync(KnowledgeSource exact, CancellationToken cancellationToken) =>
        Run(connection =>
        {
            var current = Require(connection, exact);
            // Persist revocation before deletion. Interrupted cleanup remains visible and retryable.
            var removing = current with { State = KnowledgeSourceState.Removing, Version = checked(current.Version + 1),
                ChangedAt = DateTimeOffset.UtcNow };
            Update(connection, removing, null, replaceSnapshot: false, cancellationToken);
            using var transaction = connection.BeginTransaction();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE knowledge_sources SET snapshot=NULL WHERE id=$id";
            command.Parameters.AddWithValue("$id", current.Id.ToString("D"));
            TransactionCheckpoint?.BeforeWrite(connection, transaction);
            command.ExecuteNonQuery();
            TransactionCheckpoint?.BeforeCommit(connection, transaction);
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return current.Id;
        }, cancellationToken, finishRemoval: exact.Id);

    private Task<T> Run<T>(Func<SqliteConnection, T> action, CancellationToken token, Guid? finishRemoval = null) =>
        Task.Run(() =>
        {
            using var operation = new StorageOperation("knowledge.storage", token);
            using var lease = database.AcquireLease(out var created, token);
            using var storageIdentity = HoldStorageIdentity(created);
            T result;
            using (var connection = database.Open(created, RestrictedSqliteDatabase.DefaultProgressTimeout, token))
            {
                using var settings = connection.CreateCommand();
                settings.CommandText = "PRAGMA secure_delete=ON;";
                settings.ExecuteNonQuery();
                database.RequireEmptyArtifactInventory();
                VerifyStorageInventory();
                result = action(connection);
            }
            storageIdentity?.Validate();
            storageIdentity?.Dispose();
            database.ClearCommittedJournal();
            database.RequireEmptyJournal();
            if (finishRemoval is { } removedId)
            {
                // Delete metadata only after body/journal cleanup. No arbitrary paths or directory deletes.
                using (var removalIdentity = HoldStorageIdentity(created: false))
                using (var connection = database.Open(false, RestrictedSqliteDatabase.DefaultProgressTimeout, token))
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = "PRAGMA secure_delete=ON; DELETE FROM knowledge_sources WHERE id=$id AND snapshot IS NULL";
                    command.Parameters.AddWithValue("$id", removedId.ToString("D"));
                    token.ThrowIfCancellationRequested();
                    command.ExecuteNonQuery();
                    removalIdentity!.Validate();
                }
                database.ClearCommittedJournal();
                database.RequireEmptyJournal();
            }
            token.ThrowIfCancellationRequested();
            operation.Complete();
            return result;
        }, token);

    private static System.Collections.ObjectModel.ReadOnlyCollection<KnowledgeSource> List(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id,version,info,length(snapshot) FROM knowledge_sources ORDER BY id LIMIT 17";
        using var reader = command.ExecuteReader();
        var items = new List<KnowledgeSource>();
        while (reader.Read())
        {
            if (items.Count >= MaximumSources) { throw new InvalidDataException("The source inventory exceeds its bound."); }
            var dto = Deserialize<SourceData>(reader.GetString(2));
            var folder = ReadMetadata(dto.FormatVersion, dto.Path, dto.Identity, dto.Files, dto.Scope, dto.Directories);
            if (dto.Id == Guid.Empty || dto.Version <= 0 || !string.Equals(dto.Id.ToString("D"), reader.GetString(0), StringComparison.Ordinal)
                || dto.Version != reader.GetInt64(1) || !Enum.IsDefined(dto.State)
                || string.IsNullOrWhiteSpace(dto.Name) || dto.Name.Length > 80 || dto.Name.Any(char.IsControl)
                || dto.StoredBytes is <= 0 or > MaximumSnapshotBytes
                || (reader.IsDBNull(3) ? dto.State != KnowledgeSourceState.Removing : reader.GetInt32(3) != dto.StoredBytes))
            { throw new InvalidDataException("The durable source inventory is invalid."); }
            items.Add(new(dto.Id, dto.Version, dto.Name, folder, dto.State, dto.ChangedAt, dto.StoredBytes));
        }
        return items.AsReadOnly();
    }

    private static KnowledgeSource Require(SqliteConnection connection, KnowledgeSource exact)
    {
        var current = List(connection).SingleOrDefault(item => item.Id == exact.Id);
        if (current is null || current.Version != exact.Version)
        { throw new InvalidOperationException("The exact source inventory changed; reload and review it again."); }
        return current;
    }

    private void Update(SqliteConnection connection, KnowledgeSource source, byte[]? snapshot,
        bool replaceSnapshot, CancellationToken token, bool insert = false)
    {
        var info = JsonSerializer.Serialize(new SourceData(source.Id, source.Version, source.Name,
            source.Folder.CanonicalPath, source.Folder.DirectoryIdentity, source.Folder.Files.ToArray(),
            source.State, source.ChangedAt, source.StoredBytes, 2, source.Folder.Scope, source.Folder.Directories.ToArray()), Json);
        if (System.Text.Encoding.UTF8.GetByteCount(info) > 65536)
        { throw new InvalidDataException("The whole named-source metadata exceeds its existing storage bound."); }
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = insert
            ? "INSERT INTO knowledge_sources(id,version,info,snapshot) VALUES($id,$version,$info,$snapshot)"
            : replaceSnapshot
                ? "UPDATE knowledge_sources SET version=$version,info=$info,snapshot=$snapshot WHERE id=$id"
                : "UPDATE knowledge_sources SET version=$version,info=$info WHERE id=$id";
        command.Parameters.AddWithValue("$id", source.Id.ToString("D"));
        command.Parameters.AddWithValue("$version", source.Version);
        command.Parameters.AddWithValue("$info", info);
        if (replaceSnapshot) { command.Parameters.AddWithValue("$snapshot", snapshot!); }
        TransactionCheckpoint?.BeforeWrite(connection, transaction);
        if (command.ExecuteNonQuery() != 1) { throw new InvalidDataException("The source publication did not affect exactly one source."); }
        TransactionCheckpoint?.BeforeCommit(connection, transaction);
        token.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    private static T Deserialize<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidDataException("Source state is null."); }
        catch (JsonException exception) { throw new InvalidDataException("Source state is malformed.", exception); }
    }

    private sealed record SourceData(Guid Id, long Version, string Name, string Path, string Identity,
        LocalFileMetadata[] Files, KnowledgeSourceState State, DateTimeOffset ChangedAt, int StoredBytes,
        int FormatVersion = 1, KnowledgeSourceScope? Scope = null, KnowledgeSourceDirectory[]? Directories = null);

    private static KnowledgeSourceMetadata ReadMetadata(int version, string path, string identity, LocalFileMetadata[] files,
        KnowledgeSourceScope? scope, KnowledgeSourceDirectory[]? directories)
    {
        if (version == 1 && scope is null && directories is null)
        { return KnowledgeSourceMetadata.FromImmediate(new LocalFolderMetadata(path, identity, files)); }
        if (version == 2 && scope is not null && directories is not null)
        { return new(path, identity, scope.Value, directories, files); }
        throw new InvalidDataException("Unknown or malformed named-source metadata format; no legacy consent widening is permitted.");
    }
}
