using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteHostInteractionStore
{
    public const int MaximumAuditRetentionRecords = 128;

    private sealed record AuditAnchor(long Sequence, string Hash, long Cutoff);

    private void MigrateAuditRetention(SqliteConnection connection, SqliteTransaction transaction, CancellationToken token)
    {
        ValidateAuthority(connection);
        token.ThrowIfCancellationRequested();
        Execute(connection, transaction, HostInteractionSchema.AuditAnchorTable);
        SeedAuditAnchor(connection, transaction);
        checkpoint?.BeforeCommit(connection, transaction);
    }

    private static void SeedAuditAnchor(SqliteConnection connection, SqliteTransaction transaction) =>
        Execute(connection, transaction, "INSERT INTO authority_retention_anchor VALUES(1,1,0,$hash,0);", ("$hash", EmptyHash));

    private static AuditAnchor ReadAuditAnchor(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        if ((long)command.ExecuteScalar()! < 15) { return new(0, EmptyHash, 0); }
        command.CommandText = "SELECT version,sequence,hash,cutoff_utc FROM authority_retention_anchor WHERE singleton=1;";
        using var row = command.ExecuteReader();
        if (!row.Read()) { throw new InvalidDataException("The authoritative audit retention anchor is missing."); }
        var anchor = new AuditAnchor(row.GetInt64(1), row.GetString(2), row.GetInt64(3));
        if (row.GetInt64(0) != 1 || anchor.Sequence < 0
            || anchor.Sequence == 0 && (!Same(anchor.Hash, EmptyHash) || anchor.Cutoff != 0)
            || anchor.Sequence > 0 && (!IsHex(anchor.Hash, 64) || anchor.Cutoff <= 0 || anchor.Cutoff > DateTimeOffset.MaxValue.UtcTicks)
            || row.Read())
        { throw new InvalidDataException("The authoritative audit retention anchor is invalid."); }
        return anchor;
    }

    public async ValueTask<AuthorityAuditRetentionBatch> PruneAuditPrefixAsync(Func<bool> admitted, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        var owner = HostActivity.RequireCurrent();
        if (owner.Request.Origin != RequestOrigin.HostSystem || owner.Activity!.IsStopped || !admitted())
        { throw new InvalidOperationException("Audit retention requires a live owning host-system lifecycle admission."); }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Retention);
        try
        {
            var result = await Task.Run(() => PruneAuditPrefix(owner.Request, admitted, token), token).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }

    private AuthorityAuditRetentionBatch PruneAuditPrefix(HostRequest owner, Func<bool> admitted, CancellationToken token)
    {
        using var lease = database.AcquireReadLease(token);
        using var connection = Open(created: false, token);
        _ = RequireInspectionIdentity();
        database.RequireEmptyArtifactInventory();
        using var transaction = connection.BeginTransaction();
        var anchor = ReadAuditAnchor(connection);
        var cutoff = time.GetUtcNow();
        if (cutoff.UtcTicks < anchor.Cutoff)
        { throw new InvalidOperationException("Clock rollback holds authoritative audit retention."); }
        var references = AuditRetentionReferences(connection, cutoff, token);
        var heldAt = references.First;
        var last = anchor;
        var removed = 0;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT sequence,correlation_id,previous_hash,hash,envelope FROM security_audit_events ORDER BY sequence LIMIT {MaximumAuditRetentionRecords};";
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read())
            {
                token.ThrowIfCancellationRequested();
                var sequence = rows.GetInt64(0);
                var audit = DecodeAuthorityAudit(sequence, rows.GetString(1), rows.GetString(2), rows.GetString(3), rows.GetString(4));
                if (audit.DueAt > cutoff || sequence >= heldAt) { break; }
                last = new(sequence, rows.GetString(3), cutoff.UtcTicks);
                removed++;
            }
        }
        if (removed != 0)
        {
            Execute(connection, transaction, """
                DELETE FROM security_audit_events WHERE sequence<=$sequence;
                UPDATE authority_retention_anchor SET sequence=$sequence,hash=$hash,cutoff_utc=$cutoff WHERE singleton=1;
                """, ("$sequence", last.Sequence), ("$hash", last.Hash), ("$cutoff", last.Cutoff));
        }
        ValidateAuthority(connection);
        var backlog = references.LastDue > last.Sequence;
        Commit(transaction, owner, token, admitted);
        // After COMMIT, cancellation is not a rollback receipt; validation failure requires explicit recovery.
        checkpoint?.AfterAuditRetentionCommit(connection);
        ValidateAuthority(connection);
        database.VerifyFiles();
        if (ReadAuditAnchor(connection) != last)
        { throw new IOException("Audit retention committed but its exact anchor readback is unconfirmed; inspect storage before retry."); }
        return new(removed, last.Sequence, last.Cutoff == 0 ? null : new DateTimeOffset(last.Cutoff, TimeSpan.Zero),
            backlog, backlog && heldAt <= last.Sequence + 1);
    }

    private static (long First, long LastDue) AuditRetentionReferences(SqliteConnection connection, DateTimeOffset cutoff, CancellationToken token)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.name,f."from" FROM sqlite_schema s JOIN pragma_foreign_key_list(s.name) f
            WHERE s.type='table' AND f."table"='security_audit_events';
            """;
        var references = new List<(string Table, string Column)>();
        using (var rows = command.ExecuteReader())
        {
            while (rows.Read()) { references.Add((rows.GetString(0), rows.GetString(1))); }
        }
        var first = long.MaxValue;
        foreach (var reference in references)
        {
            token.ThrowIfCancellationRequested();
            // Names come only from the already exact-validated host schema, never an input query.
            command.CommandText = $"SELECT MIN(\"{reference.Column}\") FROM \"{reference.Table}\";";
            if (command.ExecuteScalar() is long sequence) { first = Math.Min(first, sequence); }
        }
        command.CommandText = """
            SELECT MIN(a.sequence) FROM security_audit_events a JOIN host_tasks t
                ON t.task_id=json_extract(a.envelope,'$.Request.TaskId.Value')
            WHERE t.state IN ($intent,$dispatch,$unknown);
            """;
        command.Parameters.AddWithValue("$intent", (int)HostTaskState.IntentRecorded);
        command.Parameters.AddWithValue("$dispatch", (int)HostTaskState.DispatchRecorded);
        command.Parameters.AddWithValue("$unknown", (int)HostTaskState.Unknown);
        if (command.ExecuteScalar() is long live) { first = Math.Min(first, live); }
        command.CommandText = "SELECT sequence,correlation_id,previous_hash,hash,envelope FROM security_audit_events ORDER BY sequence;";
        long lastDue = 0;
        using var audits = command.ExecuteReader();
        while (audits.Read())
        {
            token.ThrowIfCancellationRequested();
            var audit = DecodeAuthorityAudit(audits.GetInt64(0), audits.GetString(1), audits.GetString(2),
                audits.GetString(3), audits.GetString(4));
            if (audit.DueAt <= cutoff) { lastDue = audits.GetInt64(0); }
            if (audit.Audit.Outcome is SecurityAuditOutcome.Requested or SecurityAuditOutcome.Unknown)
            { first = Math.Min(first, audits.GetInt64(0)); }
            if (audit.QueueMoveReceipt is { } move)
            { first = Math.Min(first, Math.Min(move.Selected.Position, move.Neighbor.Position)); }
            if (audit.QueueCancelReceipt is { } cancel)
            { foreach (var entry in cancel.Entries) { first = Math.Min(first, entry.Position); } }
            if (audit.QueueRequeueReceipt is { } requeue)
            { first = Math.Min(first, Math.Min(requeue.Original.Position, requeue.Fresh.Position)); }
        }
        return (first, lastDue);
    }
}
