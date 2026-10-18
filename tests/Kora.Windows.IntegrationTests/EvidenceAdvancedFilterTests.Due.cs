using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

public sealed partial class EvidenceAdvancedFilterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DueEditsCancelAndReleaseTheRetiredReadWithoutReusingItsCursor(bool until)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.FromDueFilter = Edge.ToString("O", CultureInfo.InvariantCulture);
        await viewer.SearchAsync();
        viewer.Select(viewer.Records[0]);
        reader.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = viewer.NextAsync();
        if (until) { viewer.UntilDueFilter = Edge.ToString("O", CultureInfo.InvariantCulture); }
        else { viewer.FromDueFilter = ""; }
        reader.Token.IsCancellationRequested.Should().BeTrue();
        reader.Queries.Should().HaveCount(2);
        reader.Held.SetResult(Batch());
        await pending;
        viewer.Records.Should().BeEmpty();
        viewer.ResultText.Should().BeEmpty();
        viewer.CanNext.Should().BeFalse();
        viewer.CanReadTrace.Should().BeFalse();
        var released = () => reader.Token.WaitHandle;
        released.Should().Throw<ObjectDisposedException>();
        reader.Held = null;
        await viewer.SearchAsync();
        reader.Checkpoints[^1].Should().BeNull();
        viewer.Close();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("bad")]
    [InlineData("2026-10-10")]
    [InlineData("2026-10-10T00:00:00Z")]
    [InlineData("2026-10-10T00:00:00.0000000Z")]
    [InlineData("2026-10-10T00:00:00.000000+00:00")]
    [InlineData("2026-10-10T00:00:00.00000000+00:00")]
    [InlineData("2026-10-10T00:00:00.0000000-00:00")]
    [InlineData("2026-10-10T01:00:00.0000000+01:00")]
    [InlineData(" 2026-10-10T00:00:00.0000000+00:00")]
    [InlineData("2026-10-10T00:00:00.0000000+00:00 ")]
    [InlineData("2026-10-10T00:00:00.0000000+00:00\n")]
    [InlineData("2026-02-30T00:00:00.0000000+00:00")]
    [InlineData("10000-01-01T00:00:00.0000000+00:00")]
    [InlineData("9999-12-31T23:59:59.9999999-01:00")]
    public async Task InvalidDueTextNeverReadsRepairsOrFallsBackToAny(string text)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.FromDueFilter = text;
        await viewer.SearchAsync();
        viewer.Status.Should().Contain("failed").And.Contain("canonical UTC");
        viewer.FromDueFilter.Should().Be(text);
        viewer.FromDueFilter = "";
        viewer.UntilDueFilter = text;
        await viewer.SearchAsync();
        viewer.Status.Should().Contain("failed");
        viewer.UntilDueFilter.Should().Be(text);
        reader.Queries.Should().BeEmpty();
        viewer.Close();
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public async Task DueFieldsPreserveCanonicalBoundariesImmutableNextAndIndependentCommittedBounds(string culture)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var reader = new CapturingReader();
            var viewer = Viewer(reader);
            viewer.FromFilter = "2026-10-10T00:00:00Z";
            viewer.UntilFilter = "2026-10-10T00:00:00Z";
            viewer.FromDueFilter = DateTimeOffset.MinValue.ToString("O", CultureInfo.InvariantCulture);
            viewer.UntilDueFilter = DateTimeOffset.MaxValue.ToString("O", CultureInfo.InvariantCulture);
            await viewer.SearchAsync();
            var query = reader.Queries.Single();
            query.FromDueUtc.Should().Be(DateTimeOffset.MinValue);
            query.UntilDueUtc.Should().Be(DateTimeOffset.MaxValue);
            query.FromUtc.Should().Be(Edge);
            query.UntilUtc.Should().Be(Edge);
            await viewer.NextAsync();
            reader.Queries[^1].Should().BeSameAs(query);
            reader.Checkpoints[^1].Should().NotBeNull();
            viewer.FromDueFilter = Edge.AddTicks(1).ToString("O", CultureInfo.InvariantCulture);
            viewer.UntilDueFilter = Edge.ToString("O", CultureInfo.InvariantCulture);
            await viewer.SearchAsync();
            reader.Queries.Should().HaveCount(2);
            viewer.Status.Should().Contain("ArgumentException");
            viewer.ClearAdvancedFilters();
            viewer.FromDueFilter.Should().BeEmpty();
            viewer.UntilDueFilter.Should().BeEmpty();
            reader.Queries.Should().HaveCount(2);
            await viewer.SearchAsync();
            reader.Queries[^1].Should().Be(new EvidenceQuery());
            reader.Checkpoints[^1].Should().BeNull();
            viewer.Close();
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(EvidenceSource.All)]
    [InlineData(EvidenceSource.Log)]
    [InlineData(EvidenceSource.Audit)]
    [InlineData(EvidenceSource.Span)]
    [InlineData(EvidenceSource.Link)]
    [InlineData(EvidenceSource.DailyLog)]
    [InlineData(EvidenceSource.CombinedLog)]
    public async Task RealSourcesKeepInclusiveDueAndCommitRangesIndependentWithoutWrites(EvidenceSource source)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        var envelopes = WindowsDailyEvidenceReaderTests.Produce(owner, 3);
        for (var index = 0; index < 3; index++)
        {
            clock.Now = Edge.AddTicks(index - 1);
            var audit = new Kora.Core.Auditing.SecurityAuditEvent(Guid.NewGuid(),
                Kora.Core.Auditing.SecurityAuditCategory.ApplicationExecution, "host.query.version",
                Kora.Core.Auditing.SecurityAuditOutcome.Succeeded,
                Kora.Core.Auditing.SecurityAuditInitiator.LocalUser, "application.current");
            using var producer = HostActivity.BeginAudit(owner, audit);
            var envelope = envelopes[index] with { Trace = TraceSnapshot.Capture(producer.Activity), AuditCorrelationId = audit.CorrelationId };
            sink.WriteDiagnostic(envelope);
            sink.WriteAudit(new(envelope, audit));
            sink.WriteActivity(new(new(Guid.NewGuid()), envelope.Trace!, owner, clock.Now, clock.Now,
                HostOperationOutcome.Completed, [new(new string('a', 32), new string('b', 16))]));
        }
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261010.log", envelopes);
        var paths = Directory.GetFiles(fixture.LocalRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var hashes = paths.Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).ToArray();
        var service = new DurableEvidenceQuery(new WindowsEvidenceReader(new(sink), new(fixture)), new Access(),
            clock, NullLogger<DurableEvidenceQuery>.Instance);
        using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var query = new EvidenceQuery { Source = source };
        var baseline = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        baseline.Records.Should().HaveCount(source == EvidenceSource.All ? 12 : source == EvidenceSource.CombinedLog ? 6 : 3);
        var due = source == EvidenceSource.Audit ? new EvidenceRetentionPolicy().AuditDue(Edge)
            : new EvidenceRetentionPolicy().DiagnosticDue(Edge);
        var expected = source == EvidenceSource.DailyLog ? 0 : 1;
        var exact = query with { FromDueUtc = due, UntilDueUtc = due, FromUtc = Edge, UntilUtc = Edge };
        var page = await service.QueryAsync(exact, null, TestContext.Current.CancellationToken);
        page.Records.Should().HaveCount(source == EvidenceSource.All ? 3 : expected)
            .And.OnlyContain(record => record.DueUtc == due && record.CommittedUtc == Edge);
        var from = await service.QueryAsync(query with { FromDueUtc = due }, null, TestContext.Current.CancellationToken);
        var until = await service.QueryAsync(query with { UntilDueUtc = due }, null, TestContext.Current.CancellationToken);
        if (source == EvidenceSource.All)
        {
            from.Records.Should().HaveCount(9);
            until.Records.Should().HaveCount(6);
        }
        else
        {
            from.Records.Should().HaveCount(expected * 2);
            until.Records.Should().HaveCount(expected * 2);
        }
        var disjoint = await service.QueryAsync(exact with { FromUtc = Edge.AddTicks(1), UntilUtc = null }, null,
            TestContext.Current.CancellationToken);
        disjoint.Records.Should().BeEmpty();
        var beyond = await service.QueryAsync(query with { FromDueUtc = DateTimeOffset.MaxValue }, null,
            TestContext.Current.CancellationToken);
        beyond.Records.Should().BeEmpty();
        paths.Select(path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))).Should().Equal(hashes);
    }

    [Theory]
    [InlineData(EvidenceSource.Log)]
    [InlineData(EvidenceSource.CombinedLog)]
    public async Task InvalidSqliteRecordBeyondPageLimitCannotHideBehindDueExclusion(EvidenceSource source)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        var envelopes = WindowsDailyEvidenceReaderTests.Produce(HostRequest.Create(RequestOrigin.LocalUi), 53);
        foreach (var envelope in envelopes)
        {
            using var producer = HostActivity.BeginRoot(envelope.Host!, HostActivityLayer.Application, HostOperation.Request);
            sink.WriteDiagnostic(envelope with { Trace = TraceSnapshot.Capture(producer.Activity) });
        }
        clock.Now = Edge.AddDays(1);
        using (var producer = HostActivity.BeginRoot(envelopes[0].Host!, HostActivityLayer.Application, HostOperation.Request))
        {
            sink.WriteDiagnostic(envelopes[0] with { EvidenceId = new(Guid.NewGuid()), Trace = TraceSnapshot.Capture(producer.Activity) });
        }
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(fixture.LocalRoot, WindowsSqliteEvidenceSink.PartitionName, "evidence.db"), Pooling = false,
        }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE application_log_events SET envelope='{}' WHERE committed_utc=$later;";
            command.Parameters.AddWithValue("$later", clock.Now.UtcTicks);
            command.ExecuteNonQuery().Should().Be(1);
        }
        var viewer = Viewer(new WindowsEvidenceReader(new(sink), new(fixture)));
        viewer.Source = source;
        viewer.UntilDueFilter = new EvidenceRetentionPolicy().DiagnosticDue(Edge).ToString("O", CultureInfo.InvariantCulture);
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Status.Should().Contain("failed");
        viewer.CanNext.Should().BeFalse();
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidenceSource.DailyLog)]
    [InlineData(EvidenceSource.CombinedLog)]
    public async Task InvalidDailyRecordAfterPageCandidatesFailsEvenThoughDueIsUnknown(EvidenceSource source)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var envelopes = WindowsDailyEvidenceReaderTests.Produce(HostRequest.Create(RequestOrigin.LocalUi), 3);
        foreach (var envelope in envelopes)
        {
            using var producer = HostActivity.BeginRoot(envelope.Host!, HostActivityLayer.Application, HostOperation.Request);
            sink.WriteDiagnostic(envelope with { Trace = TraceSnapshot.Capture(producer.Activity) });
        }
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261010.log", envelopes);
        var outer = JsonNode.Parse(WindowsDailyEvidenceReaderTests.Line(envelopes[0]))!;
        outer["Properties"]!["EvidenceEnvelope"] = "{}";
        await File.AppendAllTextAsync(Path.Combine(fixture.LocalRoot, "Logs", "kora-20261010.log"),
            outer.ToJsonString() + "\n", new UTF8Encoding(false), TestContext.Current.CancellationToken);
        var service = new DurableEvidenceQuery(new WindowsEvidenceReader(new(sink), new(fixture)), new Access(),
            TimeProvider.System, NullLogger<DurableEvidenceQuery>.Instance);
        using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Desktop, HostOperation.Evidence);
        var page = await service.QueryAsync(new()
        {
            Source = source, Limit = 1, FromDueUtc = DateTimeOffset.MinValue,
        }, null, TestContext.Current.CancellationToken);
        page.Status.Should().Be(EvidencePageStatus.Corrupt);
        page.Records.Should().BeEmpty();
        page.Cursor.Should().BeNull();
    }
}
