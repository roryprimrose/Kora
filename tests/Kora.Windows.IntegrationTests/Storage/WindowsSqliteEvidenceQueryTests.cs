using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection("Evidence query")]
public sealed class WindowsSqliteEvidenceQueryTests
{
    [WindowsFact]
    public async Task Composed_real_store_returns_cited_sources_filters_pages_and_due_records_without_writes()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var foreign = HostRequest.Create(RequestOrigin.LocalUi);
        var ids = new List<HostId<EvidenceIdentity>>();
        using (var producer = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            for (var index = 0; index < 55; index++)
            {
                var envelope = Diagnostic(producer, index);
                ids.Add(envelope.EvidenceId);
                sink.WriteDiagnostic(envelope);
            }
        }
        using (var producer = HostActivity.BeginRoot(foreign, HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteDiagnostic(Diagnostic(producer, 900));
        }
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
            "host.query.version", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "application.current", Guid.NewGuid());
        using (var producer = HostActivity.BeginAudit(request, audit))
        {
            sink.WriteAudit(new(Diagnostic(producer, 700), audit));
            sink.WriteActivity(new(new(Guid.NewGuid()), TraceSnapshot.Capture(producer.Activity)!, request,
                clock.Now, clock.Now, HostOperationOutcome.Completed, [new(new string('a', 32), new string('b', 16))],
                audit.CorrelationId, audit.ApprovalId));
        }
        var before = Hash(fixture);
        clock.Now = clock.Now.AddDays(31);
        var service = Service(sink, clock);
        using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var query = new EvidenceQuery { Source = EvidenceSource.Log, SessionId = request.SessionId, Limit = 10 };
        var actual = new List<EvidenceRecord>();
        string? cursor = null;
        do
        {
            var page = await service.QueryAsync(query, cursor, TestContext.Current.CancellationToken);
            page.Status.Should().Be(EvidencePageStatus.Available);
            page.Records.Should().HaveCountLessThanOrEqualTo(10);
            page.Records.Should().OnlyContain(record => record.Host!.SessionId == request.SessionId);
            DurableEvidenceQuery.Serialize(page).Length.Should().BeLessThanOrEqualTo(65536);
            actual.AddRange(page.Records);
            cursor = page.Cursor;
        } while (cursor is not null);
        actual.Should().HaveCount(55);
        actual.Select(record => record.Reference.Id).Should().Equal(ids.OrderBy(id => id.Value.ToString("D"), StringComparer.Ordinal));
        actual.Should().OnlyContain(record => record.Retention == EvidenceSegmentStatus.ExpiredButPresent);
        actual[0].Properties["SecurityAudit"].CanonicalValue.Should().Be("true");
        actual[0].Audit.Should().BeNull();
        actual[0].Properties["SecretValue"].CanonicalValue.Should().Be("[redacted]");
        var filtered = await service.QueryAsync(query with
        {
            Limit = 50, EventId = 12, Category = "query.test", Severity = EvidenceSeverity.Information,
            Property = new("Count", new(EvidenceValueKind.WholeNumber, "12")), Text = "Observed",
            TaskId = request.TaskId, RequestId = request.RequestId,
            FromUtc = clock.Now.AddDays(-32), UntilUtc = clock.Now,
        }, null, TestContext.Current.CancellationToken);
        filtered.Records.Should().ContainSingle();
        filtered.Records[0].EventId.Should().Be(12);
        var exact = await service.QueryAsync(new() { Record = filtered.Records[0].Reference },
            null, TestContext.Current.CancellationToken);
        exact.Records[0].Should().BeEquivalentTo(filtered.Records[0]);
        var noCrossSession = await service.QueryAsync(new()
        {
            Record = filtered.Records[0].Reference, SessionId = foreign.SessionId,
        }, null, TestContext.Current.CancellationToken);
        noCrossSession.Status.Should().Be(EvidencePageStatus.MissingOrRemoved);
        var audits = await service.QueryAsync(new()
        {
            Source = EvidenceSource.Audit, ActionId = audit.ActionId, AuditOutcome = audit.Outcome,
            CorrelationId = audit.CorrelationId, ApprovalId = audit.ApprovalId,
        }, null, TestContext.Current.CancellationToken);
        audits.Records.Should().ContainSingle();
        audits.Records[0].Audit.Should().BeEquivalentTo(audit);
        audits.Records[0].AuditSequence.Should().Be(1);
        audits.Records[0].Retention.Should().Be(EvidenceSegmentStatus.Present);
        var all = await service.QueryAsync(new() { SessionId = request.SessionId, Limit = 50 }, null, TestContext.Current.CancellationToken);
        all.UnavailableSources.Should().Equal(EvidenceSource.Session, EvidenceSource.Conversation);
        Hash(fixture).Should().Equal(before);
    }

    [WindowsFact]
    public async Task Trace_parents_links_missing_and_expired_targets_remain_correlation_not_session_authority()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var other = HostRequest.Create(RequestOrigin.LocalUi);
        var root = Trace(new string('a', 32), new string('b', 16));
        var child = Trace(root.TraceId, new string('c', 16), root.SpanId);
        var foreign = Trace(new string('d', 32), new string('e', 16));
        var rootId = new HostId<EvidenceIdentity>(Guid.NewGuid());
        sink.WriteActivity(new(rootId, root, request, clock.Now, clock.Now, HostOperationOutcome.Completed, []));
        sink.WriteActivity(new(new(Guid.NewGuid()), foreign, other, clock.Now, clock.Now, HostOperationOutcome.Completed, []));
        clock.Now = clock.Now.AddDays(31);
        var childId = new HostId<EvidenceIdentity>(Guid.NewGuid());
        sink.WriteActivity(new(childId, child, request, clock.Now, clock.Now, HostOperationOutcome.Completed,
            [new(foreign.TraceId, foreign.SpanId), new(new string('f', 32), new string('1', 16))]));
        using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var service = Service(sink, clock);
        var page = await service.QueryAsync(new() { TraceId = root.TraceId }, null, TestContext.Current.CancellationToken);
        page.Records.Should().HaveCount(4);
        var projected = page.Records.Single(record => record.Reference.Source == EvidenceSource.Span && record.Reference.Id == childId);
        projected.RelatedSegments.Should().HaveCount(3);
        projected.RelatedSegments[0].Status.Should().Be(EvidenceSegmentStatus.ExpiredButPresent);
        projected.RelatedSegments[0].Record!.Id.Should().Be(rootId);
        projected.RelatedSegments[1].Status.Should().Be(EvidenceSegmentStatus.ExpiredButPresent);
        projected.RelatedSegments[2].Status.Should().Be(EvidenceSegmentStatus.MissingOrRemoved);
        var confined = await service.QueryAsync(new() { TraceId = root.TraceId, SessionId = request.SessionId },
            null, TestContext.Current.CancellationToken);
        confined.Records.Single(record => record.Reference.Id == childId && record.Reference.Source == EvidenceSource.Span)
            .RelatedSegments[1].Record.Should().BeNull();
        var link = page.Records.First(record => record.Reference.Source == EvidenceSource.Link);
        link.Reference.Citation.Should().Contain(":link:");
        var exact = await service.QueryAsync(new() { Record = link.Reference }, null, TestContext.Current.CancellationToken);
        exact.Records.Should().ContainSingle();
        var target = await service.QueryAsync(new() { Record = projected.RelatedSegments[0].Record },
            null, TestContext.Current.CancellationToken);
        target.Records.Single().Reference.Id.Should().Be(rootId);
        var hostileSpan = await service.QueryAsync(new() { TraceId = root.TraceId, SpanId = foreign.SpanId },
            null, TestContext.Current.CancellationToken);
        hostileSpan.Records.Should().BeEmpty();
        inspection.Request.SessionId.Should().NotBe(request.SessionId);
    }

    [WindowsFact]
    public async Task Snapshot_pages_do_not_expand_when_new_evidence_is_committed_between_reads()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using var inspection = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Evidence);
        using (var producer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteDiagnostic(Diagnostic(producer, 1));
            sink.WriteDiagnostic(Diagnostic(producer, 2));
        }
        var service = Service(sink, clock);
        var query = new EvidenceQuery { Limit = 1, Source = EvidenceSource.Log };
        var first = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        using (var producer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteDiagnostic(Diagnostic(producer, 3));
        }
        var last = await service.QueryAsync(query, first.Cursor, TestContext.Current.CancellationToken);
        last.Cursor.Should().BeNull();
        last.Records.Single().Reference.Should().NotBe(first.Records.Single().Reference);
        var fresh = await service.QueryAsync(query with { Limit = 50 }, null, TestContext.Current.CancellationToken);
        fresh.Records.Should().HaveCount(3);
    }

    [WindowsFact]
    public async Task Selective_real_store_search_stops_at_the_scan_bound_and_continues_without_lost_rows()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        using (var producer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteDiagnostic(Diagnostic(producer, 1));
        }
        var path = Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=PERSIST;";
            command.ExecuteNonQuery();
            command.CommandText = "SELECT evidence_id FROM application_log_events;";
            var seed = (string)command.ExecuteScalar()!;
            command.CommandText = "PRAGMA table_info(application_log_events);";
            var columns = new List<string>();
            using (var rows = command.ExecuteReader())
            {
                while (rows.Read()) { columns.Add(rows.GetString(1)); }
            }
            using var transaction = connection.BeginTransaction();
            command.Transaction = transaction;
            var projection = columns.Select(column => column switch
            {
                "evidence_id" => "$id",
                "envelope" => "replace(envelope,$seed,$id)",
                _ => column,
            });
            command.CommandText = $"INSERT INTO application_log_events SELECT {string.Join(',', projection)} FROM application_log_events WHERE evidence_id=$seed;";
            command.Parameters.AddWithValue("$seed", seed);
            var identity = command.Parameters.AddWithValue("$id", string.Empty);
            for (var index = 1; index < 4097; index++)
            {
                identity.Value = Guid.NewGuid().ToString("D");
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        var service = Service(sink, clock);
        var query = new EvidenceQuery { Source = EvidenceSource.Log, Text = "absent-safe-text" };
        var first = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        first.Status.Should().Be(EvidencePageStatus.ScanLimitReached);
        first.Records.Should().BeEmpty();
        first.Cursor.Should().NotBeNull();
        var second = await service.QueryAsync(query, first.Cursor, TestContext.Current.CancellationToken);
        second.Status.Should().Be(EvidencePageStatus.Available);
        second.Records.Should().BeEmpty();
        second.Cursor.Should().BeNull();
    }

    [WindowsFact]
    public async Task Real_query_logging_and_completed_spans_cannot_recursively_extend_a_page_snapshot()
    {
        using var fixture = new OwnedStorageFixture();
        using var provider = new EvidenceLoggerProvider([new WindowsSqliteEvidenceSink(fixture)], new Gaps());
        using var factory = LoggerFactory.Create(builder => builder.AddProvider(provider));
        var sink = new WindowsSqliteEvidenceSink(fixture);
        using (var producer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteDiagnostic(Diagnostic(producer, 1));
            sink.WriteDiagnostic(Diagnostic(producer, 2));
            producer.Complete(HostOperationOutcome.Completed);
        }
        using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var service = new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), new Access(), TimeProvider.System,
            factory.CreateLogger<DurableEvidenceQuery>());
        var query = new EvidenceQuery { Limit = 1, Source = EvidenceSource.Log };
        var first = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        var second = await service.QueryAsync(query, first.Cursor, TestContext.Current.CancellationToken);
        second.Cursor.Should().BeNull();
        first.Records.Single().EventId.Should().BeInRange(1, 2);
        second.Records.Single().EventId.Should().BeInRange(1, 2);
        var fresh = await service.QueryAsync(query with { Limit = 50 }, null, TestContext.Current.CancellationToken);
        fresh.Records.Should().HaveCount(2);
        inspection.Complete(HostOperationOutcome.Completed);
    }

    [Theory]
    [InlineData("envelope")]
    [InlineData("schema")]
    [InlineData("projection")]
    [InlineData("journal")]
    [InlineData("unexpected")]
    [InlineData("acl")]
    public async Task Corrupt_schema_envelope_correlation_and_private_access_fail_without_repair(string corruption)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        sink.WriteDiagnostic(Diagnostic(inspection, 1));
        var path = Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db");
        if (corruption is "envelope" or "schema" or "projection")
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=PERSIST;";
            command.ExecuteNonQuery();
            command.CommandText = corruption switch
            {
                "envelope" => "UPDATE application_log_events SET envelope='{}';",
                "schema" => "CREATE TABLE unknown_schema(content TEXT);",
                _ => "UPDATE application_log_events SET category='changed';",
            };
            command.ExecuteNonQuery();
        }
        else if (string.Equals(corruption, "journal", StringComparison.Ordinal)) { File.Delete(path + "-journal"); }
        else if (string.Equals(corruption, "unexpected", StringComparison.Ordinal)) { File.WriteAllText(path + ".foreign", "test"); }
        else
        {
            var security = new FileInfo(path).GetAccessControl();
            security.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null), FileSystemRights.Read, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        var bytes = File.ReadAllBytes(path);
        var invalid = async () => await Service(sink, new Clock()).QueryAsync(new(), null, TestContext.Current.CancellationToken);
        if (corruption is "acl" or "unexpected")
        {
            await invalid.Should().ThrowAsync<UnauthorizedAccessException>();
        }
        else { await invalid.Should().ThrowAsync<InvalidDataException>(); }
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [WindowsFact]
    public async Task Missing_store_and_cancelled_read_never_create_a_partition()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var result = await Service(sink, new Clock()).QueryAsync(new(), null, TestContext.Current.CancellationToken);
        result.Status.Should().Be(EvidencePageStatus.Unavailable);
        Directory.Exists(Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName)).Should().BeFalse();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = async () => await new WindowsSqliteEvidenceReader(sink)
            .ReadAsync(new(), null, inspection.Request, DateTimeOffset.UtcNow, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        var foreign = async () => await new WindowsSqliteEvidenceReader(sink)
            .ReadAsync(new(), null, HostRequest.Create(RequestOrigin.LocalUi), DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
        await foreign.Should().ThrowAsync<InvalidOperationException>();
        File.WriteAllText(Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName), "not-a-private-partition");
        var malformed = async () => await Service(sink, new Clock()).QueryAsync(new(), null, TestContext.Current.CancellationToken);
        await malformed.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    private static DurableEvidenceQuery Service(WindowsSqliteEvidenceSink sink, TimeProvider clock) =>
        new(new WindowsSqliteEvidenceReader(sink), new Access(), clock, NullLogger<DurableEvidenceQuery>.Instance);

    private static DiagnosticEnvelope Diagnostic(HostActivity activity, int index) => new(1, new(Guid.NewGuid()),
        DateTimeOffset.UtcNow, index, "Observed", "Information", "query.test", "Observed {Count}",
        new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
        {
            ["Count"] = new(EvidenceValueKind.WholeNumber, index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ["SecurityAudit"] = new(EvidenceValueKind.Boolean, "true"),
            ["SecretValue"] = new(EvidenceValueKind.Text, "synthetic-must-not-disclose"),
        }, [], TraceSnapshot.Capture(activity.Activity), activity.Request, null, activity.CorrelationId, activity.ApprovalId);

    private static TraceSnapshot Trace(string trace, string span, string? parent = null) =>
        new(trace, span, parent, ActivityTraceFlags.Recorded, "Kora.Application", "1.0.0", "session.request", ActivityKind.Internal);

    private static byte[] Hash(OwnedStorageFixture fixture)
    {
        var path = Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db");
        return SHA256.HashData(File.ReadAllBytes(path).Concat(File.ReadAllBytes(path + "-journal")).ToArray());
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

    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect => true; }
    private sealed class Gaps : IEvidenceGapReporter
    {
        public void Report(EvidenceGap gap) => throw new InvalidOperationException("Unexpected evidence gap: " + gap.Reason);
    }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch.AddDays(20000);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
