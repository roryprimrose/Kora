using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests.Storage;

[Collection("Evidence query")]
public sealed class WindowsCombinedEvidenceReaderTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static EvidenceQuery Combined(int limit = 50) => new() { Source = EvidenceSource.CombinedLog, Limit = limit };

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(50)]
    public async Task Source_major_pages_keep_overlapping_ids_times_text_and_citations_distinct_without_writes(int limit)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var envelopes = Seed(sink, 53);
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", envelopes.Reverse());
        var before = Hashes(fixture);
        var service = Service(sink, fixture);
        using var inspection = Root();
        var actual = await Pages(service, Combined(limit));
        actual.Should().HaveCount(106);
        actual.Take(53).Select(record => record.Reference.Id).Should().Equal(
            envelopes.Select(envelope => envelope.EvidenceId).OrderBy(id => id.Value.ToString("D"), StringComparer.Ordinal));
        actual.Skip(53).Select(record => record.DailyProvenance!.SourceEvidenceId).Should().Equal(
            envelopes.Reverse().Select(envelope => envelope.EvidenceId));
        actual.Select(record => record.Reference.Citation).Distinct(StringComparer.Ordinal).Should().HaveCount(106);
        actual.Take(53).Should().OnlyContain(record => record.Reference.Source == EvidenceSource.Log
            && record.CommittedUtc != null && record.DueUtc != null && record.Audit == null);
        actual.Skip(53).Should().OnlyContain(record => record.Reference.Source == EvidenceSource.DailyLog
            && record.CommittedUtc == null && record.DueUtc == null && record.Audit == null && record.AuditSequence == null
            && record.Retention == EvidenceSegmentStatus.RetentionUnknown
            && record.RelatedSegments.Single().Status == EvidenceSegmentStatus.Unavailable);
        foreach (var record in new[] { actual[0], actual[53] })
        {
            var exact = await service.QueryAsync(Combined() with { Record = record.Reference }, null, Token);
            exact.Records.Single().Should().BeEquivalentTo(record);
        }
        var filters = Combined() with
        {
            SessionId = envelopes[0].Host!.SessionId, TaskId = envelopes[0].Host!.TaskId,
            EventId = 12, Severity = EvidenceSeverity.Information, Category = "daily.test", Text = "Observed",
            Property = new("Count", new(EvidenceValueKind.WholeNumber, "12")), TraceId = envelopes[0].Trace!.TraceId,
            FromUtc = envelopes[0].ObservedUtc, UntilUtc = envelopes[0].ObservedUtc,
        };
        (await service.QueryAsync(filters, null, Token)).Records.Should().HaveCount(2);
        (await service.QueryAsync(filters with { Text = "never search this" }, null, Token)).Records.Should().BeEmpty();
        var all = await service.QueryAsync(new(), null, Token);
        all.Records.Count.Should().BeInRange(1, 50);
        all.Records.Should().OnlyContain(record => record.Reference.Source == EvidenceSource.Log);
        (await service.QueryAsync(new() { Source = EvidenceSource.DailyLog, Limit = 1 }, null, Token))
            .Records.Single().DailyProvenance!.SourceEvidenceId.Should().Be(envelopes[^1].EvidenceId);
        Hashes(fixture).Should().Equal(before);
    }

    [WindowsFact]
    public async Task Serialized_byte_trimming_across_the_source_boundary_never_loses_a_record()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var envelopes = Seed(sink, 12, new string('\u754c', 1800) + "\"\\");
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", envelopes);
        using var inspection = Root();
        var actual = await Pages(Service(sink, fixture), Combined());
        actual.Should().HaveCount(24);
        actual.Select(record => record.Reference.Citation).Distinct(StringComparer.Ordinal).Should().HaveCount(24);
        actual.Should().OnlyContain(record => !record.ContentOmitted);
    }

    [WindowsFact]
    public async Task Native_selection_and_trace_read_remain_opt_in_ordinary_with_original_source_citations()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var envelopes = Seed(sink, 2);
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", envelopes);
        var viewer = new Kora.EvidenceViewModel(Service(sink, fixture), () => true,
            NullLogger<Kora.EvidenceViewModel>.Instance);
        Kora.EvidenceViewModel.Sources.Should().Contain(EvidenceSource.CombinedLog);
        viewer.Source.Should().Be(EvidenceSource.All);
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(2);
        viewer.Source = EvidenceSource.CombinedLog;
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(4);
        foreach (var source in new[] { EvidenceSource.Log, EvidenceSource.DailyLog })
        {
            viewer.Select(viewer.Records.First(record => record.Reference.Source == source));
            await viewer.ReadTraceAsync();
            viewer.Records.Should().HaveCount(4);
        }
        viewer.Close();
        viewer.Records.Should().BeEmpty();
    }

    [WindowsFact]
    public async Task Appends_and_new_days_do_not_extend_either_half_of_the_pair()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var envelopes = Seed(sink, 2);
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", envelopes);
        var service = Service(sink, fixture);
        using var inspection = Root();
        var query = Combined(1);
        var first = await service.QueryAsync(query, null, Token);
        var added = Seed(sink, 1);
        await File.AppendAllTextAsync(Log(fixture), WindowsDailyEvidenceReaderTests.Line(added[0]), Token);
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261008.log", added);
        var remainder = await Pages(service, query, first.Cursor);
        remainder.Should().HaveCount(3);
        remainder.Should().NotContain(record => record.Reference.Id == added[0].EvidenceId
            || record.DailyProvenance != null && record.DailyProvenance.SourceEvidenceId == added[0].EvidenceId);
        (await Pages(service, Combined())).Should().HaveCount(7);
    }

    [Theory]
    [InlineData("delete", EvidencePageStatus.MissingOrRemoved)]
    [InlineData("truncate", EvidencePageStatus.Changed)]
    [InlineData("replace", EvidencePageStatus.Changed)]
    [InlineData("mutate", EvidencePageStatus.Changed)]
    public async Task Daily_change_while_still_paging_SQLite_discards_all_sources(string change, EvidencePageStatus expected)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var envelopes = Seed(sink, 3);
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", envelopes);
        using var inspection = Root();
        var service = Service(sink, fixture);
        var first = await service.QueryAsync(Combined(1), null, Token);
        switch (change)
        {
            case "delete": File.Delete(Log(fixture)); break;
            case "truncate": await File.WriteAllTextAsync(Log(fixture), string.Empty, Token); break;
            case "replace":
                File.Move(Log(fixture), Log(fixture) + ".old");
                WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", envelopes);
                break;
            case "mutate":
                var bytes = await File.ReadAllBytesAsync(Log(fixture), Token);
                bytes[0] = (byte)' ';
                await File.WriteAllBytesAsync(Log(fixture), bytes, Token);
                break;
        }
        var next = await service.QueryAsync(Combined(1), first.Cursor, Token);
        next.Status.Should().Be(expected);
        next.UnavailableSources.Should().Equal(EvidenceSource.DailyLog);
        next.Records.Should().BeEmpty();
        next.Cursor.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_corrupt_daily_source_never_falls_back_to_SQLite(bool corrupt)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        Seed(sink, 1);
        if (corrupt)
        {
            WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", []);
            await File.WriteAllTextAsync(Log(fixture), "{}\n", Token);
        }
        using var inspection = Root();
        var service = Service(sink, fixture);
        var page = await service.QueryAsync(Combined(), null, Token);
        page.Status.Should().Be(corrupt ? EvidencePageStatus.Corrupt : EvidencePageStatus.Unavailable);
        page.UnavailableSources.Should().Equal(EvidenceSource.DailyLog);
        page.Records.Should().BeEmpty();
        (await service.QueryAsync(new(), null, Token)).Records.Should().ContainSingle();
    }

    [WindowsFact]
    public async Task Missing_SQLite_never_falls_back_to_daily_and_native_recovery_does_not_claim_end_of_results()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log",
            WindowsDailyEvidenceReaderTests.Produce(HostRequest.Create(RequestOrigin.LocalUi), 1));
        var viewer = new Kora.EvidenceViewModel(Service(new(fixture), fixture), () => true,
            NullLogger<Kora.EvidenceViewModel>.Instance) { Source = EvidenceSource.CombinedLog };
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Status.Should().Contain("could not be read").And.NotContain("End of");
        File.Exists(Path.Combine(fixture.LocalRoot, "Evidence", "evidence.db")).Should().BeFalse();
        viewer.Source = EvidenceSource.DailyLog;
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle();
        viewer.Close();
    }

    [WindowsFact]
    public async Task Permission_denial_is_not_repaired_or_hidden_by_a_working_SQLite_source()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", Seed(sink, 1));
        var file = new FileInfo(Log(fixture));
        var permissions = file.GetAccessControl();
        permissions.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Read, AccessControlType.Allow));
        file.SetAccessControl(permissions);
        var before = file.GetAccessControl().GetSecurityDescriptorBinaryForm();
        using var inspection = Root();
        var denied = async () => await Service(sink, fixture).QueryAsync(Combined(), null, Token);
        await denied.Should().ThrowAsync<UnauthorizedAccessException>();
        file.GetAccessControl().GetSecurityDescriptorBinaryForm().Should().Equal(before);
    }

    [WindowsFact]
    public async Task Pair_expiry_eviction_foreign_host_and_tampered_cursors_never_restart()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", Seed(sink, 2));
        var reader = new WindowsEvidenceReader(new(sink), new(fixture));
        var service = WindowsDailyEvidenceReaderTests.Service(reader);
        EvidencePage first;
        using (var inspection = Root())
        {
            first = await service.QueryAsync(Combined(1), null, Token);
            for (var index = 0; index < WindowsDailyEvidenceReader.MaximumSnapshots; index++)
            {
                await service.QueryAsync(Combined(1), null, Token);
            }
            var evicted = await service.QueryAsync(Combined(1), first.Cursor, Token);
            evicted.Status.Should().Be(EvidencePageStatus.SnapshotExpired);
            evicted.Records.Should().BeEmpty();
            var fresh = await reader.ReadAsync(Combined(1), null, inspection.Request, new Clock().Now, Token);
            var expired = await reader.ReadAsync(Combined(1), new(fresh.Snapshot, fresh.Candidates[0].Position),
                inspection.Request, new Clock().Now.AddMinutes(15), Token);
            expired.Status.Should().Be(EvidencePageStatus.SnapshotExpired);
            var changedQuery = async () => await service.QueryAsync(new() { Limit = 1 }, first.Cursor, Token);
            await changedQuery.Should().ThrowAsync<InvalidDataException>();
            var tampered = async () => await service.QueryAsync(Combined(1), first.Cursor + "x", Token);
            await tampered.Should().ThrowAsync<InvalidDataException>();
        }
        using var foreign = Root();
        var rejected = async () => await service.QueryAsync(Combined(1), first.Cursor, Token);
        await rejected.Should().ThrowAsync<InvalidDataException>();
        var otherHost = async () => await Service(sink, fixture).QueryAsync(Combined(1), first.Cursor, Token);
        await otherHost.Should().ThrowAsync<InvalidDataException>();
    }

    [WindowsFact]
    public async Task Cancellation_and_stopped_host_never_publish_or_keep_file_handles_open()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", Seed(sink, 2));
        var service = Service(sink, fixture);
        using var inspection = Root();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = async () => await service.QueryAsync(Combined(), null, cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        inspection.Complete(HostOperationOutcome.Completed);
        inspection.Dispose();
        var stopped = async () => await service.QueryAsync(Combined(), null, Token);
        await stopped.Should().ThrowAsync<InvalidOperationException>();
        using var exclusive = new FileStream(Log(fixture), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [WindowsFact]
    public async Task SQLite_pruning_invalidates_the_pair_even_after_transition_to_daily()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", Seed(sink, 1).Concat(
            WindowsDailyEvidenceReaderTests.Produce(HostRequest.Create(RequestOrigin.LocalUi), 2)));
        var service = Service(sink, fixture);
        using var inspection = Root();
        var first = await service.QueryAsync(Combined(2), null, Token);
        first.Records.Select(record => record.Reference.Source).Should().Equal(EvidenceSource.Log, EvidenceSource.DailyLog);
        using (var retention = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.HostSystem),
            HostActivityLayer.Windows, HostOperation.Retention))
        {
            var result = await new WindowsSqliteDiagnosticRetention(sink, new LateClock(),
                NullLogger<WindowsSqliteDiagnosticRetention>.Instance).RunAsync(Token);
            result.Logs.Should().Be(1);
        }
        var stale = async () => await service.QueryAsync(Combined(2), first.Cursor, Token);
        await stale.Should().ThrowAsync<InvalidDataException>();
        (await service.QueryAsync(new() { Source = EvidenceSource.DailyLog }, null, Token))
            .Records.Should().HaveCount(3);
    }

    [WindowsFact]
    public async Task Typed_audit_span_graph_and_old_SQLite_continuations_are_not_combined_ordinary_records()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var envelopes = Seed(sink, 2);
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261007.log", envelopes);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
            "host.query.version", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "application.current", null);
        using (var producer = HostActivity.BeginAudit(request, audit))
        {
            var diagnostic = envelopes[0] with
            {
                EvidenceId = new(Guid.NewGuid()), Host = request, Trace = TraceSnapshot.Capture(producer.Activity),
                AuditCorrelationId = audit.CorrelationId, ApprovalId = audit.ApprovalId,
            };
            sink.WriteAudit(new(diagnostic, audit));
            sink.WriteActivity(new(new(Guid.NewGuid()), diagnostic.Trace!, request, diagnostic.ObservedUtc,
                diagnostic.ObservedUtc, HostOperationOutcome.Completed, [], audit.CorrelationId, audit.ApprovalId));
        }
        var mirror = System.Text.Json.Nodes.JsonNode.Parse(WindowsDailyEvidenceReaderTests.Line(envelopes[0]))!;
        mirror["Properties"]!["EvidenceAuthority"] = "TypedAuditCopy";
        await File.AppendAllTextAsync(Log(fixture), mirror.ToJsonString() + "\n", Token);
        using var inspection = Root();
        var service = Service(sink, fixture);
        var all = await service.QueryAsync(new(), null, Token);
        all.Records.Should().HaveCount(4);
        var audits = await service.QueryAsync(new() { Source = EvidenceSource.Audit }, null, Token);
        audits.Records.Single().Audit.Should().BeEquivalentTo(audit);
        var span = all.Records.Single(record => record.Reference.Source == EvidenceSource.Span);
        (await service.QueryAsync(new() { Record = span.Reference }, null, Token)).Records.Should().ContainSingle();
        var ordinary = await service.QueryAsync(Combined(), null, Token);
        ordinary.Status.Should().Be(EvidencePageStatus.Partial);
        ordinary.Records.Should().HaveCount(4).And.OnlyContain(record => record.Audit == null
            && record.Reference.Source != EvidenceSource.Span && record.Reference.Source != EvidenceSource.Audit);
        ordinary.DailyReport!.AuditMirrors.Should().Be(1);
        var oldQuery = new EvidenceQuery { Source = EvidenceSource.Log, Limit = 1 };
        var oldFirst = await service.QueryAsync(oldQuery, null, Token);
        await service.QueryAsync(Combined(), null, Token);
        var oldNext = await service.QueryAsync(oldQuery, oldFirst.Cursor, Token);
        oldNext.Records.Should().ContainSingle();
        oldNext.Cursor.Should().BeNull();
        oldNext.Records[0].Reference.Should().NotBe(oldFirst.Records[0].Reference);
    }

    [WindowsFact]
    public async Task File_count_and_daily_deadline_refuse_SQLite_only_success()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var envelopes = Seed(sink, 1);
        for (var index = 0; index <= WindowsDailyEvidenceReader.MaximumFiles; index++)
        {
            WindowsDailyEvidenceReaderTests.Write(fixture,
                "kora-" + new DateOnly(2026, 1, 1).AddDays(index).ToString("yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture) + ".log", envelopes);
        }
        using var inspection = Root();
        var capped = await Service(sink, fixture).QueryAsync(Combined(), null, Token);
        capped.Status.Should().Be(EvidencePageStatus.ScanLimitReached);
        capped.UnavailableSources.Should().Equal(EvidenceSource.DailyLog);
        capped.Records.Should().BeEmpty();
        var timed = await WindowsDailyEvidenceReaderTests.Service(new WindowsEvidenceReader(new(sink),
            new(fixture, new DeadlineClock()))).QueryAsync(Combined(), null, Token);
        timed.Status.Should().Be(EvidencePageStatus.TimedOut);
        timed.Records.Should().BeEmpty();
    }

    private static DiagnosticEnvelope[] Seed(WindowsSqliteEvidenceSink sink, int count, string? text = null)
    {
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var envelopes = WindowsDailyEvidenceReaderTests.Produce(request, count);
        using var producer = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        envelopes = envelopes.Select(envelope => envelope with
        {
            Trace = TraceSnapshot.Capture(producer.Activity), MessageTemplate = text ?? envelope.MessageTemplate,
        }).ToArray();
        foreach (var envelope in envelopes) { sink.WriteDiagnostic(envelope); }
        return envelopes;
    }

    private static DurableEvidenceQuery Service(WindowsSqliteEvidenceSink sink, OwnedStorageFixture fixture) =>
        WindowsDailyEvidenceReaderTests.Service(new WindowsEvidenceReader(new(sink), new(fixture)));
    private static HostActivity Root() =>
        HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
    private static string Log(OwnedStorageFixture fixture) => Path.Combine(fixture.LocalRoot, "Logs", "kora-20261007.log");
    private static string[] Hashes(OwnedStorageFixture fixture) =>
        Directory.GetFiles(fixture.LocalRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(path => path + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
    private static async Task<List<EvidenceRecord>> Pages(DurableEvidenceQuery service, EvidenceQuery query, string? cursor = null)
    {
        var records = new List<EvidenceRecord>();
        var pages = 0;
        do
        {
            var page = await service.QueryAsync(query, cursor, Token);
            page.Status.Should().Be(EvidencePageStatus.Available);
            page.UnavailableSources.Should().BeEmpty();
            page.Records.Count.Should().BeLessThanOrEqualTo(query.Limit);
            DurableEvidenceQuery.Serialize(page).Length.Should().BeLessThanOrEqualTo(EvidencePage.MaximumBytes);
            records.AddRange(page.Records);
            cursor = page.Cursor;
            (++pages).Should().BeLessThan(150);
        } while (cursor is not null);
        return records;
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; } = new(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class LateClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new Clock().Now.AddDays(31);
    }
    private sealed class DeadlineClock : TimeProvider
    {
        private long calls;
        public override long TimestampFrequency => 1;
        public override long GetTimestamp() => Interlocked.Increment(ref calls) * 5;
    }
}
