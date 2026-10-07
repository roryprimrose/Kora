using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed class WindowsSqliteEvidenceReader(WindowsSqliteEvidenceSink sink) : IEvidenceReader
{
    private static readonly string[] Tables =
        ["application_log_events", "security_audit_events", "activity_spans", "activity_links"];
    internal const int MaximumScannedRows = 4096;

    public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
        HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var current = HostActivity.RequireCurrent();
        if (current.Activity!.IsStopped || current.Request != request || request.Origin != RequestOrigin.LocalUi)
        {
            throw new InvalidOperationException("Evidence reads require the admitted live local-UI request.");
        }
        return new(Task.Run(() =>
        {
            try { return Read(query, checkpoint, request, now, cancellationToken); }
            catch (Exception exception) when (cancellationToken.IsCancellationRequested
                && exception is SqliteException or InvalidDataException)
            {
                throw new OperationCanceledException("The evidence read was cancelled during admission or query.", exception, cancellationToken);
            }
        }, cancellationToken));
    }

    private EvidenceReadBatch Read(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
        HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
    {
        using var lease = sink.AcquireReadLease(cancellationToken);
        using var connection = sink.OpenReadOnly(cancellationToken);
        var snapshot = checkpoint?.Snapshot ?? Snapshot(connection);
        if (checkpoint is not null) { ValidateSnapshot(connection, snapshot); }
        var ceilings = new[] { snapshot.Log, snapshot.Audit, snapshot.Span, snapshot.Link };
        using var command = connection.CreateCommand();
        var selects = new List<string>();
        for (var index = 0; index < Tables.Length; index++)
        {
            var source = (EvidenceSource)(index + 1);
            if (query.Source != EvidenceSource.All && query.Source != source) { continue; }
            var ordinal = source == EvidenceSource.Link ? "ordinal" : "-1";
            var where = new List<string>
            {
                $"rowid<=$ceiling{index}", "(request_id IS NULL OR request_id<>$excluded)",
            };
            command.Parameters.AddWithValue($"$ceiling{index}", ceilings[index]);
            Add(where, command, "session_id", query.SessionId?.Value.ToString("D"));
            Add(where, command, "task_id", query.TaskId?.Value.ToString("D"));
            Add(where, command, "request_id", query.RequestId?.Value.ToString("D"));
            Add(where, command, "invocation_id", query.InvocationId?.Value.ToString("D"));
            Add(where, command, "approval_id", query.ApprovalId?.ToString("D"));
            Add(where, command, "audit_correlation_id", query.CorrelationId?.ToString("D"));
            if (source == EvidenceSource.Link)
            {
                if (query.TraceId is not null && query.SpanId is not null)
                {
                    where.Add("((trace_id=$trace_id AND span_id=$span_id) OR (source_trace_id=$trace_id AND source_span_id=$span_id))");
                    Parameter(command, "$trace_id", query.TraceId);
                    Parameter(command, "$span_id", query.SpanId);
                }
                else if (query.TraceId is not null)
                {
                    where.Add("(trace_id=$trace_id OR source_trace_id=$trace_id)");
                    Parameter(command, "$trace_id", query.TraceId);
                }
                else if (query.SpanId is not null)
                {
                    where.Add("(span_id=$span_id OR source_span_id=$span_id)");
                    Parameter(command, "$span_id", query.SpanId);
                }
            }
            else
            {
                Add(where, command, "trace_id", query.TraceId);
                Add(where, command, "span_id", query.SpanId);
            }
            if (query.FromUtc is { } from) { where.Add("committed_utc >= $from"); Parameter(command, "$from", from.UtcTicks); }
            if (query.UntilUtc is { } until) { where.Add("committed_utc <= $until"); Parameter(command, "$until", until.UtcTicks); }
            if (query.Record is { } reference)
            {
                where.Add(source == reference.Source ? "evidence_id=$record" : "0");
                Parameter(command, "$record", reference.Id.Value.ToString("D"));
                if (source == EvidenceSource.Link)
                {
                    where.Add("ordinal=$ordinal");
                    Parameter(command, "$ordinal", reference.LinkOrdinal ?? -1);
                }
            }
            var auditSequence = source == EvidenceSource.Audit ? "audit_sequence" : "NULL";
            selects.Add($"SELECT committed_utc,{index + 1} AS source,evidence_id,{ordinal} AS ordinal,due_utc,envelope,{auditSequence} AS audit_sequence FROM {Tables[index]} WHERE {string.Join(" AND ", where)}");
        }
        Parameter(command, "$excluded", request.RequestId.Value.ToString("D"));
        var after = checkpoint?.After;
        var continuation = after is null ? string.Empty
            : "WHERE (committed_utc,source,evidence_id,ordinal)>($ticks,$source,$id,$lastOrdinal)";
        if (after is not null)
        {
            Parameter(command, "$ticks", after.CommittedTicks);
            Parameter(command, "$source", (int)after.Source);
            Parameter(command, "$id", after.Id);
            Parameter(command, "$lastOrdinal", after.Ordinal);
        }
        command.CommandText = $"SELECT * FROM ({string.Join(" UNION ALL ", selects)}) {continuation} ORDER BY committed_utc,source,evidence_id,ordinal LIMIT {MaximumScannedRows + 1};";
        using var rows = command.ExecuteReader();
        var candidates = new List<EvidenceCandidate>();
        EvidencePosition? scannedThrough = after;
        var scanned = 0;
        while (rows.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++scanned > MaximumScannedRows)
            {
                return new(snapshot, candidates, scannedThrough, HasMore: true, ScanLimitReached: true);
            }
            var position = new EvidencePosition(rows.GetInt64(0), (EvidenceSource)rows.GetInt32(1), rows.GetString(2), rows.GetInt32(3));
            var record = Project(connection, rows, position, query.SessionId, snapshot, now);
            scannedThrough = position;
            if (!Matches(record, query)) { continue; }
            candidates.Add(new(position, record));
            if (candidates.Count == query.Limit + 1)
            {
                return new(snapshot, candidates, scannedThrough, HasMore: true, ScanLimitReached: false);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(snapshot, candidates, scannedThrough, HasMore: false, ScanLimitReached: false);
    }

    private static EvidenceSnapshot Snapshot(SqliteConnection connection)
    {
        var values = new long[Tables.Length];
        var identities = new string?[Tables.Length];
        using var command = connection.CreateCommand();
        for (var index = 0; index < Tables.Length; index++)
        {
            var identity = index == 3 ? "evidence_id || ':' || ordinal" : "evidence_id";
            command.CommandText = $"SELECT rowid,{identity} FROM {Tables[index]} ORDER BY rowid DESC LIMIT 1;";
            using var row = command.ExecuteReader();
            if (row.Read())
            {
                values[index] = row.GetInt64(0);
                identities[index] = row.GetString(1);
            }
        }
        return new(values[0], values[1], values[2], values[3], identities[0], identities[2], identities[3]);
    }

    private static void ValidateSnapshot(SqliteConnection connection, EvidenceSnapshot snapshot)
    {
        // Ordinary rowids can be reused after pruning. Never admit a replacement into an old cursor.
        var ceilings = new[] { snapshot.Log, snapshot.Span, snapshot.Link };
        var identities = new[] { snapshot.LogCeilingId, snapshot.SpanCeilingId, snapshot.LinkCeilingId };
        var tables = new[] { Tables[0], Tables[2], Tables[3] };
        using var command = connection.CreateCommand();
        command.Parameters.Add("$ceiling", SqliteType.Integer);
        for (var index = 0; index < tables.Length; index++)
        {
            if (ceilings[index] == 0) { continue; }
            var identity = index == 2 ? "evidence_id || ':' || ordinal" : "evidence_id";
            command.CommandText = $"SELECT {identity} FROM {tables[index]} WHERE rowid=$ceiling;";
            command.Parameters["$ceiling"].Value = ceilings[index];
            if (identities[index] is null
                || !string.Equals(command.ExecuteScalar() as string, identities[index], StringComparison.Ordinal))
            {
                throw new InvalidDataException("The evidence snapshot changed during retention; start a new query.");
            }
        }
    }

    private static void Add(List<string> where, SqliteCommand command, string column, string? value)
    {
        if (value is null) { return; }
        where.Add($"{column}=${column}");
        Parameter(command, "$" + column, value);
    }

    private static void Parameter(SqliteCommand command, string name, object value)
    {
        if (!command.Parameters.Contains(name)) { command.Parameters.AddWithValue(name, value); }
    }

    private static EvidenceRecord Project(SqliteConnection connection, SqliteDataReader row, EvidencePosition position,
        HostId<SessionIdentity>? session, EvidenceSnapshot snapshot, DateTimeOffset now)
    {
        var id = new HostId<EvidenceIdentity>(Guid.ParseExact(position.Id, "D"));
        var committed = new DateTimeOffset(position.CommittedTicks, TimeSpan.Zero);
        var due = new DateTimeOffset(row.GetInt64(4), TimeSpan.Zero);
        var retention = due <= now ? EvidenceSegmentStatus.ExpiredButPresent : EvidenceSegmentStatus.Present;
        var payload = row.GetString(5);
        if (position.Source is EvidenceSource.Log or EvidenceSource.Audit)
        {
            var audit = position.Source == EvidenceSource.Audit ? WindowsSqliteEvidenceSink.DecodeAudit(payload) : null;
            var diagnostic = audit?.Diagnostic ?? WindowsSqliteEvidenceSink.DecodeDiagnostic(payload);
            var properties = diagnostic.Properties.ToDictionary(pair => pair.Key,
                pair => EvidenceFieldPolicy.IsSensitive(pair.Key) ? new(EvidenceValueKind.Text, "[redacted]") : pair.Value,
                StringComparer.Ordinal);
            var related = diagnostic.Trace is { } trace
                ? new[] { Resolve(connection, trace.TraceId, trace.SpanId, session, snapshot, now) } : [];
            return new(new(position.Source, id), committed, due, retention, diagnostic.Host, diagnostic.Trace,
                diagnostic.AuditCorrelationId, diagnostic.ApprovalId, diagnostic.Level, diagnostic.Category,
                diagnostic.EventId, diagnostic.MessageTemplate, properties, audit?.Audit, null, related,
                AuditSequence: row.IsDBNull(6) ? null : row.GetInt64(6));
        }
        var owner = position.Source == EvidenceSource.Link ? ReadOwner(connection, position.Id) : payload;
        var span = WindowsSqliteEvidenceSink.DecodeActivity(owner);
        var segments = new List<EvidenceSegment>();
        if (span.Trace.ParentSpanId is { } parent)
        {
            segments.Add(Resolve(connection, span.Trace.TraceId, parent, session, snapshot, now));
        }
        foreach (var link in span.Links)
        {
            segments.Add(Resolve(connection, link.TraceId, link.SpanId, session, snapshot, now));
        }
        if (position.Source == EvidenceSource.Link)
        {
            var link = span.Links[position.Ordinal];
            segments = [Resolve(connection, link.TraceId, link.SpanId, session, snapshot, now)];
        }
        return new(new(position.Source, id, position.Source == EvidenceSource.Link ? position.Ordinal : null),
            committed, due, retention, span.Host, span.Trace, span.AuditCorrelationId, span.ApprovalId,
            null, span.Trace.Source, null, span.Trace.Name, new Dictionary<string, EvidenceValue>(StringComparer.Ordinal),
            null, span.Outcome, segments);
    }

    private static string ReadOwner(SqliteConnection connection, string id)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM activity_spans WHERE evidence_id=$id;";
        command.Parameters.AddWithValue("$id", id);
        return (string?)command.ExecuteScalar() ?? throw new InvalidDataException("The activity link owner is missing.");
    }

    private static EvidenceSegment Resolve(SqliteConnection connection, string trace, string span,
        HostId<SessionIdentity>? session, EvidenceSnapshot snapshot, DateTimeOffset now)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT evidence_id,due_utc FROM activity_spans
            WHERE trace_id=$trace AND span_id=$span AND rowid<=$ceiling
                AND ($session IS NULL OR session_id=$session)
            ORDER BY committed_utc,evidence_id LIMIT 1;
            """;
        command.Parameters.AddWithValue("$trace", trace);
        command.Parameters.AddWithValue("$span", span);
        command.Parameters.AddWithValue("$ceiling", snapshot.Span);
        command.Parameters.AddWithValue("$session", session is { } identity ? identity.Value.ToString("D") : DBNull.Value);
        using var rows = command.ExecuteReader();
        return rows.Read()
            ? new(trace, span, rows.GetInt64(1) <= now.UtcTicks ? EvidenceSegmentStatus.ExpiredButPresent : EvidenceSegmentStatus.Present,
                new(EvidenceSource.Span, new(Guid.ParseExact(rows.GetString(0), "D"))))
            : new(trace, span, EvidenceSegmentStatus.MissingOrRemoved, null);
    }

    internal static bool Matches(EvidenceRecord record, EvidenceQuery query)
    {
        if (query.Severity is { } severity && !string.Equals(record.Level, severity.ToString(), StringComparison.Ordinal)
            || query.EventId is { } eventId && record.EventId != eventId
            || query.Category is { } category && !string.Equals(record.Category, category, StringComparison.Ordinal)
            || query.ActionId is { } action && !string.Equals(record.Audit?.ActionId, action, StringComparison.Ordinal)
            || query.AuditOutcome is { } outcome && record.Audit?.Outcome != outcome)
        {
            return false;
        }
        if (query.Property is { } property && (!record.Properties.TryGetValue(property.Name, out var value) || value != property.Value))
        {
            return false;
        }
        return query.Text is not { } text || (record.Text?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
            || record.Properties.Values.Any(value => value.CanonicalValue?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
