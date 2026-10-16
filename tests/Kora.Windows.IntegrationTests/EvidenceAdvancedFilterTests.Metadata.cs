using System.Diagnostics;

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

public sealed partial class EvidenceAdvancedFilterTests
{
    [WindowsFact]
    public async Task CommittedDiagnosticAuditSequenceUsesInclusiveEdgesAndNullMetadataDoesNotMatch()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        using var root = HostActivity.BeginRoot(owner, HostActivityLayer.Application, HostOperation.Request);
        for (var index = 0; index < 3; index++)
        {
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
                "host.query.version", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser, "application.current");
            using var producer = HostActivity.BeginAudit(owner, audit);
            var envelope = WindowsDailyEvidenceReaderTests.Produce(owner, 1)[0] with
            {
                Trace = TraceSnapshot.Capture(producer.Activity), AuditCorrelationId = audit.CorrelationId,
                EventName = "Event.Name", ExceptionType = "System.IOException",
            };
            sink.WriteAudit(new(envelope, audit));
            sink.WriteDiagnostic(envelope);
        }
        var viewer = Viewer(new WindowsSqliteEvidenceReader(sink));
        viewer.Source = EvidenceSource.All;
        viewer.FromAuditSequenceFilter = "2";
        viewer.UntilAuditSequenceFilter = "2";
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle().Which.AuditSequence.Should().Be(2);
        viewer.FromAuditSequenceFilter = "";
        await viewer.SearchAsync();
        viewer.Records.Select(record => record.AuditSequence).Should().Equal(1, 2);
        viewer.FromAuditSequenceFilter = "2";
        viewer.UntilAuditSequenceFilter = "";
        await viewer.SearchAsync();
        viewer.Records.Select(record => record.AuditSequence).Should().Equal(2, 3);
        viewer.ClearAdvancedFilters();
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(6);
        viewer.Source = EvidenceSource.Span;
        viewer.EventNameFilter = "Event.Name";
        viewer.ExceptionTypeFilter = "System.IOException";
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Close();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("1\n")]
    [InlineData("1.0")]
    [InlineData("1e1")]
    [InlineData("1,000")]
    [InlineData("\u0661")]
    [InlineData("9223372036854775808")]
    [InlineData(" ")]
    public async Task NoncanonicalSequenceNeverReadsOrFallsBackToAny(string value)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.FromAuditSequenceFilter = value;
        await viewer.SearchAsync();
        viewer.Status.Should().Contain("failed");
        viewer.FromAuditSequenceFilter = "";
        viewer.UntilAuditSequenceFilter = value;
        await viewer.SearchAsync();
        viewer.Status.Should().Contain("failed");
        viewer.UntilAuditSequenceFilter = "1";
        viewer.FromAuditSequenceFilter = "2";
        await viewer.SearchAsync();
        viewer.Status.Should().Contain("failed");
        reader.Queries.Should().BeEmpty();
        viewer.ResultText.Should().BeEmpty();
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidenceSource.All)]
    [InlineData(EvidenceSource.Log)]
    [InlineData(EvidenceSource.DailyLog)]
    [InlineData(EvidenceSource.CombinedLog)]
    public async Task RetainedDiagnosticNamesMatchExactlyWithoutReadingExceptionContent(EvidenceSource source)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        DiagnosticEnvelope[] envelopes;
        using (var producer = HostActivity.BeginRoot(owner, HostActivityLayer.Application, HostOperation.Request))
        {
            envelopes = WindowsDailyEvidenceReaderTests.Produce(owner, 3).Select((envelope, index) => envelope with
            {
                EventName = index == 0 ? null : "Event.Name",
                ExceptionType = index == 2 ? "System.IOException" : null,
                Trace = TraceSnapshot.Capture(producer.Activity),
            }).ToArray();
            foreach (var envelope in envelopes) { sink.WriteDiagnostic(envelope); }
        }
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261010.log", envelopes);
        var viewer = Viewer(new WindowsEvidenceReader(new(sink), new(fixture)));
        viewer.Source = source;
        viewer.EventNameFilter = "Event.Name";
        viewer.ExceptionTypeFilter = "System.IOException";
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(source == EvidenceSource.CombinedLog ? 2 : 1)
            .And.OnlyContain(record => record.EventName == "Event.Name" && record.ExceptionType == "System.IOException");
        viewer.ResultText.Should().NotContain("ExceptionMessage").And.NotContain("StackTrace");
        viewer.EventNameFilter = "event.name";
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.EventNameFilter = "Event.Name";
        viewer.ExceptionTypeFilter = "system.ioexception";
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.ExceptionTypeFilter = "System.IOException";
        viewer.FromAuditSequenceFilter = "1";
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.ClearAdvancedFilters();
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(source == EvidenceSource.CombinedLog ? 6 : 3);
        viewer.Close();
    }

    [Fact]
    public async Task MetadataContinuationRetainsExpiryAndFailsAfterRetentionRemovesItsCeiling()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        var trace = new TraceSnapshot(new string('a', 32), new string('b', 16), "0123456789abcdef",
            ActivityTraceFlags.Recorded, "Kora.Application", "1", "request", ActivityKind.Internal);
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        for (var index = 0; index < 3; index++)
        {
            sink.WriteActivity(new(new(Guid.NewGuid()), trace, owner, Edge, Edge, HostOperationOutcome.Completed, []));
        }
        var service = new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), new Access(), clock,
            NullLogger<DurableEvidenceQuery>.Instance);
        using var inspection = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Desktop, HostOperation.Evidence);
        var query = new EvidenceQuery
        {
            Source = EvidenceSource.Span, Limit = 1, TraceId = trace.TraceId, SpanId = trace.SpanId,
            ParentSpanId = trace.ParentSpanId, Outcome = HostOperationOutcome.Completed,
            ContentAvailability = EvidenceContentAvailability.Present,
        };
        clock.Now = Edge.AddDays(31);
        var first = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        first.Records.Should().ContainSingle().Which.Retention.Should().Be(EvidenceSegmentStatus.ExpiredButPresent);
        first.Cursor.Should().NotBeNull();
        var second = await service.QueryAsync(query, first.Cursor, TestContext.Current.CancellationToken);
        second.Records.Should().ContainSingle();
        sink.PruneOrdinaryDiagnostics(clock.Now, TestContext.Current.CancellationToken);
        var removed = () => service.QueryAsync(query, second.Cursor, TestContext.Current.CancellationToken).AsTask();
        await removed.Should().ThrowAsync<InvalidDataException>();
        var fresh = await service.QueryAsync(query, null, TestContext.Current.CancellationToken);
        fresh.Records.Should().BeEmpty();
        fresh.Status.Should().Be(EvidencePageStatus.MissingOrRemoved);
    }

    [Theory]
    [InlineData(EvidenceSource.All)]
    [InlineData(EvidenceSource.Span)]
    [InlineData(EvidenceSource.Link)]
    public async Task RealActivityParentOutcomeAndContentComposeWithTraceSpanAndEveryOutcome(EvidenceSource source)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        var trace = new TraceSnapshot(new string('a', 32), new string('b', 16), "0123456789abcdef",
            ActivityTraceFlags.Recorded, "Kora.Application", "1", "request", ActivityKind.Internal);
        foreach (var outcome in Enum.GetValues<HostOperationOutcome>())
        {
            sink.WriteActivity(new(new(Guid.NewGuid()), trace, owner, Edge, Edge, outcome,
                [new(new string('c', 32), new string('d', 16))]));
        }
        var viewer = Viewer(new WindowsSqliteEvidenceReader(sink));
        viewer.Source = source;
        viewer.TraceFilter = trace.TraceId;
        viewer.SpanFilter = trace.SpanId;
        viewer.ParentSpanFilter = trace.ParentSpanId!;
        viewer.SessionFilter = owner.SessionId.Value.ToString("D");
        viewer.TaskFilter = owner.TaskId.Value.ToString("D");
        viewer.RequestFilter = owner.RequestId.Value.ToString("D");
        viewer.FromFilter = Edge.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        viewer.UntilFilter = viewer.FromFilter;
        viewer.CategoryFilter = trace.Source;
        viewer.SafeText = trace.Name;
        viewer.ContentAvailability = EvidenceContentAvailability.Present;
        foreach (var choice in EvidenceViewModel.OutcomeChoices)
        {
            viewer.Outcome = choice;
            await viewer.SearchAsync();
            viewer.Records.Should().HaveCount((choice.Value is null ? 4 : 1) * (source == EvidenceSource.All ? 2 : 1))
                .And.OnlyContain(record => string.Equals(record.Trace!.ParentSpanId, trace.ParentSpanId, StringComparison.Ordinal)
                    && !record.ContentOmitted && (choice.Value == null || record.Outcome == choice.Value));
            viewer.ContentAvailability = EvidenceContentAvailability.Omitted;
            await viewer.SearchAsync();
            viewer.Records.Should().BeEmpty();
            viewer.Status.Should().StartWith("MissingOrRemoved");
            viewer.ContentAvailability = EvidenceContentAvailability.Present;
        }
        viewer.ParentSpanFilter = trace.SpanId;
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidenceSource.Log)]
    [InlineData(EvidenceSource.DailyLog)]
    [InlineData(EvidenceSource.CombinedLog)]
    public async Task RealDiagnosticsHaveNullOutcomeAndExactParentWithoutInventedContent(EvidenceSource source)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        DiagnosticEnvelope[] envelopes;
        using (var root = HostActivity.BeginRoot(owner, HostActivityLayer.Application, HostOperation.Request))
        using (var producer = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Request))
        {
            envelopes = WindowsDailyEvidenceReaderTests.Produce(owner, 2).Select(envelope => envelope with
            {
                Trace = TraceSnapshot.Capture(producer.Activity),
            }).ToArray();
            foreach (var envelope in envelopes) { sink.WriteDiagnostic(envelope); }
        }
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261010.log", envelopes);
        var viewer = Viewer(new WindowsEvidenceReader(new(sink), new(fixture)));
        viewer.Source = source;
        viewer.ParentSpanFilter = envelopes[0].Trace!.ParentSpanId!;
        viewer.ContentAvailability = EvidenceContentAvailability.Present;
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(source == EvidenceSource.CombinedLog ? 4 : 2)
            .And.OnlyContain(record => record.Outcome == null && !record.ContentOmitted);
        foreach (var choice in EvidenceViewModel.OutcomeChoices.Skip(1))
        {
            viewer.Outcome = choice;
            await viewer.SearchAsync();
            viewer.Records.Should().BeEmpty();
            viewer.Status.Should().StartWith("Available");
        }
        viewer.ClearAdvancedFilters();
        await viewer.SearchAsync();
        viewer.Records.Should().NotBeEmpty();
        viewer.ContentAvailability = EvidenceContentAvailability.Omitted;
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Close();
    }

    [Theory]
    [InlineData("parent", "success")]
    [InlineData("parent", "failure")]
    [InlineData("parent", "cancel")]
    [InlineData("outcome", "success")]
    [InlineData("outcome", "failure")]
    [InlineData("outcome", "cancel")]
    [InlineData("content", "success")]
    [InlineData("content", "failure")]
    [InlineData("content", "cancel")]
    [InlineData("event", "success")]
    [InlineData("event", "failure")]
    [InlineData("event", "cancel")]
    [InlineData("exception", "success")]
    [InlineData("exception", "failure")]
    [InlineData("exception", "cancel")]
    [InlineData("fromSequence", "success")]
    [InlineData("fromSequence", "failure")]
    [InlineData("fromSequence", "cancel")]
    [InlineData("untilSequence", "success")]
    [InlineData("untilSequence", "failure")]
    [InlineData("untilSequence", "cancel")]
    public async Task EveryMetadataEditCancelsAndRetiresContinuationAndLateCallbacks(string field, string completion)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        await viewer.SearchAsync();
        viewer.Select(viewer.Records[0]);
        reader.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = viewer.NextAsync();
        switch (field)
        {
            case "parent": viewer.ParentSpanFilter = "0123456789abcdef"; break;
            case "outcome": viewer.Outcome = EvidenceViewModel.OutcomeChoices[1]; break;
            case "content": viewer.ContentAvailability = EvidenceContentAvailability.Omitted; break;
            case "event": viewer.EventNameFilter = "Event.Name"; break;
            case "exception": viewer.ExceptionTypeFilter = "System.IOException"; break;
            case "fromSequence": viewer.FromAuditSequenceFilter = "1"; break;
            case "untilSequence": viewer.UntilAuditSequenceFilter = "9223372036854775807"; break;
        }
        reader.Token.IsCancellationRequested.Should().BeTrue();
        reader.Queries.Should().HaveCount(2);
        if (completion is "success") { reader.Held.SetResult(Batch()); }
        else { reader.Held.SetException(completion is "cancel" ? new OperationCanceledException() : new IOException("Retired.")); }
        await pending;
        viewer.Status.Should().StartWith("Filters changed");
        viewer.Records.Should().BeEmpty();
        viewer.Segments.Should().BeEmpty();
        viewer.ResultText.Should().BeEmpty();
        viewer.CanNext.Should().BeFalse();
        viewer.CanReadTrace.Should().BeFalse();
        reader.Held = null;
        await viewer.SearchAsync();
        reader.Checkpoints[^1].Should().BeNull();
        reader.Queries[^1].ParentSpanId.Should().Be(field is "parent" ? "0123456789abcdef" : null);
        reader.Queries[^1].Outcome.Should().Be(field is "outcome" ? HostOperationOutcome.Completed : null);
        reader.Queries[^1].ContentAvailability.Should().Be(field is "content"
            ? EvidenceContentAvailability.Omitted : EvidenceContentAvailability.Any);
        reader.Queries[^1].EventName.Should().Be(field is "event" ? "Event.Name" : null);
        reader.Queries[^1].ExceptionType.Should().Be(field is "exception" ? "System.IOException" : null);
        reader.Queries[^1].FromAuditSequence.Should().Be(field is "fromSequence" ? 1 : null);
        reader.Queries[^1].UntilAuditSequence.Should().Be(field is "untilSequence" ? long.MaxValue : null);
        viewer.Close();
        viewer.ParentSpanFilter.Should().BeEmpty();
        viewer.Outcome.Should().Be(EvidenceViewModel.OutcomeChoices[0]);
        viewer.ContentAvailability.Should().Be(EvidenceContentAvailability.Any);
        viewer.EventNameFilter.Should().BeEmpty();
        viewer.ExceptionTypeFilter.Should().BeEmpty();
        viewer.FromAuditSequenceFilter.Should().BeEmpty();
        viewer.UntilAuditSequenceFilter.Should().BeEmpty();
    }
}
