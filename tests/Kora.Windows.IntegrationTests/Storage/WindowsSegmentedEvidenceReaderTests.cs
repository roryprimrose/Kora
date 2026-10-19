using System.Text.Json;

using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection("Evidence query")]
public sealed class WindowsSegmentedEvidenceReaderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(50)]
    public async Task Legacy_and_rotated_rows_share_global_order_page_bounds_and_exact_citations(int limit)
    {
        using var first = new OwnedStorageFixture();
        using var second = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var clock = new Clock();
        var legacy = new WindowsSqliteEvidenceSink(first, timeProvider: clock);
        var active = new WindowsSqliteEvidenceSink(second, timeProvider: clock);
        var expected = Seed(legacy, 27).Concat(Seed(active, 29)).OrderBy(row => row.EvidenceId.Value.ToString("D"),
            StringComparer.Ordinal).ToArray();
        var reader = Reader(new("store", 1, [new("legacy", 0, legacy), new("active", 0, active)]));
        var service = WindowsDailyEvidenceReaderTests.Service(reader);
        using var inspection = Root();
        var query = new EvidenceQuery { Source = EvidenceSource.Log, Limit = limit };
        var actual = new List<EvidenceRecord>();
        string? cursor = null;
        do
        {
            var page = await service.QueryAsync(query, cursor, Token);
            page.Status.Should().Be(EvidencePageStatus.Available);
            page.Records.Count.Should().BeLessThanOrEqualTo(limit);
            Kora.Application.Diagnostics.DurableEvidenceQuery.Serialize(page).Length
                .Should().BeLessThanOrEqualTo(EvidencePage.MaximumBytes);
            actual.AddRange(page.Records);
            cursor = page.Cursor;
        } while (cursor is not null);
        actual.Select(row => row.Reference.Id).Should().Equal(expected.Select(row => row.EvidenceId));
        actual.Select(row => row.Reference.Citation).Distinct(StringComparer.Ordinal).Should().HaveCount(expected.Length);
        foreach (var record in new[] { actual[0], actual[^1] })
        {
            var exact = await service.QueryAsync(query with { Record = record.Reference }, null, Token);
            exact.Records.Single().Should().BeEquivalentTo(record);
        }
    }

    [WindowsFact]
    public async Task Append_does_not_extend_any_snapshot_ceiling()
    {
        using var first = new OwnedStorageFixture();
        using var second = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var legacy = new WindowsSqliteEvidenceSink(first, timeProvider: new Clock());
        var active = new WindowsSqliteEvidenceSink(second, timeProvider: new Clock());
        Seed(legacy, 2);
        Seed(active, 2);
        var reader = Reader(new("store", 1, [new("legacy", 0, legacy), new("active", 0, active)]));
        using var inspection = Root();
        var query = new EvidenceQuery { Source = EvidenceSource.Log, Limit = 1 };
        var initial = await reader.ReadAsync(query, null, inspection.Request, new Clock().Now, Token);
        var added = Seed(active, 3);
        var checkpoint = new EvidenceReadCheckpoint(initial.Snapshot, initial.Candidates[0].Position);
        var remaining = await reader.ReadAsync(query with { Limit = 50 }, checkpoint, inspection.Request, new Clock().Now, Token);
        remaining.Candidates.Should().HaveCount(3);
        remaining.Candidates.Should().NotContain(candidate => added.Any(row => row.EvidenceId == candidate.Record.Reference.Id));
        var roundTrip = JsonSerializer.Deserialize<EvidenceSnapshot>(JsonSerializer.Serialize(initial.Snapshot))!;
        var replay = await reader.ReadAsync(query with { Limit = 50 }, checkpoint with { Snapshot = roundTrip },
            inspection.Request, new Clock().Now, Token);
        replay.Should().BeEquivalentTo(remaining);
    }

    [Theory]
    [InlineData("regression")]
    [InlineData("maintenance")]
    [InlineData("identity")]
    [InlineData("removed")]
    [InlineData("base")]
    [InlineData("version")]
    public async Task Incompatible_snapshot_never_admits_a_changed_inventory(string change)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        Seed(sink, 3);
        var set = new EvidenceSegmentReadSet("store", 1, [new("legacy", 0, sink)]);
        var reader = new WindowsSegmentedEvidenceReader((read, token) =>
        {
            token.ThrowIfCancellationRequested();
            return read(set);
        });
        using var inspection = Root();
        var query = new EvidenceQuery { Source = EvidenceSource.Log, Limit = 1 };
        var initial = await reader.ReadAsync(query, null, inspection.Request, new Clock().Now, Token);
        var snapshot = initial.Snapshot;
        switch (change)
        {
            case "regression": set = set with { InventoryGeneration = 0 }; break;
            case "maintenance":
                set = set with { InventoryGeneration = 2 };
                using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"),
                    Mode = SqliteOpenMode.ReadWrite, Pooling = false,
                }.ToString()))
                {
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "PRAGMA journal_mode=PERSIST; DELETE FROM application_log_events WHERE rowid=(SELECT max(rowid) FROM application_log_events);";
                    command.ExecuteNonQuery();
                }
                break;
            case "identity": set = set with { StoreIdentity = "other" }; break;
            case "removed": set = set with { Segments = [new("new", 0, sink)] }; break;
            case "base": set = set with { Segments = [new("legacy", 8, sink)] }; break;
            case "version": snapshot = snapshot with { Segmented = snapshot.Segmented! with { Version = 99 } }; break;
        }
        var resume = async () => await reader.ReadAsync(query, new(snapshot, initial.Candidates[0].Position),
            inspection.Request, new Clock().Now, Token);
        await resume.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Compatible_rotation_preserves_original_ceilings_and_never_admits_the_new_segment()
    {
        using var first = new OwnedStorageFixture();
        using var second = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var legacy = new WindowsSqliteEvidenceSink(first, timeProvider: new Clock());
        var active = new WindowsSqliteEvidenceSink(second, timeProvider: new Clock());
        var original = Seed(legacy, 3);
        var added = Seed(active, 2);
        var set = new EvidenceSegmentReadSet("store", 1, [new("legacy", 0, legacy)]);
        var reader = new WindowsSegmentedEvidenceReader((read, token) =>
        {
            token.ThrowIfCancellationRequested();
            return read(set);
        });
        using var inspection = Root();
        var query = new EvidenceQuery { Source = EvidenceSource.Log, Limit = 1 };
        var initial = await reader.ReadAsync(query, null, inspection.Request, new Clock().Now, Token);
        set = set with { InventoryGeneration = 2, Segments = [new("legacy", 0, legacy), new("active", 0, active)] };
        var remaining = await reader.ReadAsync(query with { Limit = 50 },
            new(initial.Snapshot, initial.Candidates[0].Position), inspection.Request, new Clock().Now, Token);
        remaining.Candidates.Should().HaveCount(2).And.OnlyContain(candidate =>
            original.Any(row => row.EvidenceId == candidate.Record.Reference.Id));
        remaining.Candidates.Should().NotContain(candidate => added.Any(row => row.EvidenceId == candidate.Record.Reference.Id));
        remaining.Snapshot.Should().Be(initial.Snapshot);
    }

    [Theory]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(4100)]
    public async Task Scan_budget_is_global_not_per_segment_and_resumes_after_the_last_scanned_row(int total)
    {
        using var first = new OwnedStorageFixture();
        using var second = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var legacy = new WindowsSqliteEvidenceSink(first, timeProvider: new Clock());
        var active = new WindowsSqliteEvidenceSink(second, timeProvider: new Clock());
        var seed1 = Seed(legacy, 1)[0];
        var seed2 = Seed(active, 1)[0];
        WindowsSqliteEvidenceSinkTests.SeedDiagnosticHistory(first, seed1, total / 2);
        WindowsSqliteEvidenceSinkTests.SeedDiagnosticHistory(second, seed2, total - total / 2);
        var reader = Reader(new("store", 1, [new("legacy", 0, legacy), new("active", 0, active)]));
        using var inspection = Root();
        var query = new EvidenceQuery { Source = EvidenceSource.Log, Text = "not present" };
        var capped = await reader.ReadAsync(query, null, inspection.Request, new Clock().Now, Token);
        capped.ScanLimitReached.Should().Be(total > WindowsSqliteEvidenceReader.MaximumScannedRows);
        capped.HasMore.Should().Be(total > WindowsSqliteEvidenceReader.MaximumScannedRows);
        capped.Candidates.Should().BeEmpty();
        capped.ScannedThrough.Should().NotBeNull();
        var remainder = await reader.ReadAsync(query, new(capped.Snapshot, capped.ScannedThrough),
            inspection.Request, new Clock().Now, Token);
        remainder.HasMore.Should().BeFalse();
        remainder.ScanLimitReached.Should().BeFalse();
        if (total > WindowsSqliteEvidenceReader.MaximumScannedRows)
        {
            remainder.ScannedThrough.Should().NotBe(capped.ScannedThrough);
        }
        var visible = await reader.ReadAsync(query with { Text = null },
            new(capped.Snapshot, capped.ScannedThrough), inspection.Request, new Clock().Now, Token);
        visible.Candidates.Should().HaveCount(total - WindowsSqliteEvidenceReader.MaximumScannedRows);
    }

    [WindowsFact]
    public async Task Audit_offsets_are_applied_before_sequence_filters_without_changing_citations()
    {
        using var first = new OwnedStorageFixture();
        using var second = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var legacy = new WindowsSqliteEvidenceSink(first, timeProvider: new Clock());
        var active = new WindowsSqliteEvidenceSink(second, timeProvider: new Clock());
        var oldId = WriteAudit(legacy);
        var newId = WriteAudit(active);
        var reader = Reader(new("store", 1, [new("legacy", 0, legacy), new("active", 1, active)]));
        using var inspection = Root();
        var all = await reader.ReadAsync(new() { Source = EvidenceSource.Audit }, null,
            inspection.Request, new Clock().Now, Token);
        all.Candidates.Single(row => row.Record.Reference.Id == oldId).Record.AuditSequence.Should().Be(1);
        all.Candidates.Single(row => row.Record.Reference.Id == newId).Record.AuditSequence.Should().Be(2);
        var filtered = await reader.ReadAsync(new()
        {
            Source = EvidenceSource.Audit, FromAuditSequence = 2, UntilAuditSequence = 2,
        }, null, inspection.Request, new Clock().Now, Token);
        filtered.Candidates.Single().Record.Reference.Should().Be(new EvidenceReference(EvidenceSource.Audit, newId));
        var overflowing = Reader(new("store", 1, [new("active", long.MaxValue, active)]));
        var overflow = () => overflowing.ReadAsync(new() { Source = EvidenceSource.Audit }, null,
            inspection.Request, new Clock().Now, Token).AsTask();
        await overflow.Should().ThrowAsync<OverflowException>();
    }

    [WindowsFact]
    public async Task Cross_segment_spans_and_links_resolve_only_inside_the_selected_session_and_snapshot()
    {
        using var first = new OwnedStorageFixture();
        using var second = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var legacy = new WindowsSqliteEvidenceSink(first, timeProvider: new Clock());
        var active = new WindowsSqliteEvidenceSink(second, timeProvider: new Clock());
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        CompletedActivityEnvelope parent;
        CompletedActivityEnvelope child;
        DiagnosticEnvelope diagnostic;
        using (var producer = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            var trace = TraceSnapshot.Capture(producer.Activity)!;
            parent = new(new(Guid.NewGuid()), trace, request, new Clock().Now, new Clock().Now,
                HostOperationOutcome.Completed, []);
            legacy.WriteActivity(parent);
            using (var nested = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Evidence))
            {
                var childTrace = TraceSnapshot.Capture(nested.Activity)!;
                child = new(new(Guid.NewGuid()), childTrace, request, new Clock().Now, new Clock().Now,
                    HostOperationOutcome.Completed, [new(trace.TraceId, trace.SpanId)]);
                active.WriteActivity(child);
            }
            diagnostic = Diagnostic(producer);
            active.WriteDiagnostic(diagnostic);
        }
        var foreignRequest = HostRequest.Create(RequestOrigin.LocalUi);
        using (var foreignProducer = HostActivity.BeginRoot(foreignRequest, HostActivityLayer.Application, HostOperation.Request))
        {
            active.WriteActivity(new(new(Guid.NewGuid()), TraceSnapshot.Capture(foreignProducer.Activity)!,
                foreignRequest, new Clock().Now, new Clock().Now, HostOperationOutcome.Completed,
                [new(parent.Trace.TraceId, parent.Trace.SpanId)]));
        }
        var reader = Reader(new("store", 1, [new("legacy", 0, legacy), new("active", 0, active)]));
        using var inspection = Root();
        var query = new EvidenceQuery { SessionId = request.SessionId };
        var initial = await reader.ReadAsync(query, null, inspection.Request, new Clock().Now, Token);
        var parentReference = new EvidenceReference(EvidenceSource.Span, parent.EvidenceId);
        initial.Candidates.Single(row => row.Record.Reference.Id == diagnostic.EvidenceId)
            .Record.RelatedSegments.Single().Record.Should().Be(parentReference);
        initial.Candidates.Single(row => row.Record.Reference == new EvidenceReference(EvidenceSource.Span, child.EvidenceId))
            .Record.RelatedSegments.Should().HaveCount(2).And.OnlyContain(segment => segment.Record == parentReference);
        var link = initial.Candidates.Single(row => row.Record.Reference.Source == EvidenceSource.Link).Record;
        link.RelatedSegments.Single().Record.Should().Be(parentReference);
        var exactLink = await reader.ReadAsync(query with { Record = link.Reference }, null,
            inspection.Request, new Clock().Now, Token);
        exactLink.Candidates.Single().Record.Should().BeEquivalentTo(link);
        var foreign = await reader.ReadAsync(query with { SessionId = foreignRequest.SessionId },
            null, inspection.Request, new Clock().Now, Token);
        foreign.Candidates.Should().HaveCount(2).And.OnlyContain(candidate =>
            candidate.Record.Host!.SessionId == foreignRequest.SessionId);
        foreign.Candidates.SelectMany(candidate => candidate.Record.RelatedSegments).Should()
            .OnlyContain(segment => segment.Record == null && segment.Status == EvidenceSegmentStatus.MissingOrRemoved);
    }

    [WindowsFact]
    public async Task Appended_span_cannot_fill_a_missing_reference_in_an_existing_snapshot()
    {
        using var first = new OwnedStorageFixture();
        using var second = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var legacy = new WindowsSqliteEvidenceSink(first, timeProvider: new Clock());
        var active = new WindowsSqliteEvidenceSink(second, timeProvider: new Clock());
        legacy.Initialize();
        var reader = Reader(new("store", 1, [new("legacy", 0, legacy), new("active", 0, active)]));
        using var producer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var diagnostic = Diagnostic(producer);
        active.WriteDiagnostic(diagnostic);
        var query = new EvidenceQuery { Source = EvidenceSource.Log, SessionId = producer.Request.SessionId };
        EvidenceReadBatch initial;
        using (var inspection = Root())
        {
            initial = await reader.ReadAsync(query, null, inspection.Request, new Clock().Now, Token);
            initial.Candidates.Single().Record.RelatedSegments.Single().Status
                .Should().Be(EvidenceSegmentStatus.MissingOrRemoved);
        }
        var parentId = new HostId<EvidenceIdentity>(Guid.NewGuid());
        legacy.WriteActivity(new(parentId, diagnostic.Trace!, producer.Request,
            new Clock().Now, new Clock().Now, HostOperationOutcome.Completed, []));
        using (var inspection = Root())
        {
            var old = await reader.ReadAsync(query, new(initial.Snapshot, null), inspection.Request, new Clock().Now, Token);
            old.Candidates.Single().Record.RelatedSegments.Single().Status
                .Should().Be(EvidenceSegmentStatus.MissingOrRemoved);
            var fresh = await reader.ReadAsync(query, null, inspection.Request, new Clock().Now, Token);
            fresh.Candidates.Single().Record.RelatedSegments.Single().Record
                .Should().Be(new EvidenceReference(EvidenceSource.Span, parentId));
        }
    }

    [WindowsFact]
    public async Task Foreign_and_cancelled_requests_fail_before_opening_the_readset()
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reads = 0;
        var reader = new WindowsSegmentedEvidenceReader((_, _) =>
        {
            reads++;
            throw new InvalidOperationException("This test must not open a readset.");
        });
        using var inspection = Root();
        var foreign = () => reader.ReadAsync(new(), null, HostRequest.Create(RequestOrigin.LocalUi), new Clock().Now, Token);
        foreign.Should().Throw<InvalidOperationException>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = async () => await reader.ReadAsync(new(), null, inspection.Request, new Clock().Now, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        reads.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_history_in_any_child_refuses_the_entire_read_even_when_filters_exclude_it(bool corruptLegacy)
    {
        using var first = new OwnedStorageFixture();
        using var second = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var legacy = new WindowsSqliteEvidenceSink(first, timeProvider: new Clock());
        var active = new WindowsSqliteEvidenceSink(second, timeProvider: new Clock());
        Seed(legacy, 1);
        var expected = Seed(active, 1)[0];
        var fixture = corruptLegacy ? first : second;
        var path = Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db");
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=PERSIST; UPDATE application_log_events SET event_name='forged';";
            command.ExecuteNonQuery();
        }
        var bytes = File.ReadAllBytes(path);
        var reader = Reader(new("store", 1, [new("legacy", 0, legacy), new("active", 0, active)]));
        using var inspection = Root();
        var read = () => reader.ReadAsync(new() { Source = EvidenceSource.Log, SessionId = expected.Host!.SessionId },
            null, inspection.Request, new Clock().Now, Token).AsTask();
        await read.Should().ThrowAsync<InvalidDataException>();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    private static WindowsSegmentedEvidenceReader Reader(EvidenceSegmentReadSet set) => new((read, token) =>
    {
        token.ThrowIfCancellationRequested();
        return read(set);
    });

    private static DiagnosticEnvelope[] Seed(WindowsSqliteEvidenceSink sink, int count)
    {
        using var producer = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Request);
        var records = Enumerable.Range(0, count).Select(_ => Diagnostic(producer)).ToArray();
        foreach (var record in records) { sink.WriteDiagnostic(record); }
        return records;
    }

    private static DiagnosticEnvelope Diagnostic(HostActivity producer) => new(1, new(Guid.NewGuid()), new Clock().Now,
        1, null, "Information", "segmented.test", "Retained diagnostic",
        new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), [],
        TraceSnapshot.Capture(producer.Activity), producer.Request, null);

    private static HostId<EvidenceIdentity> WriteAudit(WindowsSqliteEvidenceSink sink)
    {
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            "fixture.segmented", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser, "fixture");
        using var host = HostActivity.BeginAudit(HostRequest.Create(RequestOrigin.LocalUi), audit);
        var diagnostic = Diagnostic(host) with { AuditCorrelationId = audit.CorrelationId, ApprovalId = audit.ApprovalId };
        sink.WriteAudit(new(diagnostic, audit));
        return diagnostic.EvidenceId;
    }

    private static HostActivity Root() => HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
        HostActivityLayer.Desktop, HostOperation.Evidence);

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; } = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
