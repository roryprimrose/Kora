using System.Security.Cryptography;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteEvidenceSink
{
    internal (long AuditCount, long Pages) ReadSegmentState(CancellationToken token) =>
        ReadSegmentState(initialAdmission: false, token);

    internal (long AuditCount, long Pages) ReadSegmentState(bool initialAdmission, CancellationToken token)
    {
        using var lease = database.AcquireReadLease(token);
        using var connection = OpenReadOnly(initialAdmission, token);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM security_audit_events;";
        var audits = (long)command.ExecuteScalar()!;
        command.CommandText = "PRAGMA page_count;";
        return (audits, (long)command.ExecuteScalar()!);
    }

    internal byte[] SegmentDigest() => database.ComputeDatabaseDigest();
    internal void ReleaseParsedHistory() => validationCache = new();

    internal bool ContainsEvidence(Guid id, CancellationToken token) =>
        ContainsEvidence(id, initialAdmission: false, token);

    internal bool ContainsEvidence(Guid id, bool initialAdmission, CancellationToken token)
    {
        using var lease = database.AcquireReadLease(token);
        using var connection = OpenReadOnly(initialAdmission, token);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM application_log_events WHERE evidence_id=$id)
                OR EXISTS(SELECT 1 FROM security_audit_events WHERE evidence_id=$id)
                OR EXISTS(SELECT 1 FROM activity_spans WHERE evidence_id=$id);
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return (long)command.ExecuteScalar()! != 0;
    }

    internal void VisitAdmissionEvidenceIds(Action<Guid> visit, CancellationToken token)
    {
        using var lease = database.AcquireReadLease(token);
        using var connection = OpenReadOnly(initialAdmission: true, token);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT evidence_id,count(*) FROM (
                SELECT evidence_id FROM application_log_events
                UNION ALL SELECT evidence_id FROM security_audit_events
                UNION ALL SELECT evidence_id FROM activity_spans)
            GROUP BY evidence_id;
            """;
        RestrictedSqliteDatabase.ResetProgressBudget(connection, InitialIntegrityTimeout, timeProvider, token);
        using var rows = command.ExecuteReader();
        while (ReadValidationRow(rows, connection, timeProvider, token))
        {
            token.ThrowIfCancellationRequested();
            if (rows.GetInt64(1) != 1) { throw new InvalidDataException("An evidence identity is ambiguous across record types."); }
            visit(Guid.ParseExact(rows.GetString(0), "D"));
        }
    }

    internal void RequireSegmentDigest(string expected)
    {
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), SegmentDigest()))
        {
            throw new InvalidDataException("The sealed evidence segment no longer matches its admitted digest.");
        }
    }
}
