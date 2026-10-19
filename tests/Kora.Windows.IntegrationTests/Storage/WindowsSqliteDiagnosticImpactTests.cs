using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(ProcessGlobalEvidenceTestGroup))]
public sealed partial class WindowsSqliteDiagnosticImpactTests
{
    [WindowsFact]
    public async Task Actual_commit_deadlines_and_owned_links_not_observed_age_or_link_targets_define_newly_due_impact()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var policy = new DiagnosticRetentionPolicy();
        policy.Activate(new(365));
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: fixture.Time, diagnosticPolicy: policy);
        sink.Initialize();
        var committed = fixture.Time.Now;
        using (var host = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteDiagnostic(Diagnostic(host, committed));
            // This remains an ordinary log. The property cannot classify a row as authoritative audit.
            sink.WriteDiagnostic(Diagnostic(host, committed) with
            {
                Properties = new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
                {
                    ["SecurityAudit"] = new(EvidenceValueKind.Boolean, "true"),
                },
            });
            WriteSpan(sink, host, committed, 32);
        }
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            "fixture.diagnostic-impact", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser, "sentinel-private");
        using (var host = HostActivity.BeginAudit(fixture.Request, audit))
        {
            sink.WriteAudit(new(Diagnostic(host, committed), audit));
        }
        fixture.Time.Now = committed.AddDays(1);
        policy.Activate(new(1));
        using (var host = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteDiagnostic(Diagnostic(host, fixture.Time.Now));
            WriteSpan(sink, host, fixture.Time.Now, 2);
        }
        fixture.Time.Now = committed.AddDays(2);
        policy.Activate(new(30));
        using (var host = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteDiagnostic(Diagnostic(host, fixture.Time.Now));
            WriteSpan(sink, host, fixture.Time.Now, 0);
        }
        var database = File.ReadAllBytes(DatabasePath(fixture.Paths));
        var journal = File.ReadAllBytes(DatabasePath(fixture.Paths) + "-journal");
        var authority = File.ReadAllBytes(fixture.DatabasePath);
        var cacheEntries = sink.PersistedEnvelopeCacheEntryCount;
        var decodes = sink.PersistedEnvelopeDecodeCount;
        var result = sink.ReadDiagnosticImpact(new(1), fixture.Time.Now, fixture.Token);
        result.Logs.Aggregate.Should().Be(new DiagnosticImpactAggregate(2, committed, committed,
            committed.AddDays(1), committed.AddDays(1)));
        result.Spans.Aggregate!.NewlyDueCount.Should().Be(1);
        result.Links.Aggregate!.NewlyDueCount.Should().Be(32);
        DiagnosticRetentionImpact.Validate(result, new(1), fixture.Time.Now);
        var zero = sink.ReadDiagnosticImpact(new(365), fixture.Time.Now, fixture.Token);
        zero.Logs.Aggregate.Should().Be(new DiagnosticImpactAggregate(0, null, null, null, null));
        zero.Spans.Aggregate!.NewlyDueCount.Should().Be(0);
        zero.Links.Aggregate!.NewlyDueCount.Should().Be(0);
        var publicShape = JsonSerializer.Serialize(new { result.Logs, result.Spans, result.Links });
        publicShape.Should().NotContain("sentinel-private").And.NotContain("SecurityAudit").And.NotContain("request")
            .And.NotContain("evidence").And.NotContain(fixture.Paths.LocalRoot);
        sink.PersistedEnvelopeCacheEntryCount.Should().Be(cacheEntries);
        sink.PersistedEnvelopeDecodeCount.Should().Be(decodes);
        File.ReadAllBytes(DatabasePath(fixture.Paths)).Should().Equal(database);
        File.ReadAllBytes(DatabasePath(fixture.Paths) + "-journal").Should().Equal(journal);
        File.ReadAllBytes(fixture.DatabasePath).Should().Equal(authority);
        // Exact proposed-due equality counts; exact original-due equality does not.
        sink.ReadDiagnosticImpact(new(1), committed.AddDays(1).AddTicks(-1), fixture.Token)
            .Logs.Status.Should().Be(DiagnosticImpactStatus.Unavailable); // Later commits cannot be silently ignored.
    }

    [Theory]
    [InlineData("log-envelope")]
    [InlineData("log-columns")]
    [InlineData("log-deadline")]
    [InlineData("log-duplicate")]
    [InlineData("span-envelope")]
    [InlineData("link-missing")]
    [InlineData("link-orphan")]
    [InlineData("link-deadline")]
    [InlineData("link-payload")]
    [InlineData("link-column")]
    public async Task Malformed_irrelevant_prefix_rows_and_unknown_linkage_suppress_only_independently_qualified_sources(string corruption)
    {
        using var fixture = new InteractionStorageFixture();
        var sink = Seed(fixture);
        Mutate(fixture.Paths, corruption switch
        {
            "log-envelope" => "UPDATE application_log_events SET envelope='{}';",
            "log-columns" => "UPDATE application_log_events SET event_name='forged';",
            "log-deadline" => "UPDATE application_log_events SET due_utc=committed_utc+1;",
            "log-duplicate" => "UPDATE application_log_events SET envelope=substr(envelope,1,length(envelope)-1)||',\"SchemaVersion\":1}';",
            "span-envelope" => "UPDATE activity_spans SET envelope='{}';",
            "link-missing" => "DELETE FROM activity_links WHERE ordinal=0;",
            "link-orphan" => "PRAGMA foreign_keys=OFF; DELETE FROM activity_spans;",
            "link-deadline" => "UPDATE activity_links SET due_utc=due_utc+1;",
            "link-payload" => "UPDATE activity_links SET envelope='{}';",
            _ => "UPDATE activity_links SET span_id='1111111111111111';",
        });
        // Nothing is newly due at this cutoff: malformed rows must still be validated, not SQL-filtered away.
        var result = sink.ReadDiagnosticImpact(new(1), fixture.Time.Now, fixture.Token);
        var logFault = corruption.StartsWith("log-", StringComparison.Ordinal);
        result.Logs.Status.Should().Be(logFault ? DiagnosticImpactStatus.Unavailable : DiagnosticImpactStatus.Complete);
        result.Spans.Status.Should().Be(logFault ? DiagnosticImpactStatus.Complete : DiagnosticImpactStatus.Unavailable);
        result.Links.Status.Should().Be(result.Spans.Status);
        if (logFault) { result.Logs.Aggregate.Should().BeNull(); }
        else { result.Spans.Aggregate.Should().BeNull(); result.Links.Aggregate.Should().BeNull(); }
    }

    [Theory]
    [InlineData("audit-envelope")]
    [InlineData("audit-prefix")]
    [InlineData("audit-deadline")]
    [InlineData("schema")]
    [InlineData("privacy")]
    public async Task Unknown_protected_prefix_schema_or_private_state_is_unavailable_never_a_zero(string reason)
    {
        using var fixture = new InteractionStorageFixture();
        var sink = Seed(fixture);
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            "fixture.protected", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser, "sentinel-private");
        using (var host = HostActivity.BeginAudit(fixture.Request, audit))
        {
            sink.WriteAudit(new(Diagnostic(host, fixture.Time.Now), audit));
        }
        using var reader = new WindowsSqliteDiagnosticImpactReader(sink, fixture.Paths);
        if (string.Equals(reason, "audit-envelope", StringComparison.Ordinal)) { Mutate(fixture.Paths, "UPDATE security_audit_events SET envelope='{}';"); }
        if (string.Equals(reason, "audit-prefix", StringComparison.Ordinal)) { Mutate(fixture.Paths, "DELETE FROM security_audit_events;"); }
        if (string.Equals(reason, "audit-deadline", StringComparison.Ordinal)) { Mutate(fixture.Paths, "UPDATE security_audit_events SET due_utc=committed_utc+1;"); }
        if (string.Equals(reason, "schema", StringComparison.Ordinal)) { Mutate(fixture.Paths, "PRAGMA user_version=99;"); }
        DirectorySecurity? original = null;
        var directory = new DirectoryInfo(Path.GetDirectoryName(DatabasePath(fixture.Paths))!);
        try
        {
            if (string.Equals(reason, "privacy", StringComparison.Ordinal))
            {
                original = directory.GetAccessControl(AccessControlSections.Access);
                var changed = directory.GetAccessControl(AccessControlSections.Access);
                changed.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                directory.SetAccessControl(changed);
            }
            var result = await reader.ReadAsync(new(1), fixture.Time.Now.AddDays(2),
                HostRequest.Create(RequestOrigin.LocalUi), fixture.Token);
            result.Should().Be(DiagnosticImpactSnapshot.Unavailable);
        }
        finally { if (original is not null) { directory.SetAccessControl(original); } }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Finite_row_and_encoded_byte_overflow_discard_partial_aggregates_without_writes(bool bytes)
    {
        using var fixture = new InteractionStorageFixture();
        var sink = Seed(fixture, large: bytes);
        CloneLogs(fixture.Paths, bytes ? 160 : DiagnosticRetentionImpact.MaximumRows - 1);
        if (!bytes)
        {
            sink.ReadDiagnosticImpact(new(1), fixture.Time.Now.AddDays(2), fixture.Token)
                .Logs.Aggregate!.NewlyDueCount.Should().Be(DiagnosticRetentionImpact.MaximumRows);
            CloneLogs(fixture.Paths, 1);
        }
        var before = File.ReadAllBytes(DatabasePath(fixture.Paths));
        var journal = File.ReadAllBytes(DatabasePath(fixture.Paths) + "-journal");
        var result = sink.ReadDiagnosticImpact(new(1), fixture.Time.Now.AddDays(2), fixture.Token);
        result.Logs.Status.Should().Be(DiagnosticImpactStatus.Incomplete);
        result.Logs.Aggregate.Should().BeNull();
        result.Spans.Status.Should().Be(DiagnosticImpactStatus.Complete);
        File.ReadAllBytes(DatabasePath(fixture.Paths)).Should().Equal(before);
        File.ReadAllBytes(DatabasePath(fixture.Paths) + "-journal").Should().Equal(journal);
    }

    [WindowsFact]
    public async Task Reader_is_native_only_currentness_is_generation_bound_and_cancellation_never_changes_bytes()
    {
        using var fixture = new InteractionStorageFixture();
        var sink = Seed(fixture);
        using var reader = new WindowsSqliteDiagnosticImpactReader(sink, fixture.Paths);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var result = await reader.ReadAsync(new(1), fixture.Time.Now.AddDays(2), request, fixture.Token);
        result.Logs.Status.Should().Be(DiagnosticImpactStatus.Complete);
        reader.IsCurrent(result).Should().BeTrue();
        var forbidden = () => reader.ReadAsync(new(1), fixture.Time.Now, HostRequest.Create(RequestOrigin.ActivatedVoice), fixture.Token);
        forbidden.Should().Throw<InvalidOperationException>();
        var before = File.ReadAllBytes(DatabasePath(fixture.Paths));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await ((Func<Task>)(async () => { await reader.ReadAsync(new(1), fixture.Time.Now, request, cancellation.Token); }))
            .Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllBytes(DatabasePath(fixture.Paths)).Should().Equal(before);
        // A committed mutation and reversal cannot revive a source snapshot.
        Mutate(fixture.Paths, "UPDATE application_log_events SET event_name='other';");
        Mutate(fixture.Paths, "UPDATE application_log_events SET event_name='sentinel-private';");
        reader.IsCurrent(result).Should().BeFalse();
        reader.Dispose();
        reader.IsCurrent(result).Should().BeFalse();
        var closed = () => reader.ReadAsync(new(1), fixture.Time.Now, request, fixture.Token);
        closed.Should().Throw<ObjectDisposedException>();
    }

    [WindowsFact]
    public async Task Production_observation_success_and_failure_never_write_evidence_authority_preferences_or_self_invalidating_spans()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var sink = Seed(fixture);
        var policy = new DiagnosticRetentionPolicy();
        var preferences = new LocalDiagnosticRetentionPreferences(fixture.Paths);
        await using var admission = new DiagnosticRetentionAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        var configuration = new DiagnosticRetentionConfigurationService(preferences, policy, admission, new NoAuditWrites());
        configuration.Observe();
        using var reader = new WindowsSqliteDiagnosticImpactReader(sink, fixture.Paths);
        using var preview = new DiagnosticImpactPreviewService(reader, preferences, configuration, fixture.Time);
        using var hostileHost = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        using var provider = new EvidenceLoggerProvider([sink], new NoGaps());
        var bytes = File.ReadAllBytes(DatabasePath(fixture.Paths));
        var journal = File.ReadAllBytes(DatabasePath(fixture.Paths) + "-journal");
        var authority = File.ReadAllBytes(fixture.DatabasePath);
        fixture.Time.Now = fixture.Time.Now.AddDays(2);
        await preview.PreviewAsync("1", static () => true, fixture.Token);
        preview.Get().Result!.Logs.Aggregate!.NewlyDueCount.Should().Be(1);
        Activity.Current.Should().BeSameAs(hostileHost.Activity);
        var eligible = true;
        preview.Changed += (_, _) => eligible = false;
        await ((Func<Task>)(() => preview.PreviewAsync("1", () => eligible, fixture.Token)))
            .Should().ThrowAsync<InvalidOperationException>();
        preview.Get().Result.Should().BeNull();
        File.ReadAllBytes(DatabasePath(fixture.Paths)).Should().Equal(bytes);
        File.ReadAllBytes(DatabasePath(fixture.Paths) + "-journal").Should().Equal(journal);
        File.ReadAllBytes(fixture.DatabasePath).Should().Equal(authority);
        preferences.Load().Should().BeNull();
        policy.Effective.Should().Be(DiagnosticRetentionDays.Default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bounded_owned_link_group_or_preserved_protected_prefix_never_leaks_partial_counts(bool protectedPrefix)
    {
        using var fixture = new InteractionStorageFixture();
        var sink = Seed(fixture);
        if (protectedPrefix)
        {
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                "fixture.protected-bound", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser, "sentinel-private");
            using var host = HostActivity.BeginAudit(fixture.Request, audit);
            sink.WriteAudit(new(Diagnostic(host, fixture.Time.Now), audit));
            CloneRows(fixture.Paths, "security_audit_events", DiagnosticRetentionImpact.MaximumRows);
        }
        else
        {
            // Seed owner + two links consumes three admissions. Exceed the shared group budget,
            // while the independent ordinary-log source remains fully qualified.
            CloneRows(fixture.Paths, "activity_spans", DiagnosticRetentionImpact.MaximumRows / 3);
        }
        var before = File.ReadAllBytes(DatabasePath(fixture.Paths));
        var result = sink.ReadDiagnosticImpact(new(1), fixture.Time.Now.AddDays(2), fixture.Token);
        result.Spans.Status.Should().Be(DiagnosticImpactStatus.Incomplete);
        result.Links.Status.Should().Be(DiagnosticImpactStatus.Incomplete);
        result.Spans.Aggregate.Should().BeNull();
        result.Links.Aggregate.Should().BeNull();
        result.Logs.Status.Should().Be(protectedPrefix ? DiagnosticImpactStatus.Incomplete : DiagnosticImpactStatus.Complete);
        File.ReadAllBytes(DatabasePath(fixture.Paths)).Should().Equal(before);
    }

    [WindowsFact]
    public void Missing_partition_is_not_created_and_unknown_version_is_not_migrated()
    {
        using var fixture = new InteractionStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        var read = () => sink.ReadDiagnosticImpact(new(1), fixture.Time.Now, fixture.Token);
        read.Should().Throw<FileNotFoundException>();
        Directory.Exists(Path.GetDirectoryName(DatabasePath(fixture.Paths))).Should().BeFalse();
        sink.Initialize();
        Mutate(fixture.Paths, "PRAGMA user_version=1;");
        var before = File.ReadAllBytes(DatabasePath(fixture.Paths));
        read.Should().Throw<InvalidDataException>();
        File.ReadAllBytes(DatabasePath(fixture.Paths)).Should().Equal(before);
    }

    private static WindowsSqliteEvidenceSink Seed(InteractionStorageFixture fixture, bool large = false)
    {
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: fixture.Time);
        sink.Initialize();
        using var host = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var diagnostic = Diagnostic(host, fixture.Time.Now);
        if (large)
        {
            diagnostic = diagnostic with
            {
                Scopes = Enumerable.Range(0, 8).Select(_ =>
                    (IReadOnlyDictionary<string, EvidenceValue>)Enumerable.Range(0, 4).ToDictionary(
                        index => "field" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        _ => new EvidenceValue(EvidenceValueKind.Text, new string('x', 1024)), StringComparer.Ordinal)).ToArray(),
            };
        }
        sink.WriteDiagnostic(diagnostic);
        WriteSpan(sink, host, fixture.Time.Now, 2);
        return sink;
    }

    private static DiagnosticEnvelope Diagnostic(HostActivity host, DateTimeOffset committed) => new(1, new(Guid.NewGuid()),
        committed.AddYears(-10), 1, "sentinel-private", "Information", "Kora.Tests", "sentinel-private",
        new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), [], TraceSnapshot.Capture(host.Activity),
        host.Request, null, host.CorrelationId, host.ApprovalId);

    private static void WriteSpan(WindowsSqliteEvidenceSink sink, HostActivity host, DateTimeOffset committed, int links) =>
        sink.WriteActivity(new(new(Guid.NewGuid()), TraceSnapshot.Capture(host.Activity)!, host.Request, committed, committed,
            HostOperationOutcome.Completed, Enumerable.Range(0, links).Select(_ =>
                new ActivityLinkEnvelope(ActivityTraceId.CreateRandom().ToHexString(), ActivitySpanId.CreateRandom().ToHexString())).ToArray()));

    private static string DatabasePath(OwnedStorageFixture paths) => Path.Combine(paths.LocalRoot,
        WindowsSqliteEvidenceSink.PartitionName, "evidence.db");
    private static SqliteConnection Open(OwnedStorageFixture paths)
        => OpenDatabase(DatabasePath(paths));

    private static SqliteConnection OpenDatabase(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=PERSIST;";
        command.ExecuteNonQuery();
        return connection;
    }
    private static void Mutate(OwnedStorageFixture paths, string sql)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
    private static void CloneLogs(OwnedStorageFixture paths, int count)
    {
        using var connection = Open(paths);
        CloneLogs(connection, count);
    }

    private static void CloneLogs(SqliteConnection connection, int count)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT envelope FROM application_log_events LIMIT 1;";
        var original = WindowsSqliteEvidenceSink.DecodeDiagnostic((string)command.ExecuteScalar()!);
        command.CommandText = "PRAGMA table_info(application_log_events);";
        var columns = new List<string>();
        using (var rows = command.ExecuteReader()) { while (rows.Read()) { columns.Add(rows.GetString(1)); } }
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO application_log_events({string.Join(',', columns)}) SELECT "
            + string.Join(',', columns.Select(name => name switch { "evidence_id" => "$id", "envelope" => "$payload", _ => name }))
            + " FROM application_log_events LIMIT 1;";
        var identity = command.Parameters.Add("$id", SqliteType.Text);
        var payload = command.Parameters.Add("$payload", SqliteType.Text);
        for (var index = 0; index < count; index++)
        {
            var id = Guid.NewGuid();
            identity.Value = id.ToString("D");
            payload.Value = JsonSerializer.Serialize(original with { EvidenceId = new(id) });
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    private static void CloneRows(OwnedStorageFixture paths, string table, int count)
    {
        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT evidence_id,envelope FROM {table} LIMIT 1;";
        string originalId;
        string originalPayload;
        using (var row = command.ExecuteReader())
        {
            row.Read().Should().BeTrue();
            originalId = row.GetString(0);
            originalPayload = row.GetString(1);
        }
        var ownerColumns = Columns(connection, table, excludeAuditSequence: true);
        var linkColumns = Columns(connection, "activity_links", excludeAuditSequence: false);
        using var transaction = connection.BeginTransaction();
        command.Transaction = transaction;
        var identity = command.Parameters.Add("$id", SqliteType.Text);
        var payload = command.Parameters.Add("$payload", SqliteType.Text);
        command.Parameters.AddWithValue("$original", originalId);
        for (var index = 0; index < count; index++)
        {
            var id = Guid.NewGuid().ToString("D");
            identity.Value = id;
            payload.Value = originalPayload.Replace(originalId, id, StringComparison.Ordinal);
            command.CommandText = $"INSERT INTO {table}({string.Join(',', ownerColumns)}) SELECT "
                + string.Join(',', ownerColumns.Select(name => name switch { "evidence_id" => "$id", "envelope" => "$payload", _ => name }))
                + $" FROM {table} WHERE evidence_id=$original;";
            command.ExecuteNonQuery();
            if (string.Equals(table, "activity_spans", StringComparison.Ordinal))
            {
                command.CommandText = $"INSERT INTO activity_links({string.Join(',', linkColumns)}) SELECT "
                    + string.Join(',', linkColumns.Select(name => string.Equals(name, "evidence_id", StringComparison.Ordinal) ? "$id" : name))
                    + " FROM activity_links WHERE evidence_id=$original;";
                command.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    private static List<string> Columns(SqliteConnection connection, string table, bool excludeAuditSequence)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";
        using var rows = command.ExecuteReader();
        var columns = new List<string>();
        while (rows.Read())
        {
            var name = rows.GetString(1);
            if (!excludeAuditSequence || !string.Equals(name, "audit_sequence", StringComparison.Ordinal)) { columns.Add(name); }
        }
        return columns;
    }

    private sealed class NoAuditWrites : ISecurityAuditLog
    {
        public void Write(SecurityAuditEvent auditEvent) => throw new InvalidOperationException("Preview cannot write audit.");
    }
    private sealed class NoGaps : IEvidenceGapReporter
    {
        public void Report(EvidenceGap gap) => throw new InvalidOperationException("Preview cannot emit a storage gap.");
    }
}
