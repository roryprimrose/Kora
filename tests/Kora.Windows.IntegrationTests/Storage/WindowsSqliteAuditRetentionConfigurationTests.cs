using System.Diagnostics;
using System.Globalization;
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
using Kora.Core.Storage;
using Kora.Windows.Storage;
using Kora.Windows.IntegrationTests.Audio;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection(nameof(DurableStorageCompositionTestGroup))]
public sealed class WindowsSqliteAuditRetentionConfigurationTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.LocalUser)]
    [InlineData(RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand)]
    [InlineData(RequestOrigin.ActivatedVoice, SecurityAuditInitiator.VoiceCommand)]
    public async Task Real_authority_and_independent_projection_future_deadlines_preserve_all_prior_bytes_and_grants(
        RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        foreach (var scope in new[] { "once", "session", "perpetual" })
        {
            await fixture.GrantAsync(scope);
            await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
            fixture.Request = InteractionStorageFixture.NewRequest(fixture.Request.SessionId);
            await fixture.AdmitAsync(newSession: false);
        }
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var grants = await fixture.Store.ReadGrantsAsync(fixture.Token);
        var originalRows = AuthorityRows(fixture);
        var policy = new AuditRetentionPolicy();
        policy.Activate(AuditRetentionDays.Default);
        fixture.Reopen(auditPolicy: policy);
        await fixture.Store.InitializeAsync(fixture.Token);
        var ordinary = new DiagnosticRetentionPolicy();
        ordinary.Activate(new(14));
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: fixture.Time,
            diagnosticPolicy: ordinary, auditPolicy: policy);
        sink.Initialize();
        var observed = new Observed();
        using var provider = new EvidenceLoggerProvider([sink, observed], observed);
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        var audit = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
        await using var admission = new AuditRetentionAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        using var call = new CallCommunicationPolicy(new ClearCall());
        var preferences = new LocalAuditRetentionPreferences(fixture.Paths);
        var service = new AuditRetentionConfigurationService(preferences, policy, admission, audit);
        service.Observe();
        using var hostile = new Activity("untrusted.audit-retention").SetIdFormat(ActivityIdFormat.W3C).Start();
        foreach (var value in new[] { "30", "365", "90", null })
        {
            var priorDays = policy.Effective!.Value.Days;
            var priorAuthority = AuditRows(fixture);
            var priorProjection = ProjectionRows(sink);
            var priorOrdinary = OrdinaryRows(sink);
            await service.RefreshAsync(origin, static () => true, fixture.Token);
            (await service.ApplyAsync(service.Propose(value, service.Get().Revision, call.Current.Revision),
                origin, initiator, call, static () => true, fixture.Token)).Should().BeTrue();
            var expected = value is null ? AuditRetentionDays.Default : AuditRetentionDays.Parse(value);
            preferences.Load().Should().Be(value is null ? null : expected);
            policy.Effective.Should().Be(expected);
            AuditRows(fixture).Take(priorAuthority.Length).Should().Equal(priorAuthority);
            ProjectionRows(sink).Take(priorProjection.Length).Should().Equal(priorProjection);
            using (var lease = sink.AcquireReadLease(fixture.Token))
            using (var connection = sink.OpenReadOnly(fixture.Token))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT due_utc-committed_utc FROM security_audit_events ORDER BY audit_sequence DESC LIMIT 2;";
                using var rows = command.ExecuteReader();
                for (var count = 0; count < 2; count++)
                {
                    rows.Read().Should().BeTrue();
                    rows.GetInt64(0).Should().Be(TimeSpan.FromDays(priorDays).Ticks);
                }
            }
            // A genuinely new host authority mutation (not the projected configuration message) uses the confirmed policy.
            var request = HostRequest.Create(origin);
            using (var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Policy))
            {
                await fixture.Store.RecordControlIntentAsync(request, fixture.Token);
                await fixture.Store.CreateAuditRetentionSessionAsync(request, () => true, fixture.Token);
                audit.Write(new(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite, "fixture.after-activation",
                    SecurityAuditOutcome.Succeeded, initiator, "preferences.device-local"));
                sink.WriteDiagnostic(new(1, new(Guid.NewGuid()), fixture.Time.Now, 1, "AuditRetentionOrdinaryFixture",
                    "Information", "Kora.Tests", "Independent ordinary metadata.",
                    new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), [], TraceSnapshot.Capture(host.Activity),
                    host.Request, null, host.CorrelationId, host.ApprovalId));
                sink.WriteActivity(new(new(Guid.NewGuid()), TraceSnapshot.Capture(host.Activity)!, request,
                    fixture.Time.Now, fixture.Time.Now, HostOperationOutcome.Completed,
                    [new(new string('a', 32), new string('b', 16))], host.CorrelationId, host.ApprovalId));
                await new Kora.Application.Hosting.HostTaskCoordinator(fixture.Tasks).RecordOutcomeAsync(
                    new(request, new(1), HostTaskState.IntentRecorded), HostTaskState.Succeeded, fixture.Token);
            }
            var batch = await AuthorityBatch(fixture);
            batch.Candidates[^1].Record.DueUtc.Should().Be(fixture.Time.Now.AddDays(expected.Days));
            batch.Candidates[^1].Record.AuthorityProvenance!.Changes.Should().ContainSingle();
            originalRows.Should().BeSubsetOf(AuthorityRows(fixture));
            (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().Equal(grants);
            ordinary.Effective.Should().Be(new DiagnosticRetentionDays(14));
            priorOrdinary.Should().BeSubsetOf(OrdinaryRows(sink));
            using (var lease = sink.AcquireReadLease(fixture.Token))
            using (var connection = sink.OpenReadOnly(fixture.Token))
            using (var command = connection.CreateCommand())
            {
                command.Parameters.AddWithValue("$ticks", TimeSpan.FromDays(14).Ticks);
                foreach (var table in new[] { "application_log_events", "activity_spans", "activity_links" })
                {
                    command.CommandText = $"SELECT count(*) FROM {table} WHERE due_utc-committed_utc<>$ticks;";
                    command.ExecuteScalar().Should().Be(0L);
                }
            }
            var coldPolicy = new AuditRetentionPolicy();
            var cold = new AuditRetentionConfigurationService(new LocalAuditRetentionPreferences(fixture.Paths),
                coldPolicy, admission, audit);
            cold.Observe();
            coldPolicy.Effective.Should().Be(expected);
            fixture.Reopen(auditPolicy: coldPolicy);
            await fixture.Store.InitializeAsync(fixture.Token);
            new WindowsSqliteEvidenceSink(fixture.Paths, auditPolicy: coldPolicy).Initialize();
            fixture.Reopen(auditPolicy: policy);
            await fixture.Store.InitializeAsync(fixture.Token);
        }
        observed.Audits.Where(item => string.Equals(item.Audit.ActionId, "configuration.audit-retention", StringComparison.Ordinal))
            .Should().HaveCount(8).And.OnlyContain(item => item.Diagnostic.Host!.Origin == origin && item.Audit.Initiator == initiator
                && item.Audit.CorrelationId == item.Diagnostic.Host.RequestId.Value);
        observed.Gaps.Should().BeEmpty();
        Activity.Current.Should().BeSameAs(hostile);
        var reader = new DurableEvidenceQuery(new WindowsEvidenceReader(new(sink), new(fixture.Paths), fixture.Store),
            new Access(), fixture.Time, NullLogger<DurableEvidenceQuery>.Instance);
        using (var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            var authority = await reader.QueryAsync(new() { Source = EvidenceSource.AuthorityAudit }, null, fixture.Token);
            var projections = await reader.QueryAsync(new() { Source = EvidenceSource.Audit }, null, fixture.Token);
            authority.Records.Should().NotBeEmpty().And.OnlyContain(record => record.AuthorityProvenance != null);
            projections.Records.Should().HaveCount(12).And.OnlyContain(record => record.AuthorityProvenance == null);
            var citations = authority.Records.Select(record => record.Reference.Citation).ToArray();
            fixture.Time.Now = fixture.Time.Now.AddDays(366);
            var expired = await reader.QueryAsync(new() { Source = EvidenceSource.AuthorityAudit }, null, fixture.Token);
            expired.Records.Select(record => record.Reference.Citation).Should().Equal(citations);
            expired.Records.Should().OnlyContain(record => record.Retention == EvidenceSegmentStatus.ExpiredButPresent);
            (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().Equal(grants);
            originalRows.Should().BeSubsetOf(AuthorityRows(fixture));
        }
        EvidenceRetentionPolicy.DailyFileDays.Should().Be(30);
        EvidenceRetentionPolicy.DailyFileCount.Should().Be(30);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Legacy_migration_with_valid_variable_audit_metadata_preserves_exact_rows_citations_and_deadlines(int legacyVersion)
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await fixture.GrantAsync("perpetual");
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var policy = new AuditRetentionPolicy();
        policy.Activate(new(30));
        fixture.Reopen(auditPolicy: policy);
        await fixture.Store.InitializeAsync(fixture.Token);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using (var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Policy))
        {
            await fixture.Store.RecordControlIntentAsync(request, fixture.Token);
            await fixture.Store.CreateAuditRetentionSessionAsync(request, () => true, fixture.Token);
            await new Kora.Application.Hosting.HostTaskCoordinator(fixture.Tasks).RecordOutcomeAsync(
                new(request, new(1), HostTaskState.IntentRecorded), HostTaskState.Succeeded, fixture.Token);
        }
        var original = AuditRows(fixture);
        var committed = (await AuthorityBatch(fixture)).Candidates.Select(item => item.Record).ToArray();
        fixture.StageLegacy(legacyVersion);
        policy.Activate(new(365));
        fixture.Reopen(auditPolicy: policy);
        await fixture.Store.InitializeAsync(fixture.Token);
        AuditRows(fixture).Should().Equal(original);
        var migrated = (await AuthorityBatch(fixture)).Candidates.Select(item => item.Record).ToArray();
        migrated.Select(item => (item.Reference.Citation, item.DueUtc, item.AuthorityProvenance!.CommitDigest))
            .Should().Equal(committed.Select(item => (item.Reference.Citation, item.DueUtc, item.AuthorityProvenance!.CommitDigest)));
        (await fixture.Store.ReadGrantsAsync(fixture.Token)).Should().ContainSingle();

        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, new EvidenceRetentionPolicy(30), fixture.Time);
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite, "fixture.legacy-audit",
            SecurityAuditOutcome.Requested, SecurityAuditInitiator.TypedCommand, "preferences.device-local");
        using (var host = HostActivity.BeginAudit(HostRequest.Create(RequestOrigin.LocalUi), audit))
        {
            var diagnostic = new DiagnosticEnvelope(1, new(Guid.NewGuid()), fixture.Time.Now, 150, null,
                "Information", "Kora.Tests", "Legacy audit fixture.", new Dictionary<string, EvidenceValue>(StringComparer.Ordinal),
                [], TraceSnapshot.Capture(host.Activity), host.Request, null, host.CorrelationId, host.ApprovalId);
            sink.WriteAudit(new(diagnostic, audit));
        }
        var projection = ProjectionRows(sink);
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(fixture.Paths.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"),
            Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA locking_mode=EXCLUSIVE; PRAGMA journal_mode=PERSIST; PRAGMA synchronous=FULL; PRAGMA user_version=1;";
            command.ExecuteNonQuery();
        }
        new WindowsSqliteEvidenceSink(fixture.Paths, auditPolicy: policy).Initialize();
        ProjectionRows(sink).Should().Equal(projection);
    }

    [WindowsFact]
    public async Task Real_required_terminal_projection_failure_keeps_durable_marker_and_refuses_new_and_restarted_authority()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var policy = new AuditRetentionPolicy();
        policy.Activate(new(365));
        fixture.Reopen(auditPolicy: policy);
        await fixture.Store.InitializeAsync(fixture.Token);
        var preferences = new LocalAuditRetentionPreferences(fixture.Paths);
        preferences.Save(new(365));
        var checkpoint = new FailedTerminal();
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, null, fixture.Time, checkpoint, auditPolicy: policy);
        sink.Initialize();
        var observed = new Observed();
        using var provider = new EvidenceLoggerProvider([sink, observed], observed);
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var audit = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
        await using var admission = new AuditRetentionAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        using var call = new CallCommunicationPolicy(new ClearCall());
        var service = new AuditRetentionConfigurationService(preferences, policy, admission, audit);
        service.Observe();
        await service.RefreshAsync(RequestOrigin.LocalUi, () => true, fixture.Token);
        checkpoint.Fail = true;
        var apply = () => service.ApplyAsync(service.Propose("30", service.Get().Revision, call.Current.Revision),
            RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, call, () => true, fixture.Token);
        await apply.Should().ThrowAsync<IOException>();
        policy.Effective.Should().BeNull();
        preferences.ReadBack().Should().Be(new AuditRetentionDays(30));
        new LocalAuditRetentionPreferences(fixture.Paths).Invoking(item => item.Load()).Should().Throw<InvalidDataException>();
        var cold = new AuditRetentionPolicy();
        new AuditRetentionConfigurationService(new LocalAuditRetentionPreferences(fixture.Paths), cold, admission, audit)
            .Invoking(item => item.Observe()).Should().Throw<InvalidDataException>();
        cold.Effective.Should().BeNull();
        var before = AuditRows(fixture);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using (var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Policy))
        {
            await fixture.Store.RecordControlIntentAsync(request, fixture.Token);
            var create = () => fixture.Store.CreateAuditRetentionSessionAsync(request, () => true, fixture.Token).AsTask();
            await create.Should().ThrowAsync<AuditRetentionUnavailableException>();
        }
        AuditRows(fixture).Should().Equal(before);
        ProjectionRows(sink).Should().ContainSingle();
        var query = await AuthorityBatch(fixture);
        query.Candidates.Should().NotBeEmpty();
        query.Candidates.Should().OnlyContain(item => item.Record.AuthorityProvenance != null);
    }

    [WindowsFact]
    public async Task Concurrent_real_ordinary_and_audit_changes_keep_each_policy_and_host_trace_isolated_without_reentrant_lease()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var auditPolicy = new AuditRetentionPolicy();
        auditPolicy.Activate(AuditRetentionDays.Default);
        var ordinary = new DiagnosticRetentionPolicy();
        fixture.Reopen(auditPolicy: auditPolicy);
        await fixture.Store.InitializeAsync(fixture.Token);
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: fixture.Time,
            diagnosticPolicy: ordinary, auditPolicy: auditPolicy);
        sink.Initialize();
        var observed = new Observed();
        using var provider = new EvidenceLoggerProvider([sink, observed], observed);
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(provider));
        var audit = new LoggerSecurityAuditLog(factory.CreateLogger<LoggerSecurityAuditLog>());
        await using var auditAdmission = new AuditRetentionAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        await using var diagnosticAdmission = new DiagnosticRetentionAdmission(fixture.Store, fixture.Store, new(fixture.Tasks));
        using var call = new CallCommunicationPolicy(new ClearCall());
        var auditService = new AuditRetentionConfigurationService(new LocalAuditRetentionPreferences(fixture.Paths), auditPolicy, auditAdmission, audit);
        var diagnosticService = new DiagnosticRetentionConfigurationService(new LocalDiagnosticRetentionPreferences(fixture.Paths),
            ordinary, diagnosticAdmission, audit);
        auditService.Observe();
        diagnosticService.Observe();
        await Task.WhenAll(auditService.RefreshAsync(RequestOrigin.LocalUi, () => true, fixture.Token),
            diagnosticService.RefreshAsync(RequestOrigin.LocalUi, () => true, fixture.Token));
        var results = await Task.WhenAll(auditService.ApplyAsync(auditService.Propose("365", auditService.Get().Revision, call.Current.Revision),
            RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, call, () => true, fixture.Token),
            diagnosticService.ApplyAsync(diagnosticService.Propose("7", diagnosticService.Get().Revision, call.Current.Revision),
                RequestOrigin.LocalUi, SecurityAuditInitiator.TypedCommand, call, () => true, fixture.Token));
        results.Should().OnlyContain(result => result);
        auditPolicy.Effective.Should().Be(new AuditRetentionDays(365));
        ordinary.Effective.Should().Be(new DiagnosticRetentionDays(7));
        observed.Audits.GroupBy(item => item.Audit.ActionId, StringComparer.Ordinal)
            .Select(group => group.Select(item => item.Diagnostic.Host!.SessionId).Distinct().Single()).Distinct().Should().HaveCount(2);
        observed.Audits.Should().OnlyContain(item => item.Diagnostic.Host!.RequestId.Value == item.Audit.CorrelationId
            && item.Diagnostic.Trace != null);
        observed.Gaps.Should().BeEmpty();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using (var host = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteActivity(new(new(Guid.NewGuid()), TraceSnapshot.Capture(host.Activity)!, request,
                fixture.Time.Now, fixture.Time.Now, HostOperationOutcome.Completed, [], host.CorrelationId, host.ApprovalId));
        }
        using var lease = sink.AcquireReadLease(fixture.Token);
        using var connection = sink.OpenReadOnly(fixture.Token);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM activity_spans WHERE due_utc-committed_utc=$ticks;";
        command.Parameters.AddWithValue("$ticks", TimeSpan.FromDays(7).Ticks);
        ((long)command.ExecuteScalar()!).Should().BeGreaterThan(0);
    }

    private static async Task<EvidenceReadBatch> AuthorityBatch(InteractionStorageFixture fixture)
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        return await ((ICommittedAuthorityAuditReader)fixture.Store).ReadAsync(new() { Source = EvidenceSource.AuthorityAudit },
            null, host.Request, fixture.Time.Now, fixture.Token);
    }
    private static string[] AuditRows(InteractionStorageFixture fixture) => RawRows(fixture, ["security_audit_events"]);
    private static string[] AuthorityRows(InteractionStorageFixture fixture) => RawRows(fixture,
        ["security_audit_events", "work_sessions", "host_observations", "host_questions", "scoped_grants",
            "perpetual_grants", "host_tasks", "host_task_events", "host_task_waits", "session_metadata"]);
    private static string[] RawRows(InteractionStorageFixture fixture, string[] tables)
    {
        using var connection = fixture.OpenRaw();
        return ReadRows(connection, tables);
    }
    private static string[] ProjectionRows(WindowsSqliteEvidenceSink sink)
    {
        using var lease = sink.AcquireReadLease(TestContext.Current.CancellationToken);
        using var connection = sink.OpenReadOnly(TestContext.Current.CancellationToken);
        return ReadRows(connection, ["security_audit_events"]);
    }
    private static string[] OrdinaryRows(WindowsSqliteEvidenceSink sink)
    {
        using var lease = sink.AcquireReadLease(TestContext.Current.CancellationToken);
        using var connection = sink.OpenReadOnly(TestContext.Current.CancellationToken);
        return ReadRows(connection, ["application_log_events", "activity_spans", "activity_links"]);
    }
    private static string[] ReadRows(SqliteConnection connection, string[] tables)
    {
        using var command = connection.CreateCommand();
        var result = new List<string>();
        foreach (var table in tables)
        {
            command.CommandText = $"SELECT * FROM {table} ORDER BY rowid;";
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                result.Add(table + ":" + string.Join('|', Enumerable.Range(0, rows.FieldCount)
                    .Select(index => Convert.ToString(rows.GetValue(index), CultureInfo.InvariantCulture))));
            }
        }
        return result.ToArray();
    }
    private sealed class ClearCall : ICallStateService
    {
        public CallState CurrentState => CallState.Clear;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
    }
    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect => true; }
    private sealed class Observed : IEvidenceSink, IEvidenceGapReporter
    {
        public string Name => "audit-retention-fixture";
        internal List<AuditEnvelope> Audits { get; } = [];
        internal List<EvidenceGap> Gaps { get; } = [];
        public void WriteDiagnostic(DiagnosticEnvelope envelope) { }
        public void WriteAudit(AuditEnvelope envelope) => Audits.Add(envelope);
        public void WriteActivity(CompletedActivityEnvelope envelope) { }
        public void Report(EvidenceGap gap) => Gaps.Add(gap);
    }
    private sealed class FailedTerminal : ISqliteTransactionCheckpoint
    {
        internal bool Fail { get; set; }
        public void BeforeWrite(SqliteConnection connection, SqliteTransaction transaction) { }
        public void BeforeCommit(SqliteConnection connection, SqliteTransaction transaction)
        {
            if (!Fail) { return; }
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT envelope FROM security_audit_events ORDER BY audit_sequence DESC LIMIT 1;";
            if (command.ExecuteScalar() is string json
                && WindowsSqliteEvidenceSink.DecodeAudit(json).Audit.Outcome == SecurityAuditOutcome.Succeeded)
            {
                throw new IOException("Injected required terminal audit commit failure.");
            }
        }
    }
}
