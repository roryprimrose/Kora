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
public sealed partial class EvidenceAdvancedFilterTests
{
    private static readonly DateTimeOffset Edge = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("0123456789abcdef")]
    [InlineData("0000000000000001")]
    public async Task NativeSpanUsesEmptyOnlyAnyAndPreservesExactCanonicalInput(string value)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.SpanFilter = value;
        await viewer.SearchAsync();
        reader.Queries.Should().ContainSingle().Which.SpanId.Should().Be(value.Length == 0 ? null : value);
        viewer.Select(viewer.Records[0]);
        viewer.SpanFilter = value;
        viewer.CanNext.Should().BeTrue();
        viewer.CanReadTrace.Should().BeTrue();
        reader.Queries.Should().ContainSingle();
        viewer.SpanFilter = string.Empty;
        await viewer.SearchAsync();
        reader.Queries[^1].SpanId.Should().BeNull();
        reader.Checkpoints[^1].Should().BeNull();
        viewer.Close();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0123456789abcde")]
    [InlineData("0123456789abcdef0")]
    [InlineData("0123456789ABCDEF")]
    [InlineData("0123456789abcdeg")]
    [InlineData("0000000000000000")]
    [InlineData(" 0123456789abcdef")]
    [InlineData("0123456789abcdef ")]
    [InlineData("01234567 9abcdef")]
    [InlineData("                ")]
    [InlineData("\t")]
    [InlineData("0123456789abcde\n")]
    [InlineData("0x23456789abcdef")]
    [InlineData("0123456789abcde\u00e9")]
    [InlineData("0123456789abcde\ud800")]
    public async Task MalformedSpanNeverReadsRepairsOrFallsBackToAny(string value)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        await viewer.SearchAsync();
        viewer.Select(viewer.Records[0]);
        viewer.SpanFilter = value;
        viewer.Records.Should().BeEmpty();
        viewer.CanNext.Should().BeFalse();
        viewer.CanReadTrace.Should().BeFalse();
        await viewer.SearchAsync();
        reader.Queries.Should().ContainSingle();
        viewer.SpanFilter.Should().Be(value);
        viewer.ResultText.Should().BeEmpty();
        viewer.Status.Should().Contain("failed").And.Contain("span (16)");
        viewer.SpanFilter = string.Empty;
        viewer.ParentSpanFilter = value;
        await viewer.SearchAsync();
        reader.Queries.Should().ContainSingle();
        viewer.ParentSpanFilter.Should().Be(value);
        viewer.Status.Should().Contain("failed");
        viewer.Close();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateSpanReadFailureCannotRestoreRetiredResultsOrStatus(bool cancelled)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.SpanFilter = "0123456789abcdef";
        await viewer.SearchAsync();
        viewer.Select(viewer.Records[0]);
        reader.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = viewer.NextAsync();
        viewer.SpanFilter = new string('b', 16);
        reader.Token.IsCancellationRequested.Should().BeTrue();
        if (cancelled) { reader.Held.SetException(new OperationCanceledException()); }
        else { reader.Held.SetException(new IOException("Retired source failure.")); }
        await pending;
        viewer.Status.Should().StartWith("Filters changed");
        viewer.Records.Should().BeEmpty();
        viewer.Segments.Should().BeEmpty();
        viewer.ResultText.Should().BeEmpty();
        viewer.CanNext.Should().BeFalse();
        viewer.CanReadTrace.Should().BeFalse();
        reader.Queries.Should().HaveCount(2);
        reader.Held = null;
        await viewer.SearchAsync();
        reader.Queries[^1].SpanId.Should().Be(new string('b', 16));
        reader.Checkpoints[^1].Should().BeNull();
        viewer.Close();
    }

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
            viewer.TraceFilter = new string('a', 32);
            viewer.SpanFilter = "0123456789abcdef";
            viewer.RequestFilter = request.RequestId.Value.ToString("D");
            viewer.InvocationFilter = invocation.ToString("D").ToUpperInvariant();
            viewer.ApprovalFilter = approval.ToString("D");
            viewer.CorrelationFilter = correlation.ToString("D");
            viewer.FromFilter = "2026-10-10T11:00:00+11:00";
            viewer.UntilFilter = "2026-10-10T00:00:00.0000000Z";
            viewer.Severity = EvidenceSeverity.Warning;
            viewer.AuditOutcome = SecurityAuditOutcome.Denied;
            viewer.EventIdFilter = "-2147483648";
            viewer.CategoryFilter = "Exact.Category";
            viewer.ActionIdFilter = "Exact Action / correlation";
            viewer.EventNameFilter = "Event.Name";
            viewer.ExceptionTypeFilter = "System.IOException";
            viewer.FromAuditSequenceFilter = "1";
            viewer.UntilAuditSequenceFilter = "9223372036854775807";
            await viewer.SearchAsync();
            var query = reader.Queries.Single();
            query.SessionId.Should().Be(request.SessionId);
            query.TaskId.Should().Be(request.TaskId);
            query.TraceId.Should().Be(viewer.TraceFilter);
            query.SpanId.Should().Be(viewer.SpanFilter);
            query.RequestId.Should().Be(request.RequestId);
            query.InvocationId.Should().Be(new HostId<InvocationIdentity>(invocation));
            query.ApprovalId.Should().Be(approval);
            query.CorrelationId.Should().Be(correlation);
            query.FromUtc.Should().Be(Edge);
            query.UntilUtc.Should().Be(Edge);
            query.FromUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
            query.Severity.Should().Be(EvidenceSeverity.Warning);
            query.AuditOutcome.Should().Be(SecurityAuditOutcome.Denied);
            query.EventId.Should().Be(int.MinValue);
            query.Category.Should().Be("Exact.Category");
            query.ActionId.Should().Be("Exact Action / correlation");
            query.EventName.Should().Be("Event.Name");
            query.ExceptionType.Should().Be("System.IOException");
            query.FromAuditSequence.Should().Be(1);
            query.UntilAuditSequence.Should().Be(long.MaxValue);
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
            reader.Queries.Should().HaveCount(2);
            viewer.TraceFilter.Should().Be(new string('a', 32));
            viewer.SpanFilter.Should().Be("0123456789abcdef");
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
            blank.EventId.Should().BeNull();
            blank.Category.Should().BeNull();
            blank.ActionId.Should().BeNull();
            blank.TraceId.Should().Be(viewer.TraceFilter);
            blank.SpanId.Should().Be(viewer.SpanFilter);
            reader.Checkpoints[^1].Should().BeNull();
            viewer.Close();
            viewer.SessionFilter.Should().BeEmpty();
            viewer.TraceFilter.Should().BeEmpty();
            viewer.SpanFilter.Should().BeEmpty();
            viewer.RequestFilter.Should().BeEmpty();
            viewer.InvocationFilter.Should().BeEmpty();
            viewer.ApprovalFilter.Should().BeEmpty();
            viewer.CorrelationFilter.Should().BeEmpty();
            viewer.FromFilter.Should().BeEmpty();
            viewer.UntilFilter.Should().BeEmpty();
            viewer.EventIdFilter.Should().BeEmpty();
            viewer.CategoryFilter.Should().BeEmpty();
            viewer.ActionIdFilter.Should().BeEmpty();
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
    [InlineData(nameof(EvidenceViewModel.SpanFilter))]
    [InlineData(nameof(EvidenceViewModel.EventIdFilter))]
    [InlineData(nameof(EvidenceViewModel.CategoryFilter))]
    [InlineData(nameof(EvidenceViewModel.ActionIdFilter))]
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
        reader.Queries.Should().ContainSingle();
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

    [Theory]
    [InlineData(nameof(EvidenceViewModel.RequestFilter))]
    [InlineData(nameof(EvidenceViewModel.EventIdFilter))]
    [InlineData(nameof(EvidenceViewModel.CategoryFilter))]
    [InlineData(nameof(EvidenceViewModel.ActionIdFilter))]
    [InlineData(nameof(EvidenceViewModel.SpanFilter))]
    public async Task ChangingFiltersDuringAReadCancelsAndCannotPublishLateContent(string field)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var viewer = Viewer(reader);
        var pending = viewer.SearchAsync();
        Set(viewer, field, field switch
        {
            nameof(EvidenceViewModel.RequestFilter) => Guid.NewGuid().ToString("D"),
            nameof(EvidenceViewModel.SpanFilter) => "0123456789abcdef",
            _ => "1",
        });
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

    [Theory]
    [InlineData(nameof(EvidenceViewModel.ActionIdFilter))]
    [InlineData(nameof(EvidenceViewModel.SpanFilter))]
    public async Task LateUnsupportedFailureCannotReplaceTheNewFilterRevision(string field)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var viewer = Viewer(reader);
        var pending = viewer.SearchAsync();
        Set(viewer, field, "0123456789abcdef");
        reader.Held.SetException(new NotSupportedException("Retired source failure."));
        await pending;
        viewer.Status.Should().StartWith("Filters changed");
        viewer.ResultText.Should().BeEmpty();
        reader.Queries.Should().ContainSingle();
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
        viewer.SessionFilter = owner.SessionId.Value.ToString("D");
        viewer.TaskFilter = owner.TaskId.Value.ToString("D");
        viewer.TraceFilter = matching.Trace!.TraceId;
        viewer.SpanFilter = matching.Trace.SpanId;
        viewer.SafeText = matching.MessageTemplate;
        viewer.EventIdFilter = matching.EventId.ToString(CultureInfo.InvariantCulture);
        viewer.CategoryFilter = matching.Category;
        viewer.Severity = EvidenceSeverity.Warning;
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
        viewer.SpanFilter = new string('f', 16);
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Status.Should().NotContain("failed");
        viewer.SpanFilter = string.Empty;
        viewer.TraceFilter = string.Empty;
        viewer.SessionFilter = string.Empty;
        viewer.TaskFilter = string.Empty;
        viewer.SafeText = string.Empty;
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
        viewer.EventIdFilter = "0";
        viewer.CategoryFilter = "daily.test";
        viewer.ActionIdFilter = events[1].ActionId;
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle().Which.Audit.Should().BeEquivalentTo(events[1]);
        var match = viewer.Records[0];
        viewer.CorrelationFilter = events[1].CorrelationId.ToString("D");
        viewer.SpanFilter = match.Trace!.SpanId;
        viewer.TraceFilter = match.Trace.TraceId;
        viewer.SessionFilter = match.Host!.SessionId.Value.ToString("D");
        viewer.TaskFilter = match.Host.TaskId.Value.ToString("D");
        viewer.RequestFilter = match.Host.RequestId.Value.ToString("D");
        viewer.SafeText = match.Text!;
        viewer.Severity = EvidenceSeverity.Information;
        viewer.ApprovalFilter = events[1].ApprovalId!.Value.ToString("D");
        viewer.FromFilter = Edge.ToString("O", CultureInfo.InvariantCulture);
        viewer.UntilFilter = Edge.ToString("O", CultureInfo.InvariantCulture);
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle().Which.Reference.Source.Should().Be(EvidenceSource.Audit);
        viewer.ActionIdFilter = events[1].ActionId.ToUpperInvariant();
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.ActionIdFilter = events[1].ActionId;
        viewer.CategoryFilter = "DAILY.TEST";
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.CategoryFilter = "daily.test";
        viewer.AuditOutcome = SecurityAuditOutcome.Succeeded;
        viewer.TraceFilter = string.Empty;
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Status.Should().StartWith("Available");
        viewer.Close();
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("-1", -1)]
    [InlineData("-2147483648", int.MinValue)]
    [InlineData("2147483647", int.MaxValue)]
    public async Task NativeEventIdsParseLosslesslyAcrossCultures(string text, int expected)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (var culture in new[] { "en-US", "fr-FR", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var reader = new CapturingReader();
                var viewer = Viewer(reader);
                viewer.EventIdFilter = text;
                await viewer.SearchAsync();
                reader.Queries.Should().ContainSingle().Which.EventId.Should().Be(expected);
                viewer.Close();
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData("-0")]
    [InlineData("1.0")]
    [InlineData("1,000")]
    [InlineData("1e2")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("\u0661")]
    [InlineData("1\0")]
    public async Task InvalidEventIdNeverFallsBackToAnUnfilteredRead(string value)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.EventIdFilter = value;
        await viewer.SearchAsync();
        reader.Queries.Should().BeEmpty();
        viewer.Status.Should().Contain("FormatException");
        viewer.ResultText.Should().BeEmpty();
        viewer.Close();
    }

    public static TheoryData<string> InvalidExactText => new()
    {
        " ", "\t", " exact", "exact ", "ex\nact", "ex\0act", "\ud800",
    };

    [Theory]
    [MemberData(nameof(InvalidExactText))]
    public async Task InvalidExactTextNeverReadsTheStore(string value)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        foreach (var field in new[] { nameof(EvidenceViewModel.CategoryFilter), nameof(EvidenceViewModel.ActionIdFilter),
            nameof(EvidenceViewModel.EventNameFilter), nameof(EvidenceViewModel.ExceptionTypeFilter) })
        {
            var reader = new CapturingReader();
            var viewer = Viewer(reader);
            Set(viewer, field, value);
            await viewer.SearchAsync();
            reader.Queries.Should().BeEmpty();
            viewer.Status.Should().Contain("failed");
            viewer.ResultText.Should().BeEmpty();
            viewer.Close();
        }
    }

    [Theory]
    [InlineData(nameof(EvidenceViewModel.CategoryFilter), 256)]
    [InlineData(nameof(EvidenceViewModel.ActionIdFilter), 128)]
    [InlineData(nameof(EvidenceViewModel.EventNameFilter), 128)]
    [InlineData(nameof(EvidenceViewModel.ExceptionTypeFilter), 256)]
    public async Task NativeExactTextPreservesTheAuthoritativeLimitWithoutTruncation(string field, int limit)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        Set(viewer, field, new string('x', limit));
        await viewer.SearchAsync();
        reader.Queries.Should().ContainSingle();
        Set(viewer, field, new string('x', limit + 1));
        await viewer.SearchAsync();
        reader.Queries.Should().ContainSingle();
        viewer.Status.Should().Contain("ArgumentException");
        viewer.ResultText.Should().BeEmpty();
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidenceSource.Log, nameof(EvidenceViewModel.ActionIdFilter))]
    [InlineData(EvidenceSource.DailyLog, nameof(EvidenceViewModel.ActionIdFilter))]
    [InlineData(EvidenceSource.CombinedLog, nameof(EvidenceViewModel.ActionIdFilter))]
    [InlineData(EvidenceSource.Span, nameof(EvidenceViewModel.ActionIdFilter))]
    [InlineData(EvidenceSource.Link, nameof(EvidenceViewModel.ActionIdFilter))]
    [InlineData(EvidenceSource.Span, nameof(EvidenceViewModel.EventIdFilter))]
    [InlineData(EvidenceSource.Link, nameof(EvidenceViewModel.EventIdFilter))]
    [InlineData(EvidenceSource.AuthorityAudit, nameof(EvidenceViewModel.EventIdFilter))]
    [InlineData(EvidenceSource.Session, nameof(EvidenceViewModel.CategoryFilter))]
    [InlineData(EvidenceSource.Conversation, nameof(EvidenceViewModel.CategoryFilter))]
    public async Task NativeUnsupportedExactFieldsRequireExplicitRecoveryWithoutReading(EvidenceSource source, string field)
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.Source = source;
        Set(viewer, field, "1");
        await viewer.SearchAsync();
        reader.Queries.Should().BeEmpty();
        viewer.Status.Should().StartWith("Unsupported filter").And.Contain("search again");
        viewer.ClearAdvancedFilters();
        await viewer.SearchAsync();
        viewer.Status.Should().NotStartWith("Unsupported filter");
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidenceSource.All)]
    [InlineData(EvidenceSource.Log)]
    [InlineData(EvidenceSource.DailyLog)]
    [InlineData(EvidenceSource.CombinedLog)]
    public async Task NativeEventIdAndCategoryMatchRealSourcesExactlyWithIntegerBounds(EvidenceSource source)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var owner = HostRequest.Create(RequestOrigin.LocalUi);
        var envelopes = WindowsDailyEvidenceReaderTests.Produce(owner, 64)
            .Select((envelope, index) => envelope with
            {
                EventId = index < 60 ? 777 : new[] { int.MinValue, -1, 0, int.MaxValue }[index - 60],
                Category = index < 60 ? "Other.Category" : "Exact.Category",
            }).ToArray();
        using (var producer = HostActivity.BeginRoot(owner, HostActivityLayer.Application, HostOperation.Request))
        {
            foreach (var envelope in envelopes) { sink.WriteDiagnostic(envelope with { Trace = TraceSnapshot.Capture(producer.Activity) }); }
        }
        WindowsDailyEvidenceReaderTests.Write(fixture, "kora-20261010.log", envelopes);
        var viewer = Viewer(new WindowsEvidenceReader(new(sink), new(fixture)));
        viewer.Source = source;
        viewer.CategoryFilter = "Exact.Category";
        foreach (var envelope in envelopes.Where(envelope => envelope.Category is "Exact.Category"))
        {
            viewer.EventIdFilter = envelope.EventId.ToString(CultureInfo.InvariantCulture);
            await viewer.SearchAsync();
            viewer.Records.Should().HaveCount(source == EvidenceSource.CombinedLog ? 2 : 1)
                .And.OnlyContain(record => record.EventId == envelope.EventId && record.Category == envelope.Category);
            System.Text.Encoding.UTF8.GetByteCount(viewer.ResultText).Should().BeLessThanOrEqualTo(EvidencePage.MaximumBytes);
        }
        viewer.CategoryFilter = "exact.category";
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Status.Should().StartWith("Available");
        viewer.Close();
    }

    [Fact]
    public async Task NewExactFieldsDoNotLeakIntoTraceOrSegmentNavigationQueries()
    {
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var reader = new CapturingReader();
        var viewer = Viewer(reader);
        viewer.EventIdFilter = "1";
        viewer.SpanFilter = "0123456789abcdef";
        viewer.CategoryFilter = "test";
        viewer.ActionIdFilter = "exact action";
        viewer.EventNameFilter = "Event.Name";
        viewer.ExceptionTypeFilter = "System.IOException";
        viewer.FromAuditSequenceFilter = "1";
        viewer.UntilAuditSequenceFilter = "2";
        await viewer.SearchAsync();
        viewer.Select(viewer.Records[0]);
        await viewer.ReadTraceAsync();
        reader.Queries[^1].Source.Should().Be(EvidenceSource.All);
        reader.Queries[^1].TraceId.Should().NotBeNull();
        reader.Queries[^1].SpanId.Should().BeNull();
        reader.Queries[^1].EventId.Should().BeNull();
        reader.Queries[^1].Category.Should().BeNull();
        reader.Queries[^1].ActionId.Should().BeNull();
        reader.Queries[^1].EventName.Should().BeNull();
        reader.Queries[^1].ExceptionType.Should().BeNull();
        reader.Queries[^1].FromAuditSequence.Should().BeNull();
        reader.Queries[^1].UntilAuditSequence.Should().BeNull();
        var reference = viewer.Records[0].Reference;
        await viewer.NavigateAsync(new(new string('a', 32), new string('b', 16), EvidenceSegmentStatus.Present, reference));
        reader.Queries[^1].Record.Should().Be(reference);
        reader.Queries[^1].SpanId.Should().BeNull();
        reader.Queries[^1].EventId.Should().BeNull();
        reader.Queries[^1].Category.Should().BeNull();
        reader.Queries[^1].ActionId.Should().BeNull();
        reader.Queries[^1].EventName.Should().BeNull();
        reader.Queries[^1].ExceptionType.Should().BeNull();
        reader.Queries[^1].FromAuditSequence.Should().BeNull();
        reader.Queries[^1].UntilAuditSequence.Should().BeNull();
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidenceSource.Span)]
    [InlineData(EvidenceSource.Link)]
    public async Task NativeActivityCategoryMatchesTheSourceRatherThanRenderedName(EvidenceSource source)
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: new Clock());
        var trace = new TraceSnapshot(new string('a', 32), new string('b', 16), null,
            System.Diagnostics.ActivityTraceFlags.Recorded, "Kora.Application", "1.0.0", "different.name",
            System.Diagnostics.ActivityKind.Internal);
        sink.WriteActivity(new(new(Guid.NewGuid()), trace, HostRequest.Create(RequestOrigin.LocalUi), Edge, Edge,
            HostOperationOutcome.Completed, [new(new string('c', 32), new string('d', 16))]));
        var viewer = Viewer(new WindowsSqliteEvidenceReader(sink));
        viewer.Source = source;
        viewer.CategoryFilter = trace.Source;
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle().Which.Category.Should().Be(trace.Source);
        viewer.CategoryFilter = trace.Source.ToLowerInvariant();
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.CategoryFilter = trace.Name;
        await viewer.SearchAsync();
        viewer.Records.Should().BeEmpty();
        viewer.Close();
    }

    [WindowsFact]
    public async Task ExactSpanComposesWithEveryNativeFilterFamilyUsingAnd()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = WindowsDailyEvidenceReaderTests.Listen();
        var clock = new Clock();
        var sink = new WindowsSqliteEvidenceSink(fixture, timeProvider: clock);
        var owner = new HostRequest(new(Guid.NewGuid()), new(Guid.NewGuid()), new(Guid.NewGuid()),
            RequestOrigin.LocalUi, new(Guid.NewGuid()));
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
            "host.query.version", SecurityAuditOutcome.Succeeded, SecurityAuditInitiator.LocalUser, "application.current", Guid.NewGuid());
        DiagnosticEnvelope envelope;
        using var parentActivity = HostActivity.BeginRoot(owner, HostActivityLayer.Application, HostOperation.Request);
        using (var producer = HostActivity.BeginAudit(owner, audit))
        {
            envelope = WindowsDailyEvidenceReaderTests.Produce(owner, 1)[0] with
            {
                Trace = TraceSnapshot.Capture(producer.Activity), AuditCorrelationId = audit.CorrelationId,
                ApprovalId = audit.ApprovalId, EventName = "Event.Name", ExceptionType = "System.IOException",
            };
            sink.WriteAudit(new(envelope, audit));
            sink.WriteDiagnostic(envelope);
        }
        var viewer = Viewer(new WindowsSqliteEvidenceReader(sink));
        Action[] mismatches =
        [
            () => viewer.Source = EvidenceSource.Span,
            () => viewer.SafeText = "absent literal",
            () => viewer.SessionFilter = Guid.NewGuid().ToString("D"),
            () => viewer.TaskFilter = Guid.NewGuid().ToString("D"),
            () => viewer.TraceFilter = new string('f', 32),
            () => viewer.SpanFilter = new string('f', 16),
            () => viewer.ParentSpanFilter = new string('f', 16),
            () => viewer.TraceSourceFilter = "kora.application",
            () => viewer.TraceSourceVersionFilter = "different",
            () => { viewer.TraceSourceVersionFilter = ""; viewer.TraceVersionAvailability = TraceSourceVersionAvailability.Absent; },
            () => viewer.ActivityNameFilter = "Different",
            () => viewer.ActivityKind = EvidenceViewModel.ActivityKindChoices.Single(choice => choice.Value == System.Diagnostics.ActivityKind.Client),
            () => viewer.TraceFlags = EvidenceViewModel.TraceFlagsChoices.Single(choice =>
                choice.Value is not null && choice.Value != envelope.Trace!.Flags),
            () => viewer.Outcome = EvidenceViewModel.OutcomeChoices[1],
            () => viewer.ContentAvailability = EvidenceContentAvailability.Omitted,
            () => viewer.RequestFilter = Guid.NewGuid().ToString("D"),
            () => viewer.InvocationFilter = Guid.NewGuid().ToString("D"),
            () => viewer.ApprovalFilter = Guid.NewGuid().ToString("D"),
            () => viewer.CorrelationFilter = Guid.NewGuid().ToString("D"),
            () => viewer.FromFilter = Edge.AddTicks(1).ToString("O", CultureInfo.InvariantCulture),
            () => viewer.UntilFilter = Edge.AddTicks(-1).ToString("O", CultureInfo.InvariantCulture),
            () => viewer.Severity = EvidenceSeverity.Warning,
            () => viewer.EventIdFilter = "1",
            () => viewer.CategoryFilter = "Other.Category",
            () => viewer.ActionIdFilter = "other.action",
            () => viewer.AuditOutcome = SecurityAuditOutcome.Denied,
            () => viewer.EventNameFilter = "event.name",
            () => viewer.ExceptionTypeFilter = "system.ioexception",
            () => { viewer.UntilAuditSequenceFilter = ""; viewer.FromAuditSequenceFilter = "2"; },
            () => { viewer.FromAuditSequenceFilter = "2"; viewer.UntilAuditSequenceFilter = "2"; },
        ];
        foreach (var mismatch in mismatches)
        {
            viewer.Source = EvidenceSource.Audit;
            viewer.SafeText = envelope.MessageTemplate;
            viewer.SessionFilter = owner.SessionId.Value.ToString("D");
            viewer.TaskFilter = owner.TaskId.Value.ToString("D");
            viewer.TraceFilter = envelope.Trace!.TraceId;
            viewer.SpanFilter = envelope.Trace.SpanId;
            viewer.ParentSpanFilter = envelope.Trace.ParentSpanId!;
            viewer.TraceSourceFilter = envelope.Trace.Source;
            viewer.TraceSourceVersionFilter = envelope.Trace.SourceVersion!;
            viewer.TraceVersionAvailability = TraceSourceVersionAvailability.Exact;
            viewer.ActivityNameFilter = envelope.Trace.Name;
            viewer.ActivityKind = EvidenceViewModel.ActivityKindChoices.Single(choice => choice.Value == envelope.Trace.Kind);
            viewer.TraceFlags = EvidenceViewModel.TraceFlagsChoices.Single(choice => choice.Value == envelope.Trace.Flags);
            viewer.Outcome = EvidenceViewModel.OutcomeChoices[0];
            viewer.ContentAvailability = EvidenceContentAvailability.Present;
            viewer.RequestFilter = owner.RequestId.Value.ToString("D");
            viewer.InvocationFilter = owner.InvocationId!.Value.Value.ToString("D");
            viewer.ApprovalFilter = audit.ApprovalId!.Value.ToString("D");
            viewer.CorrelationFilter = audit.CorrelationId.ToString("D");
            viewer.FromFilter = Edge.AddTicks(-1).ToString("O", CultureInfo.InvariantCulture);
            viewer.UntilFilter = Edge.AddTicks(1).ToString("O", CultureInfo.InvariantCulture);
            viewer.Severity = EvidenceSeverity.Information;
            viewer.EventIdFilter = "0";
            viewer.CategoryFilter = envelope.Category;
            viewer.ActionIdFilter = audit.ActionId;
            viewer.AuditOutcome = audit.Outcome;
            viewer.EventNameFilter = envelope.EventName!;
            viewer.ExceptionTypeFilter = envelope.ExceptionType!;
            viewer.FromAuditSequenceFilter = "1";
            viewer.UntilAuditSequenceFilter = "1";
            await viewer.SearchAsync();
            viewer.Records.Should().ContainSingle().Which.Audit.Should().BeEquivalentTo(audit);
            mismatch();
            viewer.Records.Should().BeEmpty();
            await viewer.SearchAsync();
            viewer.Records.Should().BeEmpty();
            viewer.CanNext.Should().BeFalse();
            viewer.CanReadTrace.Should().BeFalse();
            viewer.Status.Should().NotContain("failed");
            // Span cannot provide the selected diagnostic/audit filters; refusal is not empty success.
            if (viewer.Source == EvidenceSource.Span) { viewer.Status.Should().StartWith("Unsupported filter"); }
            else { viewer.Status.Should().StartWith("MissingOrRemoved"); }
        }
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
            case nameof(EvidenceViewModel.SpanFilter): viewer.SpanFilter = value; break;
            case nameof(EvidenceViewModel.EventIdFilter): viewer.EventIdFilter = value; break;
            case nameof(EvidenceViewModel.CategoryFilter): viewer.CategoryFilter = value; break;
            case nameof(EvidenceViewModel.ActionIdFilter): viewer.ActionIdFilter = value; break;
            case nameof(EvidenceViewModel.EventNameFilter): viewer.EventNameFilter = value; break;
            case nameof(EvidenceViewModel.ExceptionTypeFilter): viewer.ExceptionTypeFilter = value; break;
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
