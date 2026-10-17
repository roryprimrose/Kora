using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

using AwesomeAssertions;

using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

namespace Kora.Windows.IntegrationTests;

public sealed partial class EvidenceAdvancedFilterTests
{
    [Theory]
    [InlineData(EvidenceSource.All)]
    [InlineData(EvidenceSource.Log)]
    [InlineData(EvidenceSource.DailyLog)]
    [InlineData(EvidenceSource.CombinedLog)]
    public async Task ObservedParentKindUsesOnlyValidatedRetainedTraceAndComposesWithExactParent(EvidenceSource source)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        var envelopes = new List<DiagnosticEnvelope>();
        string parentId;
        using (var producer = HostActivity.BeginRoot(owner, HostActivityLayer.Application, HostOperation.Request))
        {
            parentId = TraceSnapshot.Capture(producer.Activity)!.SpanId;
            var templates = WindowsDailyEvidenceReaderTests.Produce(owner, 3);
            for (var index = 0; index < templates.Length; index++)
            {
                using var child = index == 2 ? HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Request) : null;
                var envelope = templates[index] with
                {
                    Trace = index == 0 ? null : TraceSnapshot.Capture(child?.Activity ?? producer.Activity),
                    Host = index == 0 ? null : owner,
                    Properties = index == 0 ? new Dictionary<string, EvidenceValue>(StringComparer.Ordinal)
                    {
                        ["kora.bootstrap"] = new(EvidenceValueKind.Boolean, "false"),
                        ["kora.evidence.gap"] = new(EvidenceValueKind.Text, "MissingHostContext"),
                    } : templates[index].Properties,
                };
                envelopes.Add(envelope);
                sink.WriteDiagnostic(envelope);
            }
        }
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261010.log", envelopes);
        var viewer = Viewer(new WindowsEvidenceReader(new(sink), new(fixture)));
        viewer.Source = source;
        foreach (var choice in EvidenceViewModel.TraceParentKindChoices)
        {
            viewer.ParentKind = choice;
            await viewer.SearchAsync();
            var copies = source == EvidenceSource.CombinedLog ? 2 : 1;
            viewer.Records.Should().HaveCount((choice.Value == TraceParentKind.Any ? 3 : 1) * copies);
            viewer.Records.Should().OnlyContain(record => choice.Value == TraceParentKind.Any
                || record.Trace != null && (choice.Value == TraceParentKind.ObservedRoot) == (record.Trace.ParentSpanId == null));
            viewer.ParentSpanFilter = parentId;
            await viewer.SearchAsync();
            viewer.Records.Should().HaveCount(choice.Value == TraceParentKind.ObservedRoot ? 0 : copies);
            viewer.ParentSpanFilter = "";
        }
        viewer.Close();
    }

    [Theory]
    [InlineData(TraceParentKind.ObservedRoot)]
    [InlineData(TraceParentKind.ObservedChild)]
    public async Task ParentKindEditsRetireInFlightContinuationWithoutReadingOrPublishingLateResults(TraceParentKind kind)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        await viewer.SearchAsync();
        viewer.Select(viewer.Records[0]);
        reader.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = viewer.NextAsync();
        viewer.ParentKind = EvidenceViewModel.TraceParentKindChoices.Single(choice => choice.Value == kind);
        reader.Token.IsCancellationRequested.Should().BeTrue();
        reader.Queries.Should().HaveCount(2);
        viewer.Records.Should().BeEmpty();
        viewer.Segments.Should().BeEmpty();
        viewer.ResultText.Should().BeEmpty();
        viewer.CanNext.Should().BeFalse();
        viewer.CanReadTrace.Should().BeFalse();
        reader.Held.SetResult(Batch());
        await pending;
        viewer.Records.Should().BeEmpty();
        viewer.Status.Should().StartWith("Filters changed");
        reader.Held = null;
        await viewer.SearchAsync();
        reader.Queries[^1].TraceParentKind.Should().Be(kind);
        reader.Checkpoints[^1].Should().BeNull();
        await viewer.NextAsync();
        reader.Queries[^1].Should().BeSameAs(reader.Queries[^2]);
        viewer.ClearAdvancedFilters();
        viewer.ParentKind.Value.Should().Be(TraceParentKind.Any);
        viewer.Records.Should().BeEmpty();
        viewer.CanNext.Should().BeFalse();
        viewer.Close();
    }

    [Fact]
    public async Task InvalidNativeParentKindFailsBeforeAnyRead()
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.ParentKind = new((TraceParentKind)99, "Invalid");
        await viewer.SearchAsync();
        reader.Queries.Should().BeEmpty();
        viewer.Status.Should().Contain("failed");
        viewer.Close();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData(" x")]
    [InlineData("x ")]
    [InlineData("x\n")]
    public async Task InvalidNativeTraceTextNeverReadsOrFallsBackToAny(string text)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.TraceSourceFilter = text;
        await viewer.SearchAsync();
        viewer.Status.Should().Contain("failed");
        viewer.TraceSourceFilter = "";
        viewer.ActivityNameFilter = text;
        await viewer.SearchAsync();
        viewer.Status.Should().Contain("failed");
        viewer.ActivityNameFilter = "";
        viewer.TraceVersionAvailability = TraceSourceVersionAvailability.Exact;
        viewer.TraceSourceVersionFilter = text;
        await viewer.SearchAsync();
        viewer.Status.Should().Contain("failed");
        viewer.TraceSourceVersionFilter = "";
        await viewer.SearchAsync();
        viewer.Status.Should().Contain("failed");
        viewer.TraceSourceVersionFilter = "1";
        foreach (var choice in new[] { TraceSourceVersionAvailability.Any, TraceSourceVersionAvailability.Absent, TraceSourceVersionAvailability.Present })
        {
            viewer.TraceVersionAvailability = choice;
            await viewer.SearchAsync();
            viewer.Status.Should().Contain("failed");
        }
        reader.Queries.Should().BeEmpty();
        viewer.ResultText.Should().BeEmpty();
        viewer.Close();
    }

    [Fact]
    public async Task NativeTraceMetadataRejectsMalformedUnicodeAndOverLimitTextWithoutReading()
    {
        // Construct malformed Unicode inside the test; runner argument transport normalizes isolated surrogates.
        foreach (var text in new[] { new string('\ud800', 1), new string('x', 129) })
        {
            await InvalidNativeTraceTextNeverReadsOrFallsBackToAny(text);
        }
    }

    [WindowsFact]
    public async Task SqliteSpansMatchExactVersionAvailabilityNameKindAndFlagsWithoutInventingAuthority()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        var trace = new TraceSnapshot(new string('a', 32), new string('b', 16), null,
            ActivityTraceFlags.None, "Kora.Core", null, "request", ActivityKind.Client);
        foreach (var version in new[] { null, "1", "2" })
        foreach (var kind in Enum.GetValues<ActivityKind>())
        foreach (var flags in Enum.GetValues<ActivityTraceFlags>())
        {
            sink.WriteActivity(new(new(Guid.NewGuid()), trace with { SourceVersion = version, Kind = kind, Flags = flags },
                owner, Edge, Edge, HostOperationOutcome.Completed, []));
        }
        var viewer = Viewer(new WindowsSqliteEvidenceReader(sink));
        viewer.Source = EvidenceSource.Span;
        viewer.TraceSourceFilter = trace.Source;
        viewer.ActivityNameFilter = trace.Name;
        viewer.ActivityKind = EvidenceViewModel.ActivityKindChoices.Single(choice => choice.Value == trace.Kind);
        viewer.TraceFlags = EvidenceViewModel.TraceFlagsChoices.Single(choice => choice.Value == trace.Flags);
        foreach (var availability in Enum.GetValues<TraceSourceVersionAvailability>())
        {
            viewer.TraceVersionAvailability = availability;
            viewer.TraceSourceVersionFilter = availability == TraceSourceVersionAvailability.Exact ? "1" : "";
            await viewer.SearchAsync();
            viewer.Records.Should().HaveCount(availability == TraceSourceVersionAvailability.Any ? 3
                : availability == TraceSourceVersionAvailability.Present ? 2 : 1);
            viewer.Records.Should().OnlyContain(record => record.Trace!.Kind == trace.Kind && record.Trace.Flags == trace.Flags);
            viewer.ResultText.Should().NotContain("Baggage").And.NotContain("Tags").And.NotContain("Credentials");
        }
        viewer.TraceSourceVersionFilter = "01";
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.ClearAdvancedFilters();
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(30);
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidenceSource.All)]
    [InlineData(EvidenceSource.Log)]
    [InlineData(EvidenceSource.DailyLog)]
    [InlineData(EvidenceSource.CombinedLog)]
    public async Task DiagnosticSourcesUseTheSameTraceMatcherAndExactCase(EvidenceSource source)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        DiagnosticEnvelope envelope;
        using (var producer = HostActivity.BeginRoot(owner, HostActivityLayer.Application, HostOperation.Request))
        {
            envelope = WindowsDailyEvidenceReaderTests.Produce(owner, 1)[0] with { Trace = TraceSnapshot.Capture(producer.Activity) };
            sink.WriteDiagnostic(envelope);
        }
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261010.log", [envelope]);
        var viewer = Viewer(new WindowsEvidenceReader(new(sink), new(fixture)));
        viewer.Source = source;
        viewer.TraceSourceFilter = envelope.Trace!.Source;
        viewer.TraceSourceVersionFilter = envelope.Trace.SourceVersion!;
        viewer.TraceVersionAvailability = TraceSourceVersionAvailability.Exact;
        viewer.ActivityNameFilter = envelope.Trace.Name;
        viewer.ActivityKind = EvidenceViewModel.ActivityKindChoices.Single(choice => choice.Value == envelope.Trace.Kind);
        viewer.TraceFlags = EvidenceViewModel.TraceFlagsChoices.Single(choice => choice.Value == envelope.Trace.Flags);
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(source == EvidenceSource.CombinedLog ? 2 : 1);
        viewer.TraceSourceFilter = envelope.Trace.Source.ToLowerInvariant();
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Close();
    }

    [Theory]
    [InlineData("Source", "Other")]
    [InlineData("SourceVersion", "")]
    [InlineData("SourceVersion", " 1")]
    [InlineData("Name", "request ")]
    [InlineData("Kind", "Unknown")]
    [InlineData("Flags", "2")]
    [InlineData("SourceVersion", null)]
    [InlineData("Flags", null)]
    [InlineData("Kind", null)]
    [InlineData("ParentSpanId", null)]
    public async Task DailyInvalidCompleteTraceFailsBeforeNonmatchingMetadataCanHideIt(string field, string? value)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var envelope = WindowsDailyEvidenceReaderTests.Produce(HostRequest.Create(RequestOrigin.LocalUi), 1)[0];
        var payload = JsonNode.Parse(WindowsSqliteEvidenceSink.EncodeDailyDiagnostic(envelope))!;
        if (value is null) { payload["Trace"]!.AsObject().Remove(field); }
        else { payload["Trace"]![field] = value; }
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261010.log", [envelope]);
        var outer = JsonNode.Parse(WindowsDailyEvidenceReaderTests.Line(envelope))!;
        outer["Properties"]!["EvidenceEnvelope"] = payload.ToJsonString();
        await File.WriteAllTextAsync(Path.Combine(fixture.LocalRoot, "Logs", "kora-20261010.log"),
            outer.ToJsonString() + "\n", new UTF8Encoding(false), TestContext.Current.CancellationToken);
        var viewer = Viewer(new WindowsDailyEvidenceReader(fixture));
        viewer.ParentKind = EvidenceViewModel.TraceParentKindChoices[2];
        viewer.Source = EvidenceSource.DailyLog;
        viewer.TraceSourceFilter = "nonmatching";
        await viewer.SearchAsync();
        viewer.Status.Should().StartWith("Corrupt");
        viewer.Records.Should().BeEmpty();
        viewer.CanNext.Should().BeFalse();
        viewer.Close();
    }
}
