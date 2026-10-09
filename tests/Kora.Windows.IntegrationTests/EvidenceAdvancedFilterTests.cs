using System.Globalization;

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
public sealed class EvidenceAdvancedFilterTests
{
    private static readonly DateTimeOffset Edge = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public async Task NativeFieldsArriveTypedAndNextUsesTheExactImmutableQuery(string culture)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var reader = new CapturingReader();
            var viewer = Viewer(reader);
            var request = HostRequest.Create(RequestOrigin.LocalUi);
            var invocation = Guid.NewGuid();
            var approval = Guid.NewGuid();
            var correlation = Guid.NewGuid();
            viewer.SessionFilter = request.SessionId.Value.ToString("D");
            viewer.TaskFilter = request.TaskId.Value.ToString("D");
            viewer.RequestFilter = request.RequestId.Value.ToString("D");
            viewer.InvocationFilter = invocation.ToString("D").ToUpperInvariant();
            viewer.ApprovalFilter = approval.ToString("D");
            viewer.CorrelationFilter = correlation.ToString("D");
            viewer.FromFilter = "2026-10-10T11:00:00+11:00";
            viewer.UntilFilter = "2026-10-10T00:00:00.0000000Z";
            viewer.Severity = EvidenceSeverity.Warning;
            viewer.AuditOutcome = SecurityAuditOutcome.Denied;
            await viewer.SearchAsync();
            var query = reader.Queries.Single();
            query.SessionId.Should().Be(request.SessionId);
            query.TaskId.Should().Be(request.TaskId);
            query.RequestId.Should().Be(request.RequestId);
            query.InvocationId.Should().Be(new HostId<InvocationIdentity>(invocation));
            query.ApprovalId.Should().Be(approval);
            query.CorrelationId.Should().Be(correlation);
            query.FromUtc.Should().Be(Edge);
            query.UntilUtc.Should().Be(Edge);
            query.FromUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
            query.Severity.Should().Be(EvidenceSeverity.Warning);
            query.AuditOutcome.Should().Be(SecurityAuditOutcome.Denied);
            viewer.CanNext.Should().BeTrue();
            viewer.Select(viewer.Records[0]);
            await viewer.NextAsync();
            reader.Queries[1].Should().BeSameAs(query);
            reader.Checkpoints[1].Should().NotBeNull();
            viewer.CanReadTrace.Should().BeFalse();
            viewer.ClearAdvancedFilters();
            viewer.Records.Should().BeEmpty();
            viewer.CanNext.Should().BeFalse();
            viewer.SessionFilter.Should().Be(request.SessionId.Value.ToString("D"));
            await viewer.SearchAsync();
            var blank = reader.Queries[^1];
            blank.RequestId.Should().BeNull();
            blank.InvocationId.Should().BeNull();
            blank.ApprovalId.Should().BeNull();
            blank.CorrelationId.Should().BeNull();
            blank.FromUtc.Should().BeNull();
            blank.UntilUtc.Should().BeNull();
            blank.Severity.Should().BeNull();
            blank.AuditOutcome.Should().BeNull();
            reader.Checkpoints[^1].Should().BeNull();
            viewer.Close();
            viewer.SessionFilter.Should().BeEmpty();
            viewer.RequestFilter.Should().BeEmpty();
            viewer.InvocationFilter.Should().BeEmpty();
            viewer.ApprovalFilter.Should().BeEmpty();
            viewer.CorrelationFilter.Should().BeEmpty();
            viewer.FromFilter.Should().BeEmpty();
            viewer.UntilFilter.Should().BeEmpty();
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(nameof(EvidenceViewModel.RequestFilter))]
    [InlineData(nameof(EvidenceViewModel.InvocationFilter))]
    [InlineData(nameof(EvidenceViewModel.ApprovalFilter))]
    [InlineData(nameof(EvidenceViewModel.CorrelationFilter))]
    [InlineData(nameof(EvidenceViewModel.FromFilter))]
    [InlineData(nameof(EvidenceViewModel.UntilFilter))]
    [InlineData(nameof(EvidenceViewModel.Severity))]
    [InlineData(nameof(EvidenceViewModel.AuditOutcome))]
    [InlineData(nameof(EvidenceViewModel.Source))]
    [InlineData(nameof(EvidenceViewModel.SafeText))]
    [InlineData(nameof(EvidenceViewModel.SessionFilter))]
    [InlineData(nameof(EvidenceViewModel.TaskFilter))]
    [InlineData(nameof(EvidenceViewModel.TraceFilter))]
    public async Task EveryChangedFilterRetiresCursorResultsAndSelection(string field)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        await viewer.SearchAsync();
        viewer.Select(viewer.Records[0]);
        viewer.CanReadTrace.Should().BeTrue();
        Set(viewer, field, Guid.NewGuid().ToString("D"));
        viewer.Records.Should().BeEmpty();
        viewer.Segments.Should().BeEmpty();
        viewer.ResultText.Should().BeEmpty();
        viewer.CanNext.Should().BeFalse();
        viewer.CanReadTrace.Should().BeFalse();
        await viewer.NextAsync();
        reader.Queries.Should().ContainSingle();
        viewer.Status.Should().Contain("failed");
        viewer.Close();
    }

    [Theory]
    [InlineData("bad")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("{01234567-89ab-cdef-0123-456789abcdef}")]
    [InlineData(" 01234567-89ab-cdef-0123-456789abcdef ")]
    public async Task MalformedOrNoncanonicalIdentifiersNeverFallBackToAnUnfilteredRead(string value)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        foreach (var field in new[] { nameof(EvidenceViewModel.RequestFilter), nameof(EvidenceViewModel.InvocationFilter),
            nameof(EvidenceViewModel.ApprovalFilter), nameof(EvidenceViewModel.CorrelationFilter),
            nameof(EvidenceViewModel.SessionFilter), nameof(EvidenceViewModel.TaskFilter) })
        {
            var reader = new CapturingReader();
            var viewer = Viewer(reader);
            await viewer.SearchAsync();
            Set(viewer, field, value);
            await viewer.SearchAsync();
            reader.Queries.Should().ContainSingle();
            viewer.Status.Should().Contain("failed");
            viewer.ResultText.Should().BeEmpty();
            viewer.Close();
        }
    }

    [Theory]
    [InlineData("2026-10-10")]
    [InlineData("2026-10-10T00:00:00")]
    [InlineData("10/10/2026 00:00:00Z")]
    [InlineData("2026-02-30T00:00:00Z")]
    [InlineData("2026-10-10T00:00:00+15:00")]
    [InlineData("2026-10-10T00:00:00.12345678Z")]
    [InlineData("2026-10-10T00:00:00.Z")]
    [InlineData(" 2026-10-10T00:00:00Z ")]
    public async Task AmbiguousOrInvalidTimesNeverReadTheStore(string value)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        foreach (var field in new[] { nameof(EvidenceViewModel.FromFilter), nameof(EvidenceViewModel.UntilFilter) })
        {
            var reader = new CapturingReader();
            var viewer = Viewer(reader);
            Set(viewer, field, value);
            await viewer.SearchAsync();
            reader.Queries.Should().BeEmpty();
            viewer.Status.Should().Contain("FormatException");
            viewer.Close();
        }
    }

    [Fact]
    public async Task InvalidRangeAndUndefinedTypedChoicesNeverReadAndWhitespaceMeansUnset()
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.FromFilter = "2026-10-10T00:00:01Z";
        viewer.UntilFilter = "2026-10-10T00:00:00Z";
        await viewer.SearchAsync();
        reader.Queries.Should().BeEmpty();
        viewer.Status.Should().Contain("ArgumentException");
        viewer.ClearAdvancedFilters();
        viewer.Severity = (EvidenceSeverity)99;
        await viewer.SearchAsync();
        reader.Queries.Should().BeEmpty();
        viewer.ClearAdvancedFilters();
        viewer.AuditOutcome = (SecurityAuditOutcome)99;
        await viewer.SearchAsync();
        reader.Queries.Should().BeEmpty();
        viewer.ClearAdvancedFilters();
        foreach (var field in new[] { nameof(EvidenceViewModel.RequestFilter), nameof(EvidenceViewModel.InvocationFilter),
            nameof(EvidenceViewModel.ApprovalFilter), nameof(EvidenceViewModel.CorrelationFilter),
            nameof(EvidenceViewModel.FromFilter), nameof(EvidenceViewModel.UntilFilter) })
        {
            Set(viewer, field, " \t ");
        }
        await viewer.SearchAsync();
        reader.Queries.Should().ContainSingle().Which.Should().Be(new EvidenceQuery());
        viewer.Close();
    }

    [Fact]
    public async Task ChangingFiltersDuringAReadCancelsAndCannotPublishLateContent()
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var viewer = Viewer(reader);
        var pending = viewer.SearchAsync();
        viewer.RequestFilter = Guid.NewGuid().ToString("D");
        reader.Token.IsCancellationRequested.Should().BeTrue();
        reader.Held.SetResult(Batch());
        await pending;
        viewer.Status.Should().Contain("Filters changed");
        viewer.Records.Should().BeEmpty();
        viewer.ResultText.Should().BeEmpty();
        var released = () => reader.Token.WaitHandle;
        released.Should().Throw<ObjectDisposedException>();
        reader.Held = null;
        await viewer.SearchAsync();
        reader.Checkpoints[^1].Should().BeNull();
        viewer.Close();
    }

    [Fact]
    public async Task FilterNotificationAlreadyHasRetiredThePreviousPageAndSameValueDoesNotRestartIt()
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        await viewer.SearchAsync();
        viewer.SafeText = string.Empty;
        viewer.CanNext.Should().BeTrue();
        var notified = false;
        viewer.PropertyChanged += (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(EvidenceViewModel.RequestFilter), StringComparison.Ordinal)) { return; }
            viewer.Records.Should().BeEmpty();
            viewer.CanNext.Should().BeFalse();
            notified = true;
        };
        viewer.RequestFilter = Guid.NewGuid().ToString("D");
        notified.Should().BeTrue();
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidenceSource.AuthorityAudit, true)]
    [InlineData(EvidenceSource.Span, true)]
    [InlineData(EvidenceSource.Link, true)]
    [InlineData(EvidenceSource.Log, false)]
    [InlineData(EvidenceSource.DailyLog, false)]
    [InlineData(EvidenceSource.CombinedLog, false)]
    public async Task UnsupportedSourceFiltersExplainRecoveryWithoutReading(EvidenceSource source, bool severity)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.Source = source;
        if (severity) { viewer.Severity = EvidenceSeverity.Information; }
        else { viewer.AuditOutcome = SecurityAuditOutcome.Succeeded; }
        await viewer.SearchAsync();
        reader.Queries.Should().BeEmpty();
        viewer.Status.Should().StartWith("Unsupported filter").And.Contain("search again");
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidenceSource.All, 3)]
    [InlineData(EvidenceSource.DailyLog, 3)]
    [InlineData(EvidenceSource.CombinedLog, 6)]
    public async Task NativeExactIdentifiersSeverityAndInclusiveTimesChangeRealReaderResults(EvidenceSource source, int count)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        var owner = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            RequestOrigin.LocalUi, new(Guid.NewGuid()));
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
            "host.query.version", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser, "application.current", Guid.NewGuid());
        DiagnosticEnvelope matching;
        using (var producer = HostActivity.BeginAudit(owner, audit))
        {
            matching = WindowsDailyEvidenceReaderTests.Produce(owner, 1)[0] with
            {
                Trace = TraceSnapshot.Capture(producer.Activity), AuditCorrelationId = audit.CorrelationId,
                ApprovalId = audit.ApprovalId, Level = "Warning", ObservedUtc = Edge,
            };
            sink.WriteDiagnostic(matching);
        }
        var before = WindowsDailyEvidenceReaderTests.Produce(HostRequest.Create(RequestOrigin.LocalUi), 1)[0]
            with { ObservedUtc = Edge.AddTicks(-1) };
        var after = WindowsDailyEvidenceReaderTests.Produce(HostRequest.Create(RequestOrigin.LocalUi), 1)[0]
            with { ObservedUtc = Edge.AddTicks(1) };
        foreach (var envelope in new[] { before, after })
        {
            clock.Now = envelope.ObservedUtc;
            using var producer = HostActivity.BeginRoot(envelope.Host!, HostActivityLayer.Application, HostOperation.Request);
            sink.WriteDiagnostic(envelope with { Trace = TraceSnapshot.Capture(producer.Activity) });
        }
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261010.log", [before, matching, after]);
        var viewer = Viewer(new WindowsEvidenceReader(new(sink), new(fixture)));
        viewer.Source = source;
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(count);
        foreach (var field in new[] { nameof(EvidenceViewModel.RequestFilter), nameof(EvidenceViewModel.InvocationFilter),
            nameof(EvidenceViewModel.ApprovalFilter), nameof(EvidenceViewModel.CorrelationFilter),
            nameof(EvidenceViewModel.FromFilter), nameof(EvidenceViewModel.UntilFilter), nameof(EvidenceViewModel.Severity) })
        {
            viewer.ClearAdvancedFilters();
            Set(viewer, field, field switch
            {
                nameof(EvidenceViewModel.RequestFilter) => owner.RequestId.Value.ToString("D"),
                nameof(EvidenceViewModel.InvocationFilter) => owner.InvocationId!.Value.Value.ToString("D"),
                nameof(EvidenceViewModel.ApprovalFilter) => audit.ApprovalId!.Value.ToString("D"),
                nameof(EvidenceViewModel.CorrelationFilter) => audit.CorrelationId.ToString("D"),
                _ => "2026-10-10T00:00:00Z",
            });
            await viewer.SearchAsync();
            viewer.Records.Should().HaveCount(field is nameof(EvidenceViewModel.FromFilter) or nameof(EvidenceViewModel.UntilFilter)
                ? count * 2 / 3 : count / 3);
        }
        viewer.RequestFilter = owner.RequestId.Value.ToString("D");
        viewer.InvocationFilter = owner.InvocationId!.Value.Value.ToString("D");
        viewer.ApprovalFilter = audit.ApprovalId!.Value.ToString("D");
        viewer.CorrelationFilter = audit.CorrelationId.ToString("D");
        viewer.FromFilter = "2026-10-10T11:00:00+11:00";
        viewer.UntilFilter = "2026-10-09T19:00:00-05:00";
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(count / 3);
        viewer.Records.Should().OnlyContain(record => record.Host == owner && record.Audit == null && record.AuthorityProvenance == null);
        if (source == EvidenceSource.CombinedLog)
        {
            viewer.Records.Select(record => record.Reference.Source).Should().Equal(EvidenceSource.Log, EvidenceSource.DailyLog);
            viewer.Records[0].CommittedUtc.Should().Be(Edge);
            viewer.Records[1].CommittedUtc.Should().BeNull();
            viewer.Records[1].DailyProvenance.Should().NotBeNull();
        }
        viewer.FromFilter = "2026-10-10T00:00:00.0000001Z";
        await viewer.SearchAsync();
        viewer.Status.Should().Contain("failed");
        viewer.ClearAdvancedFilters();
        viewer.FromFilter = "2026-10-10T00:00:00.0000001Z";
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(count / 3).And.OnlyContain(record => record.Host != owner);
        viewer.ClearAdvancedFilters();
        viewer.UntilFilter = "2026-10-09T23:59:59.9999999Z";
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(count / 3).And.OnlyContain(record => record.Host != owner);
        viewer.ClearAdvancedFilters();
        viewer.RequestFilter = Guid.NewGuid().ToString("D");
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Status.Should().StartWith("Available");
        viewer.Close();
    }

    [WindowsFact]
    public async Task NativeAuditOutcomeAndCorrelationFilterOnlyRealTypedSqliteAudits()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var events = new[] { SecurityAuditOutcome.Succeeded, SecurityAuditOutcome.Denied }.Select(outcome =>
            new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution, "host.query.version",
                outcome, SecurityAuditInitiator.LocalUser, "application.current", Guid.NewGuid())).ToArray();
        foreach (var audit in events)
        {
            var request = HostRequest.Create(RequestOrigin.LocalUi);
            using var producer = HostActivity.BeginAudit(request, audit);
            var diagnostic = WindowsDailyEvidenceReaderTests.Produce(request, 1)[0] with
            {
                Trace = TraceSnapshot.Capture(producer.Activity), AuditCorrelationId = audit.CorrelationId,
                ApprovalId = audit.ApprovalId,
            };
            sink.WriteAudit(new(diagnostic, audit));
            sink.WriteDiagnostic(diagnostic);
        }
        var viewer = Viewer(new WindowsSqliteEvidenceReader(sink));
        await viewer.SearchAsync();
        viewer.Records.Should().HaveCount(4);
        viewer.AuditOutcome = SecurityAuditOutcome.Denied;
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle().Which.Audit.Should().BeEquivalentTo(events[1]);
        viewer.CorrelationFilter = events[1].CorrelationId.ToString("D");
        viewer.ApprovalFilter = events[1].ApprovalId!.Value.ToString("D");
        viewer.FromFilter = Edge.ToString("O", CultureInfo.InvariantCulture);
        viewer.UntilFilter = Edge.ToString("O", CultureInfo.InvariantCulture);
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle().Which.Reference.Source.Should().Be(EvidenceSource.Audit);
        viewer.AuditOutcome = SecurityAuditOutcome.Succeeded;
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Status.Should().StartWith("Available");
        viewer.Close();
    }

    private static void Set(EvidenceViewModel viewer, string field, string value)
    {
        switch (field)
        {
            case nameof(EvidenceViewModel.RequestFilter): viewer.RequestFilter = value; break;
            case nameof(EvidenceViewModel.InvocationFilter): viewer.InvocationFilter = value; break;
            case nameof(EvidenceViewModel.ApprovalFilter): viewer.ApprovalFilter = value; break;
            case nameof(EvidenceViewModel.CorrelationFilter): viewer.CorrelationFilter = value; break;
            case nameof(EvidenceViewModel.FromFilter): viewer.FromFilter = value; break;
            case nameof(EvidenceViewModel.UntilFilter): viewer.UntilFilter = value; break;
            case nameof(EvidenceViewModel.Severity): viewer.Severity = EvidenceSeverity.Warning; break;
            case nameof(EvidenceViewModel.AuditOutcome): viewer.AuditOutcome = SecurityAuditOutcome.Succeeded; break;
            case nameof(EvidenceViewModel.Source): viewer.Source = EvidenceSource.Log; break;
            case nameof(EvidenceViewModel.SafeText): viewer.SafeText = value; break;
            case nameof(EvidenceViewModel.SessionFilter): viewer.SessionFilter = value; break;
            case nameof(EvidenceViewModel.TaskFilter): viewer.TaskFilter = value; break;
            case nameof(EvidenceViewModel.TraceFilter): viewer.TraceFilter = value; break;
            default: throw new ArgumentException("Unknown test field.", nameof(field));
        }
    }

    private static EvidenceViewModel Viewer(IEvidenceReader reader) => new(
        new(reader, new Access(), TimeProvider.System, NullLogger<DurableEvidenceQuery>.Instance),
        () => true, NullLogger<EvidenceViewModel>.Instance);

    private static EvidenceReadBatch Batch()
    {
        var record = new EvidenceRecord(new(EvidenceSource.Log, new(Guid.NewGuid())), Edge, Edge.AddDays(30),
            EvidenceSegmentStatus.Present, null,
            new(new string('a', 32), new string('b', 16), null, System.Diagnostics.ActivityTraceFlags.Recorded,
                "Kora.Application", "1.0.0", "session.request", System.Diagnostics.ActivityKind.Internal),
            null, null, "Information", "test", 1, "content",
            new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), null, null, []);
        return new(new(1, 0, 0, 0), [new(new(1, EvidenceSource.Log, record.Reference.Id.Value.ToString("D"), -1), record)],
            null, true, false);
    }

    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect => true; }
    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = Edge;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class CapturingReader : IEvidenceReader
    {
        internal List<EvidenceQuery> Queries { get; } = [];
        internal List<EvidenceReadCheckpoint?> Checkpoints { get; } = [];
        internal TaskCompletionSource<EvidenceReadBatch>? Held { get; set; }
        internal CancellationToken Token { get; private set; }
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            Checkpoints.Add(checkpoint);
            Token = cancellationToken;
            return Held is null ? ValueTask.FromResult(Batch()) : new(Held.Task);
        }
    }
}
