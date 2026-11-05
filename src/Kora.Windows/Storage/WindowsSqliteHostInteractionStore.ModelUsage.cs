using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore : ISessionModelMetricsStore
{
    private void MigrateModelUsage(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateAuthority(connection);
        token.ThrowIfCancellationRequested();
        Execute(connection, transaction, HostInteractionSchema.ModelUsageTable);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    public ValueTask RecordModelUsageAsync(SessionModelUsage usage, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(usage);
        usage.Validate();
        var request = usage.Provenance.Request;
        RequireLive(request);
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            using var transaction = connection.BeginTransaction();
            var source = RequireModelUsageSource(connection, request);
            var payload = HostInteractionCodec.Encode(usage);
            using var existing = connection.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = "SELECT payload FROM session_model_usage WHERE turn_id=$turn;";
            existing.Parameters.AddWithValue("$turn", Id(usage.Provenance.Turn));
            if (existing.ExecuteScalar() is string previous)
            {
                if (!Same(previous, payload))
                { throw new InvalidDataException("A model turn identity has conflicting retained usage."); }
                RequireLive(request);
                token.ThrowIfCancellationRequested();
                database.VerifyFiles();
                return;
            }
            Execute(connection, transaction, """
                INSERT INTO session_model_usage VALUES($turn,$request,$session,$task,$generation,$audit,$payload,$digest);
                """, ("$turn", Id(usage.Provenance.Turn)), ("$request", Id(request.RequestId)),
                ("$session", Id(request.SessionId)), ("$task", Id(request.TaskId)), ("$generation", source.Generation.Value),
                ("$audit", source.AuditSequence), ("$payload", payload), ("$digest", ModelUsageDigest(payload)));
            Commit(transaction, request, token, () =>
            {
                RequireLive(request);
                return true;
            });
        }, token));
    }

    public ValueTask<ImmutableArray<SessionModelUsage>> ReadModelUsageAsync(HostId<SessionIdentity> session, CancellationToken token)
    {
        session.Validate();
        return new(Task.Run(() =>
        {
            using var lease = database.AcquireReadLease(token);
            using var connection = Open(created: false, token);
            _ = RequireSession(connection, session);
            var result = ReadModelUsage(connection, session, token);
            database.VerifyFiles();
            token.ThrowIfCancellationRequested();
            return result;
        }, token));
    }

    private static void ValidateModelUsage(SqliteConnection connection) =>
        _ = ReadModelUsage(connection, null, CancellationToken.None);

    private static ImmutableArray<SessionModelUsage> ReadModelUsage(SqliteConnection connection,
        HostId<SessionIdentity>? session, CancellationToken token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT turn_id,request_id,session_id,task_id,generation,audit_sequence,payload,digest FROM session_model_usage"
            + (session is null ? ";" : " WHERE session_id=$id;");
        if (session is { } id) { command.Parameters.AddWithValue("$id", Id(id)); }
        using var reader = command.ExecuteReader();
        var result = ImmutableArray.CreateBuilder<SessionModelUsage>();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            var payload = reader.GetString(6);
            var usage = HostInteractionCodec.Decode<SessionModelUsage>(payload);
            usage.Validate();
            var request = usage.Provenance.Request;
            var source = RequireModelUsageSource(connection, request);
            if (!Same(reader.GetString(0), Id(usage.Provenance.Turn))
                || !Same(reader.GetString(1), Id(request.RequestId)) || !Same(reader.GetString(2), Id(request.SessionId))
                || !Same(reader.GetString(3), Id(request.TaskId)) || reader.GetInt64(4) != source.Generation.Value
                || reader.GetInt64(5) != source.AuditSequence || !Same(payload, HostInteractionCodec.Encode(usage))
                || !Same(reader.GetString(7), ModelUsageDigest(payload)))
            { throw new InvalidDataException("Retained model usage is corrupt or crosses exact host ownership."); }
            result.Add(usage);
        }
        return result.OrderBy(usage => usage.ObservedAt).ThenBy(usage => usage.Provenance.Turn.Value).ToImmutableArray();
    }

    private static (HostRevision Generation, long AuditSequence) RequireModelUsageSource(
        SqliteConnection connection, HostRequest request)
    {
        var session = RequireSession(connection, request.SessionId);
        if (session.State == 2 || ReadRetention(connection, request.SessionId).Purged)
        { throw new InvalidDataException("Removed session model usage cannot be retained or restored."); }
        var task = ReadTask(connection, request.TaskId);
        if (task?.Request != request)
        { throw new InvalidDataException("Model usage requires the exact retained original host task."); }
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sequence,envelope FROM security_audit_events
            WHERE json_extract(envelope,'$.Request.RequestId.Value')=$request
                AND json_extract(envelope,'$.Request.SessionId.Value')=$session
                AND json_extract(envelope,'$.Request.TaskId.Value')=$task
            ORDER BY sequence LIMIT 1;
            """;
        command.Parameters.AddWithValue("$request", Id(request.RequestId));
        command.Parameters.AddWithValue("$session", Id(request.SessionId));
        command.Parameters.AddWithValue("$task", Id(request.TaskId));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        { throw new InvalidDataException("Model usage has no committed host request generation."); }
        var audit = HostInteractionCodec.Decode<AuthorityAudit>(reader.GetString(1));
        var message = ReadConversationMessage(connection, request.RequestId);
        if (audit.Request != request || audit.SessionGeneration.Value > session.Authority.Generation.Value
            || message is not null && (message.Request != request || message.Generation != audit.SessionGeneration))
        { throw new InvalidDataException("Model usage does not match the retained host request generation."); }
        return (audit.SessionGeneration, reader.GetInt64(0));
    }

    private static string ModelUsageDigest(string payload) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}
