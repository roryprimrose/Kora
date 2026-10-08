using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Dependencies;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteEvidenceSink : IEvidenceSink
{
    internal const string PartitionName = "EvidenceStorageV1";
    internal const int MaximumEnvelopeBytes = 65536;
    private const int ApplicationId = 1263489586;
    private const string BootstrapProperty = "kora.bootstrap";
    private const string ContextGapProperty = "kora.evidence.gap";
    private const string CorrelationColumns = """
        request_id TEXT CHECK(length(request_id)=36), session_id TEXT CHECK(length(session_id)=36),
        task_id TEXT CHECK(length(task_id)=36), origin INTEGER CHECK(origin BETWEEN 0 AND 2),
        invocation_id TEXT CHECK(length(invocation_id)=36),
        trace_id TEXT CHECK(length(trace_id)=32), span_id TEXT CHECK(length(span_id)=16),
        parent_span_id TEXT CHECK(length(parent_span_id)=16), trace_flags INTEGER CHECK(trace_flags BETWEEN 0 AND 1),
        activity_source TEXT CHECK(length(activity_source)<=128),
        activity_version TEXT CHECK(length(activity_version)<=128), activity_name TEXT CHECK(length(activity_name)<=128),
        activity_kind INTEGER CHECK(activity_kind BETWEEN 0 AND 4),
        audit_correlation_id TEXT CHECK(length(audit_correlation_id)=36), approval_id TEXT CHECK(length(approval_id)=36)
        """;
    private const string PayloadColumns = """
        committed_utc INTEGER NOT NULL,
        due_utc INTEGER NOT NULL CHECK(due_utc>committed_utc),
        envelope TEXT NOT NULL CHECK(length(CAST(envelope AS BLOB)) BETWEEN 1 AND 65536)
        """;
    private static readonly string[] Schema = BuildSchema();
    private static readonly JsonSerializerOptions Serialization = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 16,
    };
    private readonly RestrictedSqliteDatabase database;
    private readonly EvidenceRetentionPolicy retentionPolicy;
    private readonly DiagnosticRetentionPolicy? diagnosticPolicy;
    private readonly TimeProvider timeProvider;
    private readonly ISqliteTransactionCheckpoint? checkpoint;

    public WindowsSqliteEvidenceSink(IApplicationDataPaths paths,
        EvidenceRetentionPolicy? retentionPolicy = null, TimeProvider? timeProvider = null,
        DiagnosticRetentionPolicy? diagnosticPolicy = null)
        : this(paths, retentionPolicy, timeProvider, checkpoint: null, diagnosticPolicy)
    {
    }

    internal WindowsSqliteEvidenceSink(IApplicationDataPaths paths,
        EvidenceRetentionPolicy? retentionPolicy, TimeProvider? timeProvider, ISqliteTransactionCheckpoint? checkpoint,
        DiagnosticRetentionPolicy? diagnosticPolicy = null)
    {
        database = new RestrictedSqliteDatabase(paths, PartitionName, "evidence.db", ApplicationId, Schema,
            new(1, 2, Schema, (connection, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                ValidateDatabase(connection, legacy: true);
            }));
        this.retentionPolicy = retentionPolicy ?? new EvidenceRetentionPolicy();
        this.diagnosticPolicy = diagnosticPolicy;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.checkpoint = checkpoint;
    }

    public string Name => "windows-sqlite";

    // Startup admission intentionally performs no activity/log emission and requires no encryption key.
    public void Initialize()
    {
        using var lease = database.AcquireLease(out var created);
        using var connection = OpenDatabase(created);
    }

    public void WriteDiagnostic(DiagnosticEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateDiagnostic(envelope);
        if (envelope.Host is { } host)
        {
            ValidateLiveContext(host, envelope.Trace, envelope.AuditCorrelationId, envelope.ApprovalId);
        }
        var values = DiagnosticProjection(envelope);
        var payload = Serialize(envelope);
        CommitEvidence(() => Write("application_log_events", values, payload, audit: false));
    }

    public void WriteAudit(AuditEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateAudit(envelope);
        ValidateLiveContext(envelope.Diagnostic.Host!, envelope.Diagnostic.Trace,
            envelope.Audit.CorrelationId, envelope.Audit.ApprovalId);
        var values = AuditProjection(envelope);
        var payload = Serialize(envelope);
        CommitEvidence(() => Write("security_audit_events", values, payload, audit: true));
    }

    public void WriteActivity(CompletedActivityEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateActivity(envelope);
        var values = ActivityProjection(envelope);
        var payload = Serialize(envelope);
        CommitEvidence(() => WriteActivityCore(envelope, values, payload));
    }

    private void WriteActivityCore(CompletedActivityEnvelope envelope, Dictionary<string, object> values, string payload)
    {
        using var lease = database.AcquireLease(out var created);
        using var connection = OpenDatabase(created);
        using var transaction = connection.BeginTransaction();
        checkpoint?.BeforeWrite(connection, transaction);
        var committed = timeProvider.GetUtcNow().UtcTicks;
        var due = DiagnosticDue(new DateTimeOffset(committed, TimeSpan.Zero)).UtcTicks;
        Insert(connection, transaction, "activity_spans", values, payload, committed, due);
        for (var ordinal = 0; ordinal < envelope.Links.Count; ordinal++)
        {
            var link = envelope.Links[ordinal];
            Insert(connection, transaction, "activity_links", LinkProjection(envelope, link, ordinal),
                Serialize(link), committed, due);
        }
        checkpoint?.BeforeCommit(connection, transaction);
        database.VerifyFiles();
        transaction.Commit();
    }

    private static void CommitEvidence(Action commit)
    {
        try
        {
            commit();
        }
        catch (Exception exception) when (exception is InvalidDataException or SqliteException)
        {
            throw new IOException("The private evidence database could not durably commit the admitted envelope.", exception);
        }
    }

    private void Write(string table, Dictionary<string, object> values, string payload, bool audit)
    {
        using var lease = database.AcquireLease(out var created);
        using var connection = OpenDatabase(created);
        using var transaction = connection.BeginTransaction();
        checkpoint?.BeforeWrite(connection, transaction);
        var committed = timeProvider.GetUtcNow();
        var due = audit ? retentionPolicy.AuditDue(committed) : DiagnosticDue(committed);
        Insert(connection, transaction, table, values, payload, committed.UtcTicks, due.UtcTicks);
        checkpoint?.BeforeCommit(connection, transaction);
        database.VerifyFiles();
        transaction.Commit();
    }

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, string table,
        Dictionary<string, object> values, string payload, long committed, long due)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        // All identifiers originate in private schema/projection definitions, never in envelope text.
        command.CommandText = $"INSERT INTO {table}({string.Join(',', values.Keys)},committed_utc,due_utc,envelope) "
            + $"VALUES({string.Join(',', values.Keys.Select(key => "$" + key))},$committed,$due,$envelope);";
        foreach (var pair in values)
        {
            command.Parameters.AddWithValue("$" + pair.Key, pair.Value);
        }
        command.Parameters.AddWithValue("$committed", committed);
        command.Parameters.AddWithValue("$due", due);
        command.Parameters.AddWithValue("$envelope", payload);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenDatabase(bool created)
    {
        var connection = database.Open(created);
        return ValidateDatabase(connection);
    }

    internal FileStream AcquireReadLease(CancellationToken cancellationToken) => database.AcquireReadLease(cancellationToken);

    internal SqliteConnection OpenReadOnly(CancellationToken cancellationToken) =>
        ValidateDatabase(database.OpenReadOnly(cancellationToken));

    private DateTimeOffset DiagnosticDue(DateTimeOffset committed) =>
        diagnosticPolicy is null ? retentionPolicy.DiagnosticDue(committed) : diagnosticPolicy.Due(committed);

    private static SqliteConnection ValidateDatabase(SqliteConnection connection, bool legacy = false)
    {
        try
        {
            ValidateRows(connection, "application_log_events", legacy);
            ValidateRows(connection, "security_audit_events");
            ValidateRows(connection, "activity_spans", legacy);
            ValidateAuditSequence(connection);
            using var foreignKeys = connection.CreateCommand();
            foreignKeys.CommandText = "PRAGMA foreign_key_check;";
            using var violations = foreignKeys.ExecuteReader();
            if (violations.Read())
            {
                throw new InvalidDataException("The evidence store contains orphan activity links.");
            }
            return connection;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or SqliteException)
        {
            connection.Dispose();
            throw new InvalidDataException("Persisted evidence is malformed or inconsistent.", exception);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void ValidateRows(SqliteConnection connection, string table, bool legacy = false)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM {table};";
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            var payload = rows.GetString(rows.GetOrdinal("envelope"));
            if (Encoding.UTF8.GetByteCount(payload) > MaximumEnvelopeBytes)
            {
                throw new InvalidDataException("A persisted evidence envelope exceeds its bound.");
            }
            Dictionary<string, object> projection;
            if (string.Equals(table, "application_log_events", StringComparison.Ordinal))
            {
                var envelope = DecodeDiagnostic(payload);
                projection = DiagnosticProjection(envelope);
            }
            else if (string.Equals(table, "security_audit_events", StringComparison.Ordinal))
            {
                var envelope = DecodeAudit(payload);
                projection = AuditProjection(envelope);
                if (rows.GetInt64(rows.GetOrdinal("audit_sequence")) <= 0)
                {
                    throw new InvalidDataException("The persisted audit order is invalid.");
                }
            }
            else
            {
                var envelope = DecodeActivity(payload);
                projection = ActivityProjection(envelope);
                ValidateLinks(connection, rows, envelope);
            }

            foreach (var pair in projection)
            {
                if (!Equals(rows.GetValue(rows.GetOrdinal(pair.Key)), pair.Value))
                {
                    throw new InvalidDataException("Typed evidence correlation disagrees with its persisted envelope.");
                }
            }
            var committed = ReadTimestamp(rows, "committed_utc");
            var due = ReadTimestamp(rows, "due_utc");
            var days = (due - committed).TotalDays;
            if (string.Equals(table, "security_audit_events", StringComparison.Ordinal)
                ? days is < 30 or > 365 || days != Math.Truncate(days)
                : legacy ? days != EvidenceRetentionPolicy.DiagnosticDays
                    : days is < DiagnosticRetentionDays.Minimum or > DiagnosticRetentionDays.Maximum || days != Math.Truncate(days))
            {
                throw new InvalidDataException("The persisted effective retention due date is invalid.");
            }
        }
    }

    public static string EncodeDailyDiagnostic(DiagnosticEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ValidateDiagnostic(envelope);
        return Serialize(envelope);
    }

    public static DiagnosticEnvelope DecodeDiagnostic(string payload)
    {
        if (Encoding.UTF8.GetByteCount(payload) > MaximumEnvelopeBytes)
        {
            throw new InvalidDataException("The serialized diagnostic exceeds its byte bound.");
        }
        using (var document = JsonDocument.Parse(payload, new() { MaxDepth = 16 }))
        {
            RequireUnique(document.RootElement);
            foreach (var field in new[]
            {
                "SchemaVersion", "EvidenceId", "ObservedUtc", "EventId", "EventName", "Level", "Category",
                "MessageTemplate", "Properties", "Scopes", "Trace", "Host", "ExceptionType",
                "AuditCorrelationId", "ApprovalId",
            })
            {
                if (!document.RootElement.TryGetProperty(field, out _))
                {
                    throw new InvalidDataException("A versioned diagnostic envelope field is missing.");
                }
            }
        }
        var envelope = JsonSerializer.Deserialize<DiagnosticEnvelope>(payload, Serialization)
            ?? throw new InvalidDataException("A persisted diagnostic envelope is missing.");
        ValidateDiagnostic(envelope);
        return envelope;
    }

    internal static void RequireUnique(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) { throw new InvalidDataException("Duplicate evidence JSON fields are not admitted."); }
                RequireUnique(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) { RequireUnique(item); }
        }
    }

    internal static AuditEnvelope DecodeAudit(string payload)
    {
        var envelope = JsonSerializer.Deserialize<AuditEnvelope>(payload, Serialization)
            ?? throw new InvalidDataException("A persisted audit envelope is missing.");
        ValidateAudit(envelope);
        return envelope;
    }

    internal static CompletedActivityEnvelope DecodeActivity(string payload)
    {
        var envelope = JsonSerializer.Deserialize<CompletedActivityEnvelope>(payload, Serialization)
            ?? throw new InvalidDataException("A persisted activity envelope is missing.");
        ValidateActivity(envelope);
        return envelope;
    }

    private static void ValidateAuditSequence(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT count(*),coalesce(max(audit_sequence),0),
                coalesce((SELECT seq FROM sqlite_sequence WHERE name='security_audit_events'),0)
            FROM security_audit_events;
            """;
        using var reader = command.ExecuteReader();
        if (!reader.Read() || reader.GetInt64(0) != reader.GetInt64(1) || reader.GetInt64(1) != reader.GetInt64(2))
        {
            throw new InvalidDataException("The persisted authoritative audit sequence is discontinuous.");
        }
    }

    private static void ValidateLinks(SqliteConnection connection, SqliteDataReader row, CompletedActivityEnvelope envelope)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM activity_links WHERE evidence_id=$id ORDER BY ordinal;";
        command.Parameters.AddWithValue("$id", envelope.EvidenceId.Value.ToString("D"));
        using var links = command.ExecuteReader();
        var index = 0;
        while (links.Read())
        {
            if (index >= envelope.Links.Count || links.GetInt64(links.GetOrdinal("ordinal")) != index
                || links.GetInt64(links.GetOrdinal("committed_utc")) != row.GetInt64(row.GetOrdinal("committed_utc"))
                || links.GetInt64(links.GetOrdinal("due_utc")) != row.GetInt64(row.GetOrdinal("due_utc")))
            {
                throw new InvalidDataException("An activity link disagrees with its bounded envelope or retention date.");
            }
            var payload = links.GetString(links.GetOrdinal("envelope"));
            if (Encoding.UTF8.GetByteCount(payload) > MaximumEnvelopeBytes
                || JsonSerializer.Deserialize<ActivityLinkEnvelope>(payload, Serialization) != envelope.Links[index])
            {
                throw new InvalidDataException("A persisted activity link envelope is malformed or inconsistent.");
            }
            foreach (var pair in LinkProjection(envelope, envelope.Links[index], index))
            {
                if (!Equals(links.GetValue(links.GetOrdinal(pair.Key)), pair.Value))
                {
                    throw new InvalidDataException("Typed activity link correlation disagrees with its owning envelope.");
                }
            }
            index++;
        }
        if (index != envelope.Links.Count)
        {
            throw new InvalidDataException("A persisted activity link is missing.");
        }
    }

    private static DateTimeOffset ReadTimestamp(SqliteDataReader row, string name)
    {
        var ticks = row.GetInt64(row.GetOrdinal(name));
        if (ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            throw new InvalidDataException("A persisted evidence timestamp is invalid.");
        }
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static Dictionary<string, object> DiagnosticProjection(DiagnosticEnvelope envelope)
    {
        var values = CorrelationProjection(envelope.EvidenceId, envelope.Host, envelope.Trace,
            envelope.AuditCorrelationId, envelope.ApprovalId);
        values.Add("schema_version", (long)envelope.SchemaVersion);
        values.Add("observed_utc", envelope.ObservedUtc.UtcTicks);
        values.Add("event_id", (long)envelope.EventId);
        values.Add("event_name", Text(envelope.EventName));
        values.Add("level", envelope.Level);
        values.Add("category", envelope.Category);
        values.Add("message_template", envelope.MessageTemplate);
        values.Add("exception_type", Text(envelope.ExceptionType));
        return values;
    }

    private static Dictionary<string, object> AuditProjection(AuditEnvelope envelope)
    {
        var values = DiagnosticProjection(envelope.Diagnostic);
        values.Add("audit_category", (long)envelope.Audit.Category);
        values.Add("action_id", envelope.Audit.ActionId);
        values.Add("audit_outcome", (long)envelope.Audit.Outcome);
        values.Add("initiator", (long)envelope.Audit.Initiator);
        values.Add("target_id", envelope.Audit.TargetId);
        values.Add("reason_code", Text(envelope.Audit.ReasonCode));
        return values;
    }

    private static Dictionary<string, object> ActivityProjection(CompletedActivityEnvelope envelope)
    {
        var values = CorrelationProjection(envelope.EvidenceId, envelope.Host, envelope.Trace,
            envelope.AuditCorrelationId, envelope.ApprovalId);
        values.Add("started_utc", envelope.StartedUtc.UtcTicks);
        values.Add("ended_utc", envelope.EndedUtc.UtcTicks);
        values.Add("outcome", (long)envelope.Outcome);
        return values;
    }

    private static Dictionary<string, object> LinkProjection(CompletedActivityEnvelope owner,
        ActivityLinkEnvelope link, int ordinal) => new(StringComparer.Ordinal)
    {
        ["evidence_id"] = owner.EvidenceId.Value.ToString("D"),
        ["ordinal"] = (long)ordinal,
        ["request_id"] = owner.Host.RequestId.Value.ToString("D"),
        ["session_id"] = owner.Host.SessionId.Value.ToString("D"),
        ["task_id"] = owner.Host.TaskId.Value.ToString("D"),
        ["origin"] = (long)owner.Host.Origin,
        ["invocation_id"] = Identifier(owner.Host.InvocationId?.Value),
        ["audit_correlation_id"] = Identifier(owner.AuditCorrelationId),
        ["approval_id"] = Identifier(owner.ApprovalId),
        ["source_trace_id"] = owner.Trace.TraceId,
        ["source_span_id"] = owner.Trace.SpanId,
        ["trace_id"] = link.TraceId,
        ["span_id"] = link.SpanId,
    };

    private static Dictionary<string, object> CorrelationProjection(HostId<EvidenceIdentity> id, HostRequest? host,
        TraceSnapshot? trace, Guid? correlation, Guid? approval) => new(StringComparer.Ordinal)
    {
        ["evidence_id"] = id.Value.ToString("D"),
        ["request_id"] = Identifier(host?.RequestId.Value),
        ["session_id"] = Identifier(host?.SessionId.Value),
        ["task_id"] = Identifier(host?.TaskId.Value),
        ["origin"] = host is null ? DBNull.Value : (long)host.Origin,
        ["invocation_id"] = Identifier(host?.InvocationId?.Value),
        ["trace_id"] = Text(trace?.TraceId),
        ["span_id"] = Text(trace?.SpanId),
        ["parent_span_id"] = Text(trace?.ParentSpanId),
        ["trace_flags"] = trace is null ? DBNull.Value : (long)trace.Flags,
        ["activity_source"] = Text(trace?.Source),
        ["activity_version"] = Text(trace?.SourceVersion),
        ["activity_name"] = Text(trace?.Name),
        ["activity_kind"] = trace is null ? DBNull.Value : (long)trace.Kind,
        ["audit_correlation_id"] = Identifier(correlation),
        ["approval_id"] = Identifier(approval),
    };

    private static object Identifier(Guid? value) => value is { } id ? id.ToString("D") : DBNull.Value;
    private static object Text(string? value) => value is null ? DBNull.Value : value;

    private static void ValidateLiveContext(HostRequest host, TraceSnapshot? trace, Guid? correlation, Guid? approval)
    {
        var current = HostActivity.RequireCurrent();
        if (current.Activity!.IsStopped || current.Request != host || TraceSnapshot.Capture(current.Activity) != trace
            || current.CorrelationId != correlation || current.ApprovalId != approval)
        {
            throw new InvalidOperationException("The evidence does not match its live host-owned request and activity.");
        }
    }

    private static void ValidateDiagnostic(DiagnosticEnvelope envelope)
    {
        envelope.EvidenceId.Validate();
        if (envelope.SchemaVersion != 1 || envelope.Properties is null || envelope.Scopes is null
            || envelope.Scopes.Count > 8)
        {
            throw new InvalidDataException("The diagnostic schema or scope depth is invalid.");
        }
        Bounded(envelope.Level, 32);
        if (envelope.Level is not ("Trace" or "Debug" or "Information" or "Warning" or "Error" or "Critical"))
        {
            throw new InvalidDataException("The diagnostic level is invalid.");
        }
        Bounded(envelope.Category, 256);
        Bounded(envelope.MessageTemplate, 4096, allowEmpty: true);
        OptionalBounded(envelope.EventName, 128);
        OptionalBounded(envelope.ExceptionType, 256);
        ValidateProperties(envelope.Properties, allowContextMetadata: true);
        foreach (var scope in envelope.Scopes)
        {
            ValidateProperties(scope);
        }
        ValidateOptionalIdentity(envelope.AuditCorrelationId);
        ValidateOptionalIdentity(envelope.ApprovalId);
        if (envelope.Host is null)
        {
            if (envelope.Trace is not null || envelope.AuditCorrelationId is not null || envelope.ApprovalId is not null
                || envelope.Properties.Count(pair => IsContextMetadata(pair.Key, pair.Value)) != 2)
            {
                throw new InvalidDataException("Hostless diagnostics require an explicit bootstrap classification and MissingHostContext gap, without trusted correlation.");
            }
        }
        else
        {
            ValidateHost(envelope.Host);
            ValidateTrace(envelope.Trace ?? throw new InvalidDataException("A host diagnostic requires W3C correlation."));
        }
    }

    private static void ValidateAudit(AuditEnvelope envelope)
    {
        if (envelope.Diagnostic is null || envelope.Audit is null)
        {
            throw new InvalidDataException("A typed audit event and diagnostic envelope are required.");
        }
        ValidateDiagnostic(envelope.Diagnostic);
        if (envelope.Diagnostic.Host is null || envelope.Diagnostic.AuditCorrelationId != envelope.Audit.CorrelationId
            || envelope.Diagnostic.ApprovalId != envelope.Audit.ApprovalId)
        {
            throw new InvalidDataException("An audit event requires matching host, business correlation and approval identities.");
        }
        // Constructor validation remains authoritative, including persisted enum/identifier bounds.
        _ = new Kora.Core.Auditing.SecurityAuditEvent(envelope.Audit.CorrelationId, envelope.Audit.Category,
            envelope.Audit.ActionId, envelope.Audit.Outcome, envelope.Audit.Initiator, envelope.Audit.TargetId,
            envelope.Audit.ApprovalId, envelope.Audit.ReasonCode);
    }

    private static void ValidateActivity(CompletedActivityEnvelope envelope)
    {
        envelope.EvidenceId.Validate();
        ValidateHost(envelope.Host);
        ValidateTrace(envelope.Trace);
        ValidateOptionalIdentity(envelope.AuditCorrelationId);
        ValidateOptionalIdentity(envelope.ApprovalId);
        if (!Enum.IsDefined(envelope.Outcome) || envelope.EndedUtc < envelope.StartedUtc
            || envelope.Links is null || envelope.Links.Count > 32)
        {
            throw new InvalidDataException("The activity outcome, duration or link count is invalid.");
        }
        foreach (var link in envelope.Links)
        {
            if (link is null)
            {
                throw new InvalidDataException("An activity link is missing.");
            }
            ValidateW3C(link.TraceId, 32);
            ValidateW3C(link.SpanId, 16);
        }
    }

    private static void ValidateHost(HostRequest host)
    {
        if (host is null)
        {
            throw new InvalidDataException("A host-owned request is required.");
        }
        host.RequestId.Validate();
        host.SessionId.Validate();
        host.TaskId.Validate();
        host.InvocationId?.Validate();
        if (!Enum.IsDefined(host.Origin))
        {
            throw new InvalidDataException("The host origin is invalid.");
        }
    }

    private static void ValidateTrace(TraceSnapshot trace)
    {
        if (trace is null)
        {
            throw new InvalidDataException("An activity trace is required.");
        }
        ValidateW3C(trace.TraceId, 32);
        ValidateW3C(trace.SpanId, 16);
        if (trace.ParentSpanId is not null)
        {
            ValidateW3C(trace.ParentSpanId, 16);
        }
        if (!Enum.IsDefined(trace.Kind) || trace.Flags is not (ActivityTraceFlags.None or ActivityTraceFlags.Recorded)
            || trace.Source is not ("Kora.Core" or "Kora.Application" or "Kora.Windows" or "Kora.Desktop"))
        {
            throw new InvalidDataException("An activity source, kind or trace flags are invalid.");
        }
        Bounded(trace.Source, 128);
        OptionalBounded(trace.SourceVersion, 128);
        Bounded(trace.Name, 128);
    }

    private static void ValidateW3C(string value, int length)
    {
        if (value is null || value.Length != length || value.All(character => character == '0')
            || value.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
        {
            throw new InvalidDataException("A W3C correlation identifier is invalid.");
        }
    }

    private static bool IsContextMetadata(string name, EvidenceValue? value) => name switch
    {
        BootstrapProperty => value is { Kind: EvidenceValueKind.Boolean, CanonicalValue: "true" or "false" },
        ContextGapProperty => value is { Kind: EvidenceValueKind.Text, CanonicalValue: "MissingHostContext" },
        _ => false,
    };

    private static void ValidateProperties(IReadOnlyDictionary<string, EvidenceValue> properties, bool allowContextMetadata = false)
    {
        if (properties is null || properties.Count > (allowContextMetadata ? 34 : 32)
            || allowContextMetadata && properties.Count - properties.Count(pair => IsContextMetadata(pair.Key, pair.Value)) > 32)
        {
            throw new InvalidDataException("The structured property count exceeds its bound.");
        }
        foreach (var pair in properties)
        {
            Bounded(pair.Key, 128);
            var value = pair.Value ?? throw new InvalidDataException("A structured property value is missing.");
            EvidenceFieldPolicy.ValidateValue(value);
        }
    }

    private static void ValidateOptionalIdentity(Guid? id)
    {
        if (id == Guid.Empty)
        {
            throw new InvalidDataException("An optional business correlation identity cannot be empty.");
        }
    }

    private static void Bounded(string value, int maximum, bool allowEmpty = false)
    {
        if (value is null || value.Length > maximum || !allowEmpty && string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException("An evidence field is missing or exceeds its bound.");
        }
    }

    private static void OptionalBounded(string? value, int maximum, bool allowEmpty = false)
    {
        if (value is not null)
        {
            Bounded(value, maximum, allowEmpty);
        }
    }

    private static string Serialize<T>(T envelope)
    {
        var payload = JsonSerializer.Serialize(envelope, Serialization);
        if (Encoding.UTF8.GetByteCount(payload) > MaximumEnvelopeBytes)
        {
            throw new InvalidDataException("The serialized evidence envelope exceeds its byte bound.");
        }
        return payload;
    }

    private static string[] BuildSchema()
    {
        const string diagnosticColumns = """
            schema_version INTEGER NOT NULL CHECK(schema_version=1), observed_utc INTEGER NOT NULL,
            event_id INTEGER NOT NULL CHECK(event_id BETWEEN -2147483648 AND 2147483647),
            event_name TEXT CHECK(length(event_name)<=128), level TEXT NOT NULL CHECK(length(level)<=32),
            category TEXT NOT NULL CHECK(length(category)<=256),
            message_template TEXT NOT NULL CHECK(length(message_template)<=4096),
            exception_type TEXT CHECK(length(exception_type)<=256)
            """;
        var schema = new List<string>
        {
            $"CREATE TABLE application_log_events(evidence_id TEXT PRIMARY KEY NOT NULL CHECK(length(evidence_id)=36),{diagnosticColumns},{CorrelationColumns},{PayloadColumns}) STRICT",
            $"""
            CREATE TABLE security_audit_events(
                audit_sequence INTEGER PRIMARY KEY AUTOINCREMENT,
                evidence_id TEXT UNIQUE NOT NULL CHECK(length(evidence_id)=36),
                {diagnosticColumns},{CorrelationColumns},
                audit_category INTEGER NOT NULL CHECK(audit_category BETWEEN 0 AND 5),
                action_id TEXT NOT NULL CHECK(length(action_id) BETWEEN 1 AND 128),
                audit_outcome INTEGER NOT NULL CHECK(audit_outcome BETWEEN 0 AND 5),
                initiator INTEGER NOT NULL CHECK(initiator BETWEEN 0 AND 4),
                target_id TEXT NOT NULL CHECK(length(target_id) BETWEEN 1 AND 128),
                reason_code TEXT CHECK(length(reason_code) BETWEEN 1 AND 128),{PayloadColumns}) STRICT
            """,
            $"""
            CREATE TABLE activity_spans(evidence_id TEXT PRIMARY KEY NOT NULL CHECK(length(evidence_id)=36),
                {CorrelationColumns},started_utc INTEGER NOT NULL,
                ended_utc INTEGER NOT NULL CHECK(ended_utc>=started_utc),
                outcome INTEGER NOT NULL CHECK(outcome BETWEEN 0 AND 3),{PayloadColumns}) STRICT
            """,
            $"""
            CREATE TABLE activity_links(
                evidence_id TEXT NOT NULL REFERENCES activity_spans(evidence_id) CHECK(length(evidence_id)=36),
                ordinal INTEGER NOT NULL CHECK(ordinal BETWEEN 0 AND 31),
                request_id TEXT NOT NULL CHECK(length(request_id)=36),
                session_id TEXT NOT NULL CHECK(length(session_id)=36),
                task_id TEXT NOT NULL CHECK(length(task_id)=36),
                origin INTEGER NOT NULL CHECK(origin BETWEEN 0 AND 2),
                invocation_id TEXT CHECK(length(invocation_id)=36),
                audit_correlation_id TEXT CHECK(length(audit_correlation_id)=36),
                approval_id TEXT CHECK(length(approval_id)=36),
                source_trace_id TEXT NOT NULL CHECK(length(source_trace_id)=32),
                source_span_id TEXT NOT NULL CHECK(length(source_span_id)=16),
                trace_id TEXT NOT NULL CHECK(length(trace_id)=32), span_id TEXT NOT NULL CHECK(length(span_id)=16),
                {PayloadColumns},
                PRIMARY KEY(evidence_id,ordinal)) STRICT
            """,
        };
        foreach (var table in new[] { "application_log_events", "security_audit_events", "activity_spans" })
        {
            foreach (var field in new[] { "request_id", "session_id", "task_id", "invocation_id", "audit_correlation_id",
                "approval_id", "trace_id", "span_id", "due_utc" })
            {
                schema.Add($"CREATE INDEX ix_{table}_{field} ON {table}({field})");
            }
        }
        schema.Add("CREATE INDEX ix_activity_links_cause ON activity_links(trace_id,span_id)");
        foreach (var field in new[] { "request_id", "session_id", "task_id", "invocation_id", "audit_correlation_id", "approval_id" })
        {
            schema.Add($"CREATE INDEX ix_activity_links_{field} ON activity_links({field})");
        }
        schema.Add("CREATE INDEX ix_activity_links_source ON activity_links(source_trace_id,source_span_id)");
        schema.Add("CREATE INDEX ix_activity_links_due ON activity_links(due_utc)");
        return schema.ToArray();
    }
}
