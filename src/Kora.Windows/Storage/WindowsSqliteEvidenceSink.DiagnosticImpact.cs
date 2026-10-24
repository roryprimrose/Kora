using System.Text.Json;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteEvidenceSink
{
    internal string DiagnosticImpactStamp() => database.ReadPreviewStamp();

    internal DiagnosticImpactSnapshot ReadDiagnosticImpact(DiagnosticRetentionDays proposed, DateTimeOffset cutoff,
        CancellationToken token)
    {
        var scan = new DiagnosticImpactScan(proposed, cutoff);
        var stamp = AccumulateDiagnosticImpact(scan, token);
        return scan.Finish(stamp);
    }

    internal string AccumulateDiagnosticImpact(DiagnosticImpactScan scan, CancellationToken token)
    {
        using var lease = database.AcquireReadLease(token);
        var stamp = DiagnosticImpactStamp();
        // No initialization, migration, validation cache or writer may run on this observation path.
        using var connection = database.OpenReadOnly(token);
        using var transaction = connection.BeginTransaction(deferred: true);
        // The janitor's admission validates the preserved typed audit prefix too. This guard never
        // aggregates audit rows or infers audit authority from ordinary diagnostic properties.
        scan.ProtectedPrefixComplete = ValidateDiagnosticImpactProtectedPrefix(connection, scan.ProtectedPrefix, token);
        if (scan.ProtectedPrefixComplete)
        {
            if (scan.LogsAvailable && scan.Logs.Finish(DiagnosticImpactSource.OrdinaryLogs).Status == DiagnosticImpactStatus.Complete)
            {
                try { ReadLogImpact(connection, scan.Logs, token); }
                catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
                {
                    scan.LogsAvailable = false;
                }
            }
            if (scan.OwnedAvailable && scan.Spans.Finish(DiagnosticImpactSource.Spans).Status == DiagnosticImpactStatus.Complete)
            {
                try { ReadOwnedImpact(connection, scan.Spans, scan.Links, token); }
                catch (Exception exception) when (exception is InvalidDataException or JsonException or ArgumentException)
                {
                    scan.OwnedAvailable = false;
                }
            }
        }
        token.ThrowIfCancellationRequested();
        if (!string.Equals(stamp, DiagnosticImpactStamp(), StringComparison.Ordinal))
        {
            throw new InvalidDataException("The diagnostic store changed during preview.");
        }
        return stamp;
    }

    private static bool ValidateDiagnosticImpactProtectedPrefix(SqliteConnection connection,
        DiagnosticRetentionImpact budget, CancellationToken token)
    {
        using var command = ImpactCommand(connection, "security_audit_events", "audit_sequence");
        using var rows = command.ExecuteReader();
        long sequence = 0;
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            if (!budget.Admit(EncodedLength(rows))) { return false; }
            var payload = rows.GetString(rows.GetOrdinal("envelope"));
            using (var document = JsonDocument.Parse(payload, new() { MaxDepth = 16 })) { RequireUnique(document.RootElement); }
            var audit = DecodeProjection("security_audit_events", payload);
            ValidateProjectionColumns(rows, audit.Projection);
            if (rows.GetInt64(rows.GetOrdinal("audit_sequence")) != ++sequence
                || !AuditRetentionDays.IsValidDeadline(ReadTimestamp(rows, "committed_utc"), ReadTimestamp(rows, "due_utc")))
            {
                throw new InvalidDataException("The preserved typed audit prefix is unqualified.");
            }
        }
        rows.Close();
        ValidateAuditSequence(connection);
        return true;
    }

    private static void ReadLogImpact(SqliteConnection connection, DiagnosticRetentionImpact impact, CancellationToken token)
    {
        using var command = ImpactCommand(connection, "application_log_events", "evidence_id");
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            if (!impact.Admit(EncodedLength(rows))) { break; }
            ValidateImpactRow(rows, "application_log_events", impact);
        }
    }

    private static void ReadOwnedImpact(SqliteConnection connection, DiagnosticRetentionImpact spans,
        DiagnosticRetentionImpact links, CancellationToken token)
    {
        using var command = ImpactCommand(connection, "activity_spans", "evidence_id");
        using var linkCommand = ImpactCommand(connection, "activity_links", "evidence_id,ordinal");
        using var rows = command.ExecuteReader();
        using var owned = linkCommand.ExecuteReader();
        var hasLink = owned.Read();
        while (rows.Read())
        {
            token.ThrowIfCancellationRequested();
            if (!spans.Admit(EncodedLength(rows))) { return; }
            var activity = ValidateImpactRow(rows, "activity_spans", spans).Activity!;
            var index = 0;
            while (hasLink && string.Equals(owned.GetString(owned.GetOrdinal("evidence_id")),
                activity.EvidenceId.Value.ToString("D"), StringComparison.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                // One shared finite budget covers both owners and links, not 32 unbounded child scans.
                if (!spans.Admit(EncodedLength(owned))) { return; }
                ValidateOwnedLink(rows, activity, owned, index);
                links.Add(ReadTimestamp(owned, "committed_utc"), ReadTimestamp(owned, "due_utc"));
                index++;
                hasLink = owned.Read();
            }
            if (index != activity.Links.Count) { throw new InvalidDataException("An owned diagnostic link is missing."); }
        }
        if (hasLink) { throw new InvalidDataException("An orphan or unqualified diagnostic link remains."); }
    }

    private static SqliteCommand ImpactCommand(SqliteConnection connection, string table, string order)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT *,length(CAST(envelope AS BLOB)) AS encoded_bytes FROM {table}
            ORDER BY {order} LIMIT {DiagnosticRetentionImpact.MaximumRows + 1};
            """;
        return command;
    }

    private static int EncodedLength(SqliteDataReader row) => checked((int)row.GetInt64(row.GetOrdinal("encoded_bytes")));

    private static ValidatedProjection ValidateImpactRow(SqliteDataReader row, string table, DiagnosticRetentionImpact impact)
    {
        var payload = row.GetString(row.GetOrdinal("envelope"));
        using (var document = JsonDocument.Parse(payload, new() { MaxDepth = 16 })) { RequireUnique(document.RootElement); }
        var validated = DecodeProjection(table, payload);
        ValidateProjectionColumns(row, validated.Projection);
        impact.Add(ReadTimestamp(row, "committed_utc"), ReadTimestamp(row, "due_utc"));
        return validated;
    }
}
