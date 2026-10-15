using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

public sealed class WindowsSqliteAuditCompletenessTests
{
    [WindowsFact]
    public async Task All_typed_pairs_survive_bounded_pruning_pagination_policy_shortening_and_restart()
    {
        using var paths = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var committed = clock.Now;
        var policy = new AuditRetentionPolicy();
        policy.Activate(AuditRetentionDays.Default);
        var sink = new WindowsSqliteEvidenceSink(paths, timeProvider: clock, auditPolicy: policy);
        var expected = new Dictionary<HostId<EvidenceIdentity>, AuditEnvelope>();
        for (var index = 0; index < 33; index++)
        {
            WritePair(sink, clock, expected);
        }
        policy.Activate(new(30));
        WritePair(sink, clock, expected);
        using (var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Windows, HostOperation.Startup))
        {
            for (var index = 0; index <= WindowsSqliteDiagnosticRetention.MaximumLogs; index++)
            {
                sink.WriteDiagnostic(Diagnostic(host, clock));
            }
            for (var index = 0; index <= WindowsSqliteDiagnosticRetention.MaximumSpans; index++)
            {
                sink.WriteActivity(new(new(Guid.NewGuid()), TraceSnapshot.Capture(host.Activity)!, host.Request,
                    clock.Now, clock.Now, HostOperationOutcome.Completed,
                    [new(ActivityTraceId.CreateRandom().ToHexString(), ActivitySpanId.CreateRandom().ToHexString())]));
            }
        }
        clock.Now = committed.AddDays(31);
        (await Prune(sink, clock)).Should().Be(new DiagnosticRetentionBatch(128, 32, 32, true));
        var restarted = new WindowsSqliteEvidenceSink(paths, timeProvider: clock, auditPolicy: policy);
        restarted.Initialize();
        (await Prune(restarted, clock)).Should().Be(new DiagnosticRetentionBatch(1, 1, 1, false));

        var records = await ReadAuditPages(restarted, clock);

        records.Should().HaveCount(68);
        records.Select(record => record.Reference.Id).Should().OnlyHaveUniqueItems()
            .And.BeEquivalentTo(expected.Keys);
        records.Select(record => record.Host!.SessionId).Distinct().Should().HaveCount(34);
        foreach (var record in records)
        {
            var original = expected[record.Reference.Id];
            record.Audit.Should().BeEquivalentTo(original.Audit);
            record.Host.Should().Be(original.Diagnostic.Host);
            record.Trace.Should().Be(original.Diagnostic.Trace);
            record.CommittedUtc.Should().Be(committed);
            record.CorrelationId.Should().Be(original.Audit.CorrelationId);
            record.ApprovalId.Should().Be(original.Audit.ApprovalId);
            record.DueUtc.Should().Be(committed.AddDays(
                original.Diagnostic.Host == expected.Values.Last().Diagnostic.Host ? 30 : 90));
        }
        records.Count(record => record.Retention == EvidenceSegmentStatus.Present).Should().Be(66);
        records.Count(record => record.Retention == EvidenceSegmentStatus.ExpiredButPresent).Should().Be(2);
        records.Select(record => record.AuditSequence).Should().BeEquivalentTo(
            Enumerable.Range(1, 68).Select(sequence => (long?)sequence));

        policy.HoldUnavailable();
        var unavailableAudit = Audit();
        using (var host = HostActivity.BeginAudit(HostRequest.Create(RequestOrigin.LocalUi), unavailableAudit))
        {
            var write = () => restarted.WriteAudit(new(Diagnostic(host, clock), unavailableAudit));
            write.Should().Throw<AuditRetentionUnavailableException>();
        }
        (await ReadAuditPages(restarted, clock)).Should().BeEquivalentTo(records, options => options.WithStrictOrdering());
        clock.Now = committed.AddDays(400);
        (await Prune(restarted, clock)).Should().Be(new DiagnosticRetentionBatch(0, 0, 0, false));
        var expired = await ReadAuditPages(restarted, clock);
        expired.Should().HaveCount(68).And.OnlyContain(record => record.Retention == EvidenceSegmentStatus.ExpiredButPresent);
        expired.Select(record => record.DueUtc).Should().Equal(records.Select(record => record.DueUtc));
    }

    [WindowsFact]
    public async Task Exact_session_disposition_and_diagnostic_expiry_preserve_every_prior_authority_and_typed_record()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await WindowsSqliteSessionWorkspaceTests.FinishAsync(fixture, HostTaskState.Succeeded);
        var session = fixture.Request.SessionId;
        var service = WindowsSqliteSessionWorkspaceTests.Service(fixture, new());
        await service.RenameAsync(session, new(1), 0, new("Private name never in audit"),
            RequestOrigin.LocalUi, fixture.Token);
        var authorityBefore = AuthorityRows(fixture);
        var clock = new Clock { Now = fixture.Time.Now };
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: clock);
        var expected = new Dictionary<HostId<EvidenceIdentity>, AuditEnvelope>();
        WritePair(sink, clock, expected, fixture.Request);
        using (var host = HostActivity.BeginRoot(fixture.Request, HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteDiagnostic(Diagnostic(host, clock));
        }
        using (var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Evidence))
        {
            var preview = await service.PreviewDispositionAsync(session, new(1), 1, fixture.Token);
            await service.ConfirmDispositionAsync(preview, RequestOrigin.LocalUi, () => true, fixture.Token);
        }
        fixture.Count("session_metadata").Should().Be(0);
        fixture.Reopen();
        await fixture.Store.InitializeAsync(fixture.Token);
        (await fixture.Store.ReadSessionAsync(session, fixture.Token))!.IsActive.Should().BeFalse();
        var authorityAfter = AuthorityRows(fixture);
        authorityAfter.Should().HaveCount(authorityBefore.Length + 1);
        authorityAfter.Take(authorityBefore.Length).Should().Equal(authorityBefore);
        authorityAfter.Should().OnlyContain(row => !row.Contains("Private name never in audit", StringComparison.Ordinal));

        clock.Now = clock.Now.AddDays(31);
        var restarted = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: clock);
        restarted.Initialize();
        (await Prune(restarted, clock)).Should().Be(new DiagnosticRetentionBatch(1, 0, 0, false));
        var records = await ReadAuditPages(restarted, clock);
        records.Should().HaveCount(2).And.OnlyContain(record => record.Host!.SessionId == session
            && record.Retention == EvidenceSegmentStatus.Present);
        foreach (var record in records)
        {
            record.Audit.Should().BeEquivalentTo(expected[record.Reference.Id].Audit);
        }
        AuthorityRows(fixture).Should().Equal(authorityAfter);
    }

    private static void WritePair(WindowsSqliteEvidenceSink sink, Clock clock,
        Dictionary<HostId<EvidenceIdentity>, AuditEnvelope> expected, HostRequest? request = null)
    {
        var audit = Audit();
        using var host = HostActivity.BeginAudit(request ?? HostRequest.Create(RequestOrigin.LocalUi), audit);
        foreach (var outcome in new[] { SecurityAuditOutcome.Requested, SecurityAuditOutcome.Succeeded })
        {
            var envelope = new AuditEnvelope(Diagnostic(host, clock), audit.WithOutcome(outcome));
            sink.WriteAudit(envelope);
            expected.Add(envelope.Diagnostic.EvidenceId, envelope);
        }
    }

    private static async Task<List<EvidenceRecord>> ReadAuditPages(WindowsSqliteEvidenceSink sink, Clock clock)
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Evidence);
        var service = new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), new Access(), clock,
            NullLogger<DurableEvidenceQuery>.Instance);
        var records = new List<EvidenceRecord>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await service.QueryAsync(new() { Source = EvidenceSource.Audit, Limit = 7 }, cursor,
                TestContext.Current.CancellationToken);
            page.Status.Should().Be(EvidencePageStatus.Available);
            page.Records.Should().HaveCountLessThanOrEqualTo(7);
            DurableEvidenceQuery.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
            records.AddRange(page.Records);
            cursor = page.Cursor;
            (++pages).Should().BeLessThanOrEqualTo(10);
        } while (cursor is not null);
        return records;
    }

    private static async Task<DiagnosticRetentionBatch> Prune(WindowsSqliteEvidenceSink sink, Clock clock)
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Windows, HostOperation.Startup);
        return await new WindowsSqliteDiagnosticRetention(sink, clock,
            NullLogger<WindowsSqliteDiagnosticRetention>.Instance).RunAsync(TestContext.Current.CancellationToken);
    }

    private static string[] AuthorityRows(InteractionStorageFixture fixture)
    {
        using var connection = fixture.OpenRaw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sequence,correlation_id,previous_hash,hash,envelope FROM security_audit_events ORDER BY sequence;";
        using var rows = command.ExecuteReader();
        var result = new List<string>();
        while (rows.Read())
        {
            result.Add(string.Join('|', rows.GetInt64(0), rows.GetString(1), rows.GetString(2), rows.GetString(3), rows.GetString(4)));
        }
        return result.ToArray();
    }

    private static SecurityAuditEvent Audit() => new(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
        "fixture.audit", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "application.current", Guid.NewGuid());

    private static DiagnosticEnvelope Diagnostic(HostActivity host, Clock clock) => new(1, new(Guid.NewGuid()), clock.Now,
        1, "AuditFixture", "Information", "Kora.Tests", "Content-free audit fixture.",
        new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), [], TraceSnapshot.Capture(host.Activity),
        host.Request, null, host.CorrelationId, host.ApprovalId);

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

    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect => true; }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
