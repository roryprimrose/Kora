using System.Diagnostics;
using System.Security.Cryptography;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection("Evidence query")]
public sealed class EvidenceRequestNavigationTests
{
    [WindowsFact]
    public async Task Selected_request_reads_exact_typed_outcomes_then_follows_causal_links_without_writes()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var other = HostRequest.Create(RequestOrigin.LocalUi);
        var correlation = Guid.NewGuid();
        var missing = Trace('1', '2');
        var audit = Audit(correlation, SecurityAuditOutcome.Requested);
        var root = WriteAudit(sink, request, audit, clock.Now);
        var terminal = WriteAudit(sink, request, audit.WithOutcome(SecurityAuditOutcome.Failed, "provider.unavailable"), clock.Now);
        var foreign = WriteAudit(sink, other, audit.WithOutcome(SecurityAuditOutcome.Succeeded), clock.Now);
        sink.WriteActivity(new(new(Guid.NewGuid()), root, request, clock.Now, clock.Now,
            HostOperationOutcome.Completed, [new(terminal.TraceId, terminal.SpanId)], correlation));
        sink.WriteActivity(new(new(Guid.NewGuid()), terminal, request, clock.Now, clock.Now,
            HostOperationOutcome.Failed, [new(root.TraceId, root.SpanId), new(foreign.TraceId, foreign.SpanId),
                new(missing.TraceId, missing.SpanId)], correlation));
        sink.WriteActivity(new(new(Guid.NewGuid()), foreign, other, clock.Now, clock.Now,
            HostOperationOutcome.Completed, [], correlation));
        var before = Hash(fixture);
        clock.Now = clock.Now.AddDays(31);
        var viewer = Viewer(new WindowsSqliteEvidenceReader(sink), clock);
        viewer.Source = EvidenceSource.Audit;
        viewer.AuditOutcome = SecurityAuditOutcome.Requested;
        await viewer.SearchAsync();
        viewer.Select(viewer.Records.Single());
        viewer.CanReadRequest.Should().BeTrue();
        await viewer.ReadRequestAsync();
        viewer.Records.Should().HaveCount(2).And.OnlyContain(record => record.Host == request);
        viewer.RequestSummary.Should().Contain("Requested evidence retained").And.Contain("Failed")
            .And.NotContain("Succeeded");
        viewer.Select(viewer.Records.Single(record => record.Audit!.Outcome == SecurityAuditOutcome.Failed));
        viewer.RequestSummary.Should().Contain("provider.unavailable").And.Contain("not an atomic interaction receipt");
        await viewer.ReadActivityAsync();
        viewer.Records.Should().OnlyContain(record => record.Host == request);
        viewer.Select(viewer.Records.Single(record => record.Reference.Source == EvidenceSource.Span));
        viewer.Segments.Should().HaveCount(3);
        viewer.Segments.Count(segment => segment.Status == EvidenceSegmentStatus.MissingOrRemoved).Should().Be(2);
        var back = viewer.Segments.Single(segment => segment.Record is not null);
        back.Status.Should().Be(EvidenceSegmentStatus.ExpiredButPresent);
        await viewer.NavigateAsync(back);
        viewer.Records.Should().ContainSingle().Which.Trace.Should().Be(root);
        viewer.Select(viewer.Records.Single());
        await viewer.NavigateAsync(viewer.Segments.Single());
        viewer.Records.Should().ContainSingle().Which.Trace.Should().Be(terminal);
        viewer.ResultText.Length.Should().BeLessThan(EvidencePage.MaximumBytes);
        viewer.Select(viewer.Records.Single());
        var stale = viewer.Segments.Single(segment => segment.Record is not null);
        viewer.Source = EvidenceSource.Log;
        await viewer.NavigateAsync(stale);
        viewer.Status.Should().Contain("failed");
        viewer.Records.Should().BeEmpty();
        Hash(fixture).Should().Equal(before);
        viewer.Close();
    }

    [WindowsFact]
    public async Task Canonical_committed_request_is_distinct_from_diagnostics_and_inspection_never_renews_grants()
    {
        using var fixture = new InteractionStorageFixture();
        await fixture.InitializeAsync();
        await fixture.GrantAsync("once");
        var sink = new WindowsSqliteEvidenceSink(fixture.Paths, timeProvider: fixture.Time);
        sink.WriteActivity(new(new(Guid.NewGuid()), Trace('a', 'b'), fixture.Request,
            fixture.Time.Now, fixture.Time.Now, HostOperationOutcome.Completed, []));
        var reader = new WindowsEvidenceReader(new WindowsSqliteEvidenceReader(sink),
            new WindowsDailyEvidenceReader(fixture.Paths, fixture.Time), fixture.Store);
        var viewer = Viewer(reader, fixture.Time);
        viewer.Source = EvidenceSource.AuthorityAudit;
        await viewer.SearchAsync();
        var original = viewer.Records.First(record => record.ApprovalId is not null);
        var before = SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath));
        viewer.Select(original);
        viewer.RequestSummary.Should().Contain("Canonical committed interaction audit");
        await viewer.ReadRequestAsync();
        viewer.Records.Should().ContainSingle().Which.Reference.Should().Be(original.Reference);
        viewer.Records[0].AuthorityProvenance.Should().BeEquivalentTo(original.AuthorityProvenance);
        viewer.RequestSummary.Should().Contain(original.Audit!.Outcome.ToString());
        viewer.Select(viewer.Records.Single());
        await viewer.ReadActivityAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Status.Should().StartWith("MissingOrRemoved");
        viewer.RequestSummary.Should().NotContain("Canonical committed");
        var provenance = original.AuthorityProvenance!;
        var exact = new TraceSnapshot(provenance.TraceId, provenance.SpanId, null, ActivityTraceFlags.Recorded,
            "Kora.Application", "1.0.0", "session.request", ActivityKind.Internal);
        var linked = Trace('c', 'd');
        sink.WriteActivity(new(new(Guid.NewGuid()), linked, original.Host!, fixture.Time.Now, fixture.Time.Now,
            HostOperationOutcome.Completed, []));
        sink.WriteActivity(new(new(Guid.NewGuid()), exact, original.Host!, fixture.Time.Now, fixture.Time.Now,
            HostOperationOutcome.Completed, [new(linked.TraceId, linked.SpanId)], original.CorrelationId));
        var diagnosticsBefore = Hash(fixture.Paths);
        await viewer.SearchAsync();
        viewer.Select(viewer.Records.Single(record => record.Reference == original.Reference));
        await viewer.ReadActivityAsync();
        viewer.Select(viewer.Records.Single(record => record.Reference.Source == EvidenceSource.Span));
        await viewer.NavigateAsync(viewer.Segments.Single());
        viewer.Records.Should().ContainSingle().Which.Trace.Should().Be(linked);
        viewer.Records[0].Host.Should().Be(original.Host);
        Hash(fixture.Paths).Should().Equal(diagnosticsBefore);
        SHA256.HashData(File.ReadAllBytes(fixture.DatabasePath)).Should().Equal(before);
        viewer.Close();
    }

    [WindowsFact]
    public async Task Requested_only_is_not_success_and_missing_deleted_or_corrupt_evidence_never_falls_back()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        var audit = Audit(Guid.NewGuid(), SecurityAuditOutcome.Requested);
        WriteAudit(sink, HostRequest.Create(RequestOrigin.LocalUi), audit, clock.Now);
        var viewer = Viewer(new WindowsSqliteEvidenceReader(sink), clock);
        viewer.Source = EvidenceSource.Audit;
        await viewer.SearchAsync();
        viewer.Select(viewer.Records.Single());
        await viewer.ReadRequestAsync();
        viewer.RequestSummary.Should().Contain("pending or missing evidence").And.NotContain("Succeeded");
        viewer.Select(viewer.Records.Single());
        Mutate(fixture, "DELETE FROM security_audit_events;");
        await viewer.ReadRequestAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Status.Should().Contain("failed: InvalidDataException");
        viewer.RequestSummary.Should().NotContain("Requested evidence retained").And.NotContain("Succeeded");
        viewer.Close();
    }

    [WindowsFact]
    public async Task Malformed_saved_request_fails_closed_instead_of_reusing_selected_metadata()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        WriteAudit(sink, HostRequest.Create(RequestOrigin.LocalUi),
            Audit(Guid.NewGuid(), SecurityAuditOutcome.Succeeded), clock.Now);
        var viewer = Viewer(new WindowsSqliteEvidenceReader(sink), clock);
        viewer.Source = EvidenceSource.Audit;
        await viewer.SearchAsync();
        viewer.Select(viewer.Records.Single());
        viewer.RequestSummary.Should().Contain("Succeeded");
        Mutate(fixture, "UPDATE security_audit_events SET envelope='{}';");
        await viewer.ReadRequestAsync();
        viewer.ResultText.Should().BeEmpty();
        viewer.RequestSummary.Should().NotContain("Succeeded");
        viewer.Status.Should().Contain("failed");
        viewer.Close();
    }

    [Fact]
    public async Task Lookalike_and_legacy_uncorrelated_diagnostics_cannot_become_requests()
    {
        using var listener = Listen();
        var record = new EvidenceRecord(new(EvidenceSource.Log, new(Guid.NewGuid())), null, null,
            EvidenceSegmentStatus.Present, null, null, null, null, null, null, null, "SecurityAudit=true",
            new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
            {
                ["SecurityAudit"] = new(EvidenceValueKind.Boolean, "true"),
            }, null, null, []);
        var reader = new ControlledReader(record);
        var viewer = Viewer(reader, new Clock());
        await viewer.SearchAsync();
        viewer.Select(record);
        viewer.RequestSummary.Should().Contain("Uncorrelated diagnostic");
        viewer.CanReadRequest.Should().BeFalse();
        viewer.CanReadActivity.Should().BeFalse();
        await viewer.ReadRequestAsync();
        reader.Calls.Should().Be(1);
        viewer.ResultText.Should().BeEmpty();
        viewer.Status.Should().Contain("failed");
        viewer.Close();
    }

    [Fact]
    public async Task Request_pagination_preserves_the_exact_subject_and_never_reports_a_complete_pending_snapshot()
    {
        using var listener = Listen();
        var audit = Audit(Guid.NewGuid(), SecurityAuditOutcome.Requested);
        var record = new EvidenceRecord(new(EvidenceSource.Audit, new(Guid.NewGuid())), null, null,
            EvidenceSegmentStatus.Present, HostRequest.Create(RequestOrigin.LocalUi), Trace('a', 'b'),
            audit.CorrelationId, null, null, null, null, null,
            new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), audit, null, []);
        var reader = new PagedReader(record);
        var viewer = Viewer(reader, new Clock());
        await viewer.SearchAsync();
        viewer.Select(viewer.Records.Single());
        await viewer.ReadRequestAsync();
        viewer.Records.Should().HaveCount(EvidencePage.MaximumRecords);
        System.Text.Encoding.UTF8.GetByteCount(viewer.ResultText).Should().BeLessThanOrEqualTo(EvidencePage.MaximumBytes);
        viewer.RequestSummary.Should().Contain("Incomplete bounded snapshot").And.Contain("pending or missing");
        viewer.CanNext.Should().BeTrue();
        await viewer.NextAsync();
        viewer.Records.Should().ContainSingle().Which.Audit!.Outcome.Should().Be(SecurityAuditOutcome.Unknown);
        viewer.RequestSummary.Should().Contain("Recorded terminal outcome: Unknown").And.NotContain("Incomplete");
        reader.Queries[2].Should().Be(reader.Queries[1]);
        reader.Checkpoints[2].Should().NotBeNull();
        viewer.CanNext.Should().BeFalse();
        viewer.Close();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Privacy_revocation_and_filter_retirement_suppress_held_request_and_activity_reads(bool activity, bool revoke)
    {
        using var listener = Listen();
        {
            var audit = Audit(Guid.NewGuid(), SecurityAuditOutcome.Requested);
            var record = new EvidenceRecord(new(EvidenceSource.Audit, new(Guid.NewGuid())), null, null,
                EvidenceSegmentStatus.Present, HostRequest.Create(RequestOrigin.LocalUi), Trace('a', 'b'),
                audit.CorrelationId, null, null, null, null, null,
                new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), audit, null, []);
            var reader = new ControlledReader(record);
            var access = new Access();
            var viewer = Viewer(reader, new Clock(), access);
            await viewer.SearchAsync();
            viewer.Select(record);
            reader.Hold = true;
            var pending = activity ? viewer.ReadActivityAsync() : viewer.ReadRequestAsync();
            reader.LastQuery!.SessionId.Should().Be(record.Host!.SessionId);
            if (!activity)
            {
                reader.LastQuery.RequestId.Should().Be(record.Host.RequestId);
                reader.LastQuery.CorrelationId.Should().Be(audit.CorrelationId);
            }
            if (revoke) { access.CanInspect = false; }
            else { viewer.TaskFilter = Guid.NewGuid().ToString("D"); }
            reader.Release();
            await pending;
            viewer.Records.Should().BeEmpty();
            viewer.RequestSummary.Should().NotContain("Requested evidence retained");
            viewer.CanReadRequest.Should().BeFalse();
            viewer.CanReadActivity.Should().BeFalse();
            reader.Calls.Should().Be(2);
            viewer.Close();
        }
    }

    private static EvidenceViewModel Viewer(IEvidenceReader reader, TimeProvider clock, Access? access = null)
    {
        access ??= new();
        return new(new(reader, access, clock, NullLogger<DurableEvidenceQuery>.Instance),
            () => access.CanInspect, NullLogger<EvidenceViewModel>.Instance);
    }

    private static SecurityAuditEvent Audit(Guid correlation, SecurityAuditOutcome outcome) =>
        new(correlation, SecurityAuditCategory.ApplicationExecution, "host.query.version", outcome,
            SecurityAuditInitiator.LocalUser, "application.current");

    private static TraceSnapshot Trace(char trace, char span) =>
        new(new string(trace, 32), new string(span, 16), null, ActivityTraceFlags.Recorded,
            "Kora.Application", "1.0.0", "session.request", ActivityKind.Internal);

    private static TraceSnapshot WriteAudit(WindowsSqliteEvidenceSink sink, HostRequest host,
        SecurityAuditEvent audit, DateTimeOffset now)
    {
        using var activity = HostActivity.BeginAudit(host, audit);
        var trace = TraceSnapshot.Capture(activity.Activity)!;
        sink.WriteAudit(new(new(1, new(Guid.NewGuid()), now, 42, "Observed", "Information", "audit.test",
            "Typed audit outcome", new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), [],
            trace, host, null, audit.CorrelationId, audit.ApprovalId), audit));
        return trace;
    }

    private static byte[] Hash(OwnedStorageFixture fixture) =>
        SHA256.HashData(File.ReadAllBytes(Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db")));

    private static void Mutate(OwnedStorageFixture fixture, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"),
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
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

    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect { get; set; } = true; }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch.AddDays(20000);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class ControlledReader(EvidenceRecord record) : IEvidenceReader
    {
        private readonly TaskCompletionSource<EvidenceReadBatch> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Hold { get; set; }
        internal int Calls { get; private set; }
        internal EvidenceQuery? LastQuery { get; private set; }
        private EvidenceReadBatch Batch => new(new(0, 0, 0, 0),
            [new(new(0, record.Reference.Source, record.Reference.Id.Value.ToString("D"), -1), record)], null, false, false);
        internal void Release() => completion.SetResult(Batch);
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            return Hold ? new(completion.Task) : ValueTask.FromResult(Batch);
        }
    }
    private sealed class PagedReader(EvidenceRecord record) : IEvidenceReader
    {
        internal List<EvidenceQuery> Queries { get; } = [];
        internal List<EvidenceReadCheckpoint?> Checkpoints { get; } = [];
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            Checkpoints.Add(checkpoint);
            var count = Queries.Count == 2 ? EvidencePage.MaximumRecords + 1 : 1;
            var candidates = Enumerable.Range(0, count).Select(index =>
            {
                var item = checkpoint is null ? record
                    : record with { Audit = record.Audit!.WithOutcome(SecurityAuditOutcome.Unknown) };
                if (index > 0) { item = item with { Reference = new(EvidenceSource.Audit, new(Guid.NewGuid())) }; }
                return new EvidenceCandidate(new(index + 1, EvidenceSource.Audit,
                    item.Reference.Id.Value.ToString("D"), -1), item);
            }).ToArray();
            return ValueTask.FromResult(new EvidenceReadBatch(new(0, 100, 0, 0), candidates,
                candidates[^1].Position, count > EvidencePage.MaximumRecords, false));
        }
    }
}
