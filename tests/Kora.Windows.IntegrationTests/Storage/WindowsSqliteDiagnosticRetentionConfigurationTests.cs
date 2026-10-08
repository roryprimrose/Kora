using System.Diagnostics;
using System.Security.Cryptography;
using AwesomeAssertions;
using Kora.Application.Auditing;
using Kora.Application.Communication;
using Kora.Application.Configuration;
using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteDiagnosticRetentionConfigurationTests
{
    private static readonly string[] AuthorityPartitions = ["HostStorageV1", "InteractionStorageV1"];
    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task Genuine_independent_retention_session_required_typed_audit_atomic_receipt_and_cold_policy_readback(
        RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var grant = await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var original = await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token);
        var policy = new DiagnosticRetentionPolicy();
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, diagnosticPolicy: policy);
        sink.Initialize();
        var observed = new Observed();
        using var provider = new EvidenceLoggerProvider([sink, observed], observed);
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        var audit = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
        await using var admission = new DiagnosticRetentionAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        using var call = new CallCommunicationPolicy(new ClearCall());
        var preferences = new LocalDiagnosticRetentionPreferences(fixture.Paths);
        var service = new DiagnosticRetentionConfigurationService(preferences, policy, admission, audit);
        service.Observe();
        using var hostile = new Activity("untrusted.retention-correlation").SetIdFormat(ActivityIdFormat.W3C).Start();
        foreach (var value in new[] { "1", "365", "30", null })
        {
            await service.RefreshAsync(origin, static () => true, fixture.Token);
            var proposal = service.Propose(value, service.Get().Revision, call.Current.Revision);
            (await service.ApplyAsync(proposal, origin, initiator, call, static () => true, fixture.Token)).Should().BeTrue();
            preferences.Load().Should().Be(value is null ? null : DiagnosticRetentionDays.Parse(value));
            policy.Effective.Should().Be(value is null ? DiagnosticRetentionDays.Default : DiagnosticRetentionDays.Parse(value));
            var coldPolicy = new DiagnosticRetentionPolicy();
            var coldService = new DiagnosticRetentionConfigurationService(new LocalDiagnosticRetentionPreferences(fixture.Paths),
                coldPolicy, admission, audit);
            coldService.Observe();
            coldService.Get().Source.Should().Be(value is null ? "default" : "saved");
            coldPolicy.Effective.Should().Be(policy.Effective);
            new WindowsSqliteEvidenceSink(fixture.Paths, diagnosticPolicy: coldPolicy).Initialize();
        }
        observed.Gaps.Should().BeEmpty();
        observed.Audits.Should().HaveCount(8).And.OnlyContain(item => item.Diagnostic.Host!.Origin == origin
            && item.Audit.Initiator == initiator && item.Audit.ActionId == "configuration.sqlite-diagnostic-retention"
            && item.Audit.CorrelationId == item.Diagnostic.Host.RequestId.Value);
        var controlSession = observed.Audits[0].Diagnostic.Host!.SessionId;
        controlSession.Should().NotBe(fixture.Request.SessionId);
        observed.Audits.Should().OnlyContain(item => item.Diagnostic.Host!.SessionId == controlSession);
        (await fixture.Store.ReadMetadataAsync(fixture.Request.SessionId, fixture.Token)).Should().Be(original);
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().Contain(grant);
        (await fixture.Tasks.ReadIncompleteAsync(100, fixture.Token)).Should().BeEmpty();
        using (var lease = sink.AcquireReadLease(fixture.Token))
        using (var connection = sink.OpenReadOnly(fixture.Token))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT count(*) FROM security_audit_events WHERE due_utc-committed_utc=$ticks;";
            command.Parameters.AddWithValue("$ticks", TimeSpan.FromDays(90).Ticks);
            command.ExecuteScalar().Should().Be(8L);
        }
        var restartedPolicy = new DiagnosticRetentionPolicy();
        var restarted = new DiagnosticRetentionConfigurationService(new LocalDiagnosticRetentionPreferences(fixture.Paths),
            restartedPolicy, admission, audit);
        restarted.Observe();
        restartedPolicy.Effective.Should().Be(DiagnosticRetentionDays.Default);
        restarted.Get().Source.Should().Be("default");
        new WindowsSqliteEvidenceSink(fixture.Paths, diagnosticPolicy: restartedPolicy).Initialize();
        Activity.Current.Should().BeSameAs(hostile);
    }

    [WindowsFact]
    public void Invalid_ordinary_policy_has_explicit_independent_file_gap_without_blocking_trusted_audit_90_or_activity_disposal()
    {
        using var paths = new OwnedStorageFixture();
        var policy = new DiagnosticRetentionPolicy();
        var sink = new WindowsSqliteEvidenceSink(paths, diagnosticPolicy: policy);
        sink.Initialize();
        var observed = new Observed();
        using var provider = new EvidenceLoggerProvider([sink, observed], observed);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var audit = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
        using (var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Policy))
        {
            audit.Write(new(host.Request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                "fixture.unavailable-retention", SecurityAuditOutcome.Failed, SecurityAuditInitiator.LocalUser, "preferences.device-local"));
        }
        observed.Audits.Should().ContainSingle();
        observed.Gaps.Should().NotBeEmpty().And.OnlyContain(gap =>
            gap.ExceptionType == typeof(DiagnosticRetentionUnavailableException).FullName);
        using var lease = sink.AcquireReadLease(Token);
        using var connection = sink.OpenReadOnly(Token);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT due_utc-committed_utc FROM security_audit_events;";
        command.ExecuteScalar().Should().Be(TimeSpan.FromDays(90).Ticks);
    }

    [WindowsFact]
    public async Task Actual_committed_log_span_link_deadlines_are_future_only_immutable_and_pruned_by_original_due_with_all_authority_untouched()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        var grant = await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var authority = AuthorityHash(fixture.Paths);
        var policy = new DiagnosticRetentionPolicy();
        policy.Activate(DiagnosticRetentionDays.Default);
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: clock, diagnosticPolicy: policy);
        using var producer = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request);
        var log = Diagnostic(producer, clock.Now);
        var span = new CompletedActivityEnvelope(new(Guid.NewGuid()), TraceSnapshot.Capture(producer.Activity)!,
            producer.Request, clock.Now, clock.Now, HostOperationOutcome.Unknown,
            [new(ActivityTraceId.CreateRandom().ToHexString(), ActivitySpanId.CreateRandom().ToHexString())]);
        sink.WriteDiagnostic(log);
        sink.WriteActivity(span);
        var old = Rows(sink);
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite, "fixture.retention",
            SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser, "preferences.device-local");
        using (var activity = HostActivity.BeginAudit(fixture.Request, audit))
        {
            sink.WriteAudit(new(Diagnostic(activity, clock.Now), audit));
        }
        var auditRows = Rows(sink, auditOnly: true);
        foreach (var days in new[] { 1, 365, 30 })
        {
            policy.Activate(new(days));
            sink.WriteDiagnostic(Diagnostic(producer, clock.Now));
            sink.WriteActivity(span with { EvidenceId = new(Guid.NewGuid()), Outcome = HostOperationOutcome.Completed });
            Rows(sink).Where(row => old.Contains(row, StringComparer.Ordinal)).Should().Equal(old);
            sink.Invoking(store => store.WriteActivity(span with { EndedUtc = clock.Now.AddMinutes(1), Outcome = HostOperationOutcome.Completed }))
                .Should().Throw<IOException>();
            Rows(sink).Where(row => old.Contains(row, StringComparer.Ordinal)).Should().Equal(old);
            using var lease = sink.AcquireReadLease(Token);
            using var connection = sink.OpenReadOnly(Token);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT due_utc-committed_utc FROM application_log_events ORDER BY rowid DESC LIMIT 1;";
            command.ExecuteScalar().Should().Be(TimeSpan.FromDays(days).Ticks);
            command.CommandText = "SELECT due_utc-committed_utc FROM activity_spans ORDER BY rowid DESC LIMIT 1;";
            command.ExecuteScalar().Should().Be(TimeSpan.FromDays(days).Ticks);
        }
        policy.HoldUnavailable();
        sink.Invoking(store => store.WriteDiagnostic(Diagnostic(producer, clock.Now))).Should().Throw<InvalidOperationException>();
        using (var activity = HostActivity.BeginAudit(fixture.Request, audit))
        {
            sink.WriteAudit(new(Diagnostic(activity, clock.Now), audit));
        }
        Rows(sink, auditOnly: true).Take(1).Should().Equal(auditRows);
        clock.Now = clock.Now.AddDays(1);
        using (var cleanup = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Retention))
        {
            var receipt = await new WindowsSqliteDiagnosticRetention(sink, clock, NullLogger<WindowsSqliteDiagnosticRetention>.Instance).RunAsync(Token);
            receipt.Should().Be(new DiagnosticRetentionBatch(1, 1, 1, false));
        }
        Rows(sink).Where(row => old.Contains(row, StringComparer.Ordinal)).Should().Equal(old);
        AuthorityHash(fixture.Paths).Should().Equal(authority);
        (await fixture.Store.ReadGrantsAsync(Token)).Should().Contain(grant);
        var reopened = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: clock);
        reopened.Initialize();
        using var viewer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var query = new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(reopened), new Access(), clock,
            NullLogger<DurableEvidenceQuery>.Instance);
        var result = await query.QueryAsync(new() { Record = new(EvidenceSource.Span, span.EvidenceId) }, null, Token);
        result.Records.Should().ContainSingle().Which.DueUtc.Should().Be(clock.Now.AddDays(29));
        result = await query.QueryAsync(new() { Source = EvidenceSource.Log }, null, Token);
        result.Records.Should().HaveCount(3);
        result.Records.Select(record => (record.DueUtc!.Value - record.CommittedUtc!.Value).TotalDays).Should().BeEquivalentTo([30d, 365d, 30d]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void Semantic_schema_v2_migrates_only_valid_legacy_30_without_rewriting_any_row_or_identity(int version)
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(paths);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Startup);
        sink.WriteDiagnostic(Diagnostic(host, new Clock().Now));
        var before = Rows(sink);
        Mutate(paths, $"PRAGMA user_version={version};");
        if (version == 1)
        {
            new WindowsSqliteEvidenceSink(paths).Initialize();
            Rows(sink).Should().Equal(before);
            using var lease = sink.AcquireReadLease(Token);
            using var connection = sink.OpenReadOnly(Token);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            command.ExecuteScalar().Should().Be(2L);
        }
        else
        {
            var action = () => new WindowsSqliteEvidenceSink(paths).Initialize();
            action.Should().Throw<InvalidDataException>();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Corrupt_or_wrong_legacy_effective_deadlines_fail_closed_and_never_initialize_empty(int version)
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(paths);
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem), HostActivityLayer.Windows, HostOperation.Startup);
        sink.WriteDiagnostic(Diagnostic(host, new Clock().Now));
        Mutate(paths, $"PRAGMA user_version={version}; UPDATE application_log_events SET due_utc=committed_utc+{TimeSpan.FromDays(version == 1 ? 1 : 366).Ticks};");
        var action = () => new WindowsSqliteEvidenceSink(paths).Initialize();
        action.Should().Throw<InvalidDataException>();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static DiagnosticEnvelope Diagnostic(HostActivity host, DateTimeOffset now) => new(1, new(Guid.NewGuid()),
        now.AddYears(-1), 1, "RetentionConfigurationFixture", "Information", "Kora.Tests", "Owned fixture.",
        new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), [], TraceSnapshot.Capture(host.Activity), host.Request,
        null, host.CorrelationId, host.ApprovalId);
    private static string[] Rows(WindowsSqliteEvidenceSink sink, bool auditOnly = false)
    {
        using var lease = sink.AcquireReadLease(Token);
        using var connection = sink.OpenReadOnly(Token);
        using var command = connection.CreateCommand();
        var rows = new List<string>();
        foreach (var table in auditOnly ? new[] { "security_audit_events" } : new[] { "application_log_events", "activity_spans", "activity_links" })
        {
            command.CommandText = $"SELECT rowid,evidence_id,committed_utc,due_utc,envelope FROM {table} ORDER BY rowid;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) { rows.Add(table + ":" + string.Join('|', Enumerable.Range(0, 5).Select(index => reader.GetValue(index)))); }
        }
        return rows.ToArray();
    }
    private static byte[] AuthorityHash(OwnedStorageFixture paths) => SHA256.HashData(
        AuthorityPartitions.SelectMany(partition =>
            Directory.GetFiles(Path.Combine(paths.LocalRoot, partition)).Order(StringComparer.Ordinal).SelectMany(File.ReadAllBytes)).ToArray());
    private static void Mutate(OwnedStorageFixture paths, string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"),
            Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA locking_mode=EXCLUSIVE; PRAGMA journal_mode=PERSIST; " + sql;
        command.ExecuteNonQuery();
    }
    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect => true; }
    private sealed class ClearCall : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
    private sealed class Observed : IEvidenceSink, IEvidenceGapReporter
    {
        public string Name => "retention-fixture";
        internal List<AuditEnvelope> Audits { get; } = [];
        internal List<EvidenceGap> Gaps { get; } = [];
        public void WriteDiagnostic(DiagnosticEnvelope envelope) { }
        public void WriteAudit(AuditEnvelope envelope) => Audits.Add(envelope);
        public void WriteActivity(CompletedActivityEnvelope envelope) { }
        public void Report(EvidenceGap gap) => Gaps.Add(gap);
    }
}
