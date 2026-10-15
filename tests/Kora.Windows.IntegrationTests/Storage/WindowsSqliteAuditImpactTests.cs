using System.Text.Json;
using AwesomeAssertions;
using Kora.Application.Auditing;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(ProcessGlobalEvidenceTestGroup))]
public sealed class WindowsSqliteAuditImpactTests
{
    [Fact]
    public async Task Real_committed_sources_are_separate_content_free_and_byte_preserving_including_zero()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: fixture.Time);
        sink.Initialize();
        WriteProjection(fixture, sink);
        var authorityBytes = File.ReadAllBytes(fixture.DatabasePath);
        var projectionPath = Path.Combine(fixture.Paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db");
        var projectionBytes = File.ReadAllBytes(projectionPath);
        var journalBytes = File.ReadAllBytes(fixture.DatabasePath + "-journal");
        var cutoff = fixture.Time.Now.AddDays(40);
        var authority = fixture.Store.ReadAuditImpact(new(30), cutoff, fixture.Token);
        var projection = sink.ReadAuditImpact(new(30), cutoff, fixture.Token);
        authority.Status.Should().Be(AuditImpactStatus.Complete);
        authority.Aggregate!.AffectedCount.Should().BeGreaterThan(0);
        projection.Aggregate!.AffectedCount.Should().Be(1);
        var zero = sink.ReadAuditImpact(new(30), fixture.Time.Now, fixture.Token);
        zero.Aggregate.Should().Be(new AuditImpactAggregate(0, null, null, null, null));
        JsonSerializer.Serialize(projection.Aggregate).Should().NotContain("sentinel-private").And.NotContain("session")
            .And.NotContain("event").And.NotContain("properties").And.NotContain(fixture.Paths.LocalRoot);
        File.ReadAllBytes(fixture.DatabasePath).Should().Equal(authorityBytes);
        File.ReadAllBytes(projectionPath).Should().Equal(projectionBytes);
        File.ReadAllBytes(fixture.DatabasePath + "-journal").Should().Equal(journalBytes);
        using var reader = new WindowsSqliteAuditImpactReader(fixture.Store, sink, fixture.Paths);
        reader.IsCurrent(authority).Should().BeTrue();
        reader.IsCurrent(projection).Should().BeTrue();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        (await reader.ReadAsync(AuditImpactSource.RequiredAuthority, new(30), cutoff, request, fixture.Token))
            .Aggregate.Should().Be(authority.Aggregate);
        var forbidden = () => reader.ReadAsync(AuditImpactSource.RequiredAuthority, new(30), cutoff,
            HostRequest.Create(RequestOrigin.ActivatedVoice), fixture.Token);
        forbidden.Should().Throw<InvalidOperationException>();
        fixture.Mutate("UPDATE authority_head SET hash='" + new string('0', 64) + "';");
        reader.IsCurrent(authority).Should().BeFalse();
        (await reader.ReadAsync(AuditImpactSource.RequiredAuthority, new(30), cutoff, request, fixture.Token))
            .Should().Be(new AuditImpactResult(AuditImpactSource.RequiredAuthority, AuditImpactStatus.Unavailable, null, null));
        (await reader.ReadAsync(AuditImpactSource.DiagnosticProjection, new(30), cutoff, request, fixture.Token))
            .Status.Should().Be(AuditImpactStatus.Complete);
    }

    [Fact]
    public async Task Production_observation_trace_and_notifications_never_commit_a_self_invalidating_span_or_audit()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var policy = new AuditRetentionPolicy();
        var preferences = new LocalAuditRetentionPreferences(fixture.Paths);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: fixture.Time);
        sink.Initialize();
        WriteProjection(fixture, sink);
        using var provider = new EvidenceLoggerProvider([sink], new IgnoredGaps());
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        await using var admission = new AuditRetentionAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        var configuration = new AuditRetentionConfigurationService(preferences, policy, admission,
            new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>()));
        configuration.Observe();
        using var reader = new WindowsSqliteAuditImpactReader(fixture.Store, sink, fixture.Paths);
        using var preview = new AuditImpactPreviewService(reader, preferences, configuration, fixture.Time);
        var path = Path.Combine(fixture.Paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db");
        var authority = File.ReadAllBytes(fixture.DatabasePath);
        var projection = File.ReadAllBytes(path);
        fixture.Time.Now = fixture.Time.Now.AddDays(40);
        await preview.PreviewAsync("30", static () => true, fixture.Token);
        preview.Get().RequiredAuthority!.Status.Should().Be(AuditImpactStatus.Complete);
        preview.Get().DiagnosticProjection!.Aggregate!.AffectedCount.Should().Be(1);
        File.ReadAllBytes(fixture.DatabasePath).Should().Equal(authority);
        File.ReadAllBytes(path).Should().Equal(projection);
        preferences.Load().Should().BeNull();
    }

    private sealed class IgnoredGaps : IEvidenceGapReporter
    {
        public void Report(EvidenceGap gap) => throw new InvalidOperationException("Observation must not emit a storage gap.");
    }

    [Theory]
    [InlineData("due")]
    [InlineData("payload")]
    [InlineData("columns")]
    [InlineData("sequence")]
    [InlineData("duplicate")]
    public async Task Unqualified_projection_never_becomes_a_zero_count_or_authority(string corruption)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: fixture.Time);
        sink.Initialize();
        WriteProjection(fixture, sink);
        var path = Path.Combine(fixture.Paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=PERSIST;" + (corruption switch
            {
                "due" => "UPDATE security_audit_events SET due_utc=committed_utc+1;",
                "payload" => "UPDATE security_audit_events SET envelope='{}';",
                "columns" => "UPDATE security_audit_events SET event_name='forged';",
                "duplicate" => "UPDATE security_audit_events SET envelope=substr(envelope,1,length(envelope)-1)||',\"Audit\":null}';",
                _ => "DELETE FROM security_audit_events;",
            });
            command.ExecuteNonQuery();
        }
        using var reader = new WindowsSqliteAuditImpactReader(fixture.Store, sink, fixture.Paths);
        var result = await reader.ReadAsync(AuditImpactSource.DiagnosticProjection, new(30), fixture.Time.Now.AddDays(40),
            HostRequest.Create(RequestOrigin.LocalUi), fixture.Token);
        result.Status.Should().Be(AuditImpactStatus.Unavailable);
        result.Aggregate.Should().BeNull();
        fixture.Store.ReadAuditImpact(new(30), fixture.Time.Now.AddDays(40), fixture.Token)
            .Status.Should().Be(AuditImpactStatus.Complete);
    }

    [Fact]
    public async Task Missing_and_unknown_stores_are_not_created_or_replaced_by_preview()
    {
        using var fixture = new InteractionStorageFixture();
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths);
        var read = () => sink.ReadAuditImpact(new(30), fixture.Time.Now, fixture.Token);
        read.Should().Throw<IOException>();
        var authority = () => fixture.Store.ReadAuditImpact(new(30), fixture.Time.Now, fixture.Token);
        authority.Should().Throw<IOException>();
        File.Exists(fixture.DatabasePath).Should().BeFalse();
        await fixture.InitializeAsync();
        sink.Initialize();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var reader = new WindowsSqliteAuditImpactReader(fixture.Store, sink, fixture.Paths);
        var action = () => reader.ReadAsync(AuditImpactSource.RequiredAuthority, new(30), fixture.Time.Now,
            HostRequest.Create(RequestOrigin.LocalUi), cancelled.Token).AsTask();
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Real_bounded_projection_scan_reports_incomplete_without_a_partial_count()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: fixture.Time);
        sink.Initialize();
        WriteProjection(fixture, sink);
        var path = Path.Combine(fixture.Paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=PERSIST;";
            command.ExecuteNonQuery();
            command.CommandText = "SELECT envelope FROM security_audit_events WHERE audit_sequence=1;";
            var template = JsonSerializer.Deserialize<AuditEnvelope>((string)command.ExecuteScalar()!)!;
            command.CommandText = "PRAGMA table_info(security_audit_events);";
            var columns = new List<string>();
            using (var rows = command.ExecuteReader())
            {
                while (rows.Read())
                {
                    var name = rows.GetString(1);
                    if (!string.Equals(name, "audit_sequence", StringComparison.Ordinal)) { columns.Add(name); }
                }
            }
            using var transaction = connection.BeginTransaction();
            command.Transaction = transaction;
            command.CommandText = $"INSERT INTO security_audit_events({string.Join(',', columns)}) SELECT "
                + string.Join(',', columns.Select(name => name switch { "evidence_id" => "$id", "envelope" => "$payload", _ => name }))
                + " FROM security_audit_events WHERE audit_sequence=1;";
            var id = command.Parameters.Add("$id", SqliteType.Text);
            var payload = command.Parameters.Add("$payload", SqliteType.Text);
            for (var index = 0; index < AuditRetentionImpact.MaximumRows; index++)
            {
                var identity = Guid.NewGuid();
                id.Value = identity.ToString("D");
                payload.Value = JsonSerializer.Serialize(template with { Diagnostic = template.Diagnostic with { EvidenceId = new(identity) } });
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        var before = File.ReadAllBytes(path);
        var result = sink.ReadAuditImpact(new(30), fixture.Time.Now.AddDays(40), fixture.Token);
        result.Status.Should().Be(AuditImpactStatus.Incomplete);
        result.Aggregate.Should().BeNull();
        File.ReadAllBytes(path).Should().Equal(before);
    }

    private static void WriteProjection(InteractionStorageFixture fixture, WindowsSqliteEvidenceSink sink)
    {
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            "fixture.audit-preview", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser, "sentinel-private");
        using var host = HostActivity.BeginAudit(HostRequest.Create(RequestOrigin.LocalUi), audit);
        var diagnostic = new DiagnosticEnvelope(1, new(Guid.NewGuid()), fixture.Time.Now, 1, "sentinel-private",
            "Information", "Kora.Tests", "sentinel-private",
            new Dictionary<string, EvidenceValue>(StringComparer.Ordinal) { ["SecurityAudit"] = new(EvidenceValueKind.Boolean, "true") },
            [], TraceSnapshot.Capture(host.Activity), host.Request, null, audit.CorrelationId, audit.ApprovalId);
        sink.WriteDiagnostic(diagnostic);
        sink.WriteAudit(new(diagnostic with { EvidenceId = new(Guid.NewGuid()) }, audit));
    }
}
