using System.Diagnostics;
using System.Xml.Linq;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Windows.IntegrationTests.Audio;
using Kora.Windows.IntegrationTests.Storage;
using Kora.Windows.Storage;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Windows.IntegrationTests;

[Collection("Host tracing")]
public sealed class EvidenceViewModelTests
{
    [Theory]
    [InlineData(EvidenceSource.Log)]
    [InlineData(EvidenceSource.Audit)]
    [InlineData(EvidenceSource.Span)]
    [InlineData(EvidenceSource.Link)]
    [InlineData(EvidenceSource.DailyLog)]
    [InlineData(EvidenceSource.CombinedLog)]
    [InlineData(EvidenceSource.AuthorityAudit)]
    [InlineData(EvidenceSource.All)]
    public async Task Missing_source_requires_explicit_fresh_search_and_truthful_terminal_outcomes(EvidenceSource source)
    {
        var completed = new List<Activity>();
        using var listener = Listen(completed);
        var reader = new RecoverableReader();
        var viewer = new EvidenceViewModel(new(reader, new Access(), TimeProvider.System,
            NullLogger<DurableEvidenceQuery>.Instance), () => true, NullLogger<EvidenceViewModel>.Instance)
        {
            Source = source,
            SafeText = "private-query-never-tagged",
        };
        await viewer.SearchAsync();
        viewer.Status.Should().StartWith("Unavailable: 0 records.")
            .And.Contain("could not be read").And.Contain("no complete or empty-success")
            .And.Contain("Verify access").And.Contain("fresh search").And.NotContain("End of this filtered snapshot");
        viewer.ResultText.Should().Contain("\"Status\":\"Unavailable\"").And.Contain("\"UnavailableSources\":[");
        viewer.Records.Should().BeEmpty();
        viewer.CanFind.Should().BeFalse();
        viewer.CanNext.Should().BeFalse();
        viewer.CanQuery.Should().BeTrue();
        AssertTerminal(completed, HostOperationOutcome.Failed);
        reader.Queries.Should().ContainSingle();
        reader.Checkpoints.Should().Equal([null]);
        var unavailableText = viewer.ResultText;
        reader.Available = true;
        viewer.ResultText.Should().Be(unavailableText);
        reader.Queries.Should().ContainSingle();

        completed.Clear();
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle().Which.Should().BeSameAs(reader.Record);
        viewer.ResultText.Should().Contain(reader.Record.Text!);
        viewer.Status.Should().StartWith("Available: 1 records. End of this filtered snapshot.")
            .And.NotContain("could not be read");
        viewer.CanFind.Should().BeTrue();
        reader.Queries.Should().HaveCount(2);
        reader.Queries[1].Should().NotBeSameAs(reader.Queries[0]);
        reader.Checkpoints.Should().Equal([null, null]);
        AssertTerminal(completed, HostOperationOutcome.Completed);
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidenceSource.Log)]
    [InlineData(EvidenceSource.Audit)]
    [InlineData(EvidenceSource.Span)]
    [InlineData(EvidenceSource.Link)]
    [InlineData(EvidenceSource.DailyLog)]
    [InlineData(EvidenceSource.CombinedLog)]
    [InlineData(EvidenceSource.AuthorityAudit)]
    [InlineData(EvidenceSource.All)]
    public async Task Available_empty_source_is_a_completed_zero_record_snapshot(EvidenceSource source)
    {
        var completed = new List<Activity>();
        using var listener = Listen(completed);
        var reader = new RecoverableReader { Available = true, Empty = true };
        var viewer = new EvidenceViewModel(new(reader, new Access(), TimeProvider.System,
            NullLogger<DurableEvidenceQuery>.Instance), () => true, NullLogger<EvidenceViewModel>.Instance) { Source = source };
        await viewer.SearchAsync();
        viewer.Status.Should().StartWith("Available: 0 records. End of this filtered snapshot.")
            .And.NotContain("could not be read");
        viewer.Records.Should().BeEmpty();
        viewer.ResultText.Should().Contain("\"Status\":\"Available\"");
        viewer.CanFind.Should().BeTrue();
        viewer.CanNext.Should().BeFalse();
        if (source == EvidenceSource.All)
        {
            viewer.ResultText.Should().Contain("\"UnavailableSources\":[\"Session\",\"Conversation\"]");
        }
        AssertTerminal(completed, HostOperationOutcome.Completed);
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidencePageStatus.Unavailable, false, false)]
    [InlineData(EvidencePageStatus.Unavailable, true, false)]
    [InlineData(EvidencePageStatus.Available, true, false)]
    [InlineData(EvidencePageStatus.Partial, true, false)]
    [InlineData(EvidencePageStatus.ScanLimitReached, true, true)]
    public async Task Typed_page_and_included_source_failures_never_claim_success_even_with_records(
        EvidencePageStatus status, bool includedFailure, bool scanLimit)
    {
        var completed = new List<Activity>();
        using var listener = Listen(completed);
        var reader = new RecoverableReader
        {
            Available = true, Status = status, IncludedFailure = includedFailure, ScanLimit = scanLimit,
        };
        var viewer = new EvidenceViewModel(new(reader, new Access(), TimeProvider.System,
            NullLogger<DurableEvidenceQuery>.Instance), () => true, NullLogger<EvidenceViewModel>.Instance);
        await viewer.SearchAsync();
        viewer.Status.Should().StartWith(status + ": 1 records.").And.Contain("could not be read")
            .And.Contain("fresh search").And.NotContain("End of this filtered snapshot");
        viewer.Records.Should().ContainSingle();
        viewer.CanFind.Should().BeFalse();
        viewer.Status.Should().EndWith(EvidencePage.StorageDisclosure);
        var desktop = completed.Single(activity => string.Equals(activity.Source.Name, "Kora.Desktop", StringComparison.Ordinal));
        desktop.GetTagItem("kora.outcome").Should().Be(nameof(HostOperationOutcome.Failed));
        desktop.Status.Should().Be(ActivityStatusCode.Error);
        viewer.Close();
    }

    [Theory]
    [InlineData(EvidenceSource.CombinedLog)]
    [InlineData(EvidenceSource.AuthorityAudit)]
    public async Task Bounded_scan_disclosure_and_continuation_remain_available_without_source_failure(EvidenceSource source)
    {
        var completed = new List<Activity>();
        using var listener = Listen(completed);
        var reader = new RecoverableReader { Available = true, ScanLimit = true };
        var viewer = new EvidenceViewModel(new(reader, new Access(), TimeProvider.System,
            NullLogger<DurableEvidenceQuery>.Instance), () => true, NullLogger<EvidenceViewModel>.Instance) { Source = source };
        await viewer.SearchAsync();
        viewer.Status.Should().StartWith("ScanLimitReached: 1 records. More bounded pages available.")
            .And.EndWith(source == EvidenceSource.AuthorityAudit ? EvidencePage.AuthorityDisclosure : EvidencePage.StorageDisclosure);
        viewer.CanNext.Should().BeTrue();
        viewer.CanFind.Should().BeFalse();
        AssertTerminal(completed, HostOperationOutcome.Completed);
        viewer.Close();
    }

    [WindowsFact]
    public async Task Native_view_model_reads_current_user_store_pages_filters_and_segments_without_model_or_window()
    {
        using var fixture = new OwnedStorageFixture();
        using var listener = Listen();
        var sink = new WindowsSqliteEvidenceSink(fixture);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var trace = new TraceSnapshot(new string('a', 32), new string('b', 16), null,
            ActivityTraceFlags.Recorded, "Kora.Application", "1.0.0", "session.request", ActivityKind.Internal);
        sink.WriteActivity(new(new(Guid.NewGuid()), trace, request, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow, HostOperationOutcome.Completed, [new(new string('c', 32), new string('d', 16))]));
        var service = new DurableEvidenceQuery(new WindowsSqliteEvidenceReader(sink), new Access(),
            TimeProvider.System, NullLogger<DurableEvidenceQuery>.Instance);
        var viewer = new EvidenceViewModel(service, () => true, NullLogger<EvidenceViewModel>.Instance)
        {
            Source = EvidenceSource.Span, SessionFilter = request.SessionId.Value.ToString("D"),
            TaskFilter = request.TaskId.Value.ToString("D"), TraceFilter = trace.TraceId, SpanFilter = trace.SpanId, SafeText = "session.request",
        };
        await viewer.SearchAsync();
        viewer.Records.Should().ContainSingle();
        viewer.ResultText.Should().Contain("kora-evidence:span:")
            .And.Contain("\"Source\":\"Span\"").And.Contain("\"Retention\":\"Present\"");
        viewer.Select(viewer.Records[0]);
        viewer.CanReadTrace.Should().BeTrue();
        viewer.Segments.Should().ContainSingle();
        await viewer.ReadTraceAsync();
        viewer.Records.Should().HaveCount(2);
        viewer.Select(viewer.Records.First(record => record.Reference.Source == EvidenceSource.Span));
        await viewer.NavigateAsync(viewer.Segments[0]);
        viewer.Status.Should().Contain("failed");
        viewer.ResultText.Should().BeEmpty();
        viewer.Source = EvidenceSource.Conversation;
        await viewer.SearchAsync();
        viewer.Status.Should().StartWith("Unavailable");
        viewer.SessionFilter = "not-a-guid";
        await viewer.SearchAsync();
        viewer.Status.Should().Contain("FormatException");
        viewer.Close();
        viewer.Close();
        viewer.CanQuery.Should().BeFalse();
        viewer.Records.Should().BeEmpty();
        viewer.ResultText.Should().BeEmpty();
        viewer.SafeText.Should().BeEmpty();
    }

    [Fact]
    public async Task Late_content_cancellation_gate_revoke_and_parallel_queries_do_not_restore_closed_presentation()
    {
        using var listener = Listen();
        var reader = new HeldReader();
        var access = new Access();
        var service = new DurableEvidenceQuery(reader, access, TimeProvider.System, NullLogger<DurableEvidenceQuery>.Instance);
        var viewer = new EvidenceViewModel(service, () => access.CanInspect, NullLogger<EvidenceViewModel>.Instance);
        viewer.EventIdFilter = "0";
        viewer.SpanFilter = "0123456789abcdef";
        viewer.CategoryFilter = "Exact.Category";
        viewer.ActionIdFilter = "Exact Action";
        var pending = viewer.SearchAsync();
        await viewer.SearchAsync();
        reader.Calls.Should().Be(1);
        viewer.Close();
        viewer.EventIdFilter.Should().BeEmpty();
        viewer.SpanFilter.Should().BeEmpty();
        viewer.CategoryFilter.Should().BeEmpty();
        viewer.ActionIdFilter.Should().BeEmpty();
        reader.Token.WaitHandle.SafeWaitHandle.IsClosed.Should().BeFalse();
        reader.Completion.SetResult(new(new(0, 0, 0, 0), [], null, false, false));
        await pending;
        var retiredToken = () => reader.Token.WaitHandle;
        retiredToken.Should().Throw<ObjectDisposedException>();
        viewer.ResultText.Should().BeEmpty();
        viewer.Status.Should().Contain("cancelled");
        var denied = new EvidenceViewModel(service, () => false, NullLogger<EvidenceViewModel>.Instance);
        await denied.SearchAsync();
        denied.Status.Should().Contain("failed");
        denied.Close();
        var incomplete = new EvidenceViewModel(service, () => true, NullLogger<EvidenceViewModel>.Instance);
        await incomplete.NextAsync();
        incomplete.Status.Should().Contain("failed");
        var invalid = () => incomplete.Select(new(new(EvidenceSource.Log, new(Guid.NewGuid())),
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, EvidenceSegmentStatus.Present, null, null, null, null,
            null, null, null, null, new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), null, null, []));
        invalid.Should().Throw<InvalidOperationException>();
        incomplete.Close();
    }

    [Fact]
    public void Native_evidence_contract_uses_accessible_inert_themed_controls_and_no_clipboard_or_model_surface()
    {
        var source = Read("EvidenceWindow.axaml");
        var document = XDocument.Parse(source);
        source.Should().Contain("DynamicResource").And.Contain("AutomationProperties.Name")
            .And.Contain("No copy, export, model, browser, deletion or execution.")
            .And.NotContain("WebView").And.NotContain("SelectableTextBlock").And.NotContain("MaxLength");
        document.Descendants().Where(element => string.Equals(element.Name.LocalName, "TextBox", StringComparison.Ordinal)).Should().HaveCount(25);
        source.Should().Contain("Advanced filters (optional)").And.Contain("Both time edges are inclusive");
        foreach (var binding in new[] { nameof(EvidenceViewModel.RequestFilter), nameof(EvidenceViewModel.InvocationFilter),
            nameof(EvidenceViewModel.ApprovalFilter), nameof(EvidenceViewModel.CorrelationFilter),
            nameof(EvidenceViewModel.FromFilter), nameof(EvidenceViewModel.UntilFilter),
            nameof(EvidenceViewModel.FromDueFilter), nameof(EvidenceViewModel.UntilDueFilter),
            nameof(EvidenceViewModel.EventIdFilter), nameof(EvidenceViewModel.CategoryFilter), nameof(EvidenceViewModel.ActionIdFilter),
            nameof(EvidenceViewModel.Severity), nameof(EvidenceViewModel.AuditOutcome), nameof(EvidenceViewModel.SpanFilter),
            nameof(EvidenceViewModel.ParentSpanFilter), nameof(EvidenceViewModel.EventNameFilter),
            nameof(EvidenceViewModel.ExceptionTypeFilter), nameof(EvidenceViewModel.FromAuditSequenceFilter),
            nameof(EvidenceViewModel.UntilAuditSequenceFilter), nameof(EvidenceViewModel.TraceSourceFilter),
            nameof(EvidenceViewModel.TraceSourceVersionFilter), nameof(EvidenceViewModel.ActivityNameFilter) })
        {
            source.Should().Contain("{Binding " + binding + "}");
        }
        source.Should().Contain("{Binding Outcome, Mode=TwoWay}")
            .And.Contain("{Binding ContentAvailability, Mode=TwoWay}")
            .And.Contain("{Binding TraceVersionAvailability, Mode=TwoWay}")
            .And.Contain("{Binding ActivityKind, Mode=TwoWay}")
            .And.Contain("{Binding TraceFlags, Mode=TwoWay}");
        source.Should().Contain("{Binding ParentKind, Mode=TwoWay}");
        foreach (var field in document.Descendants().Where(element => string.Equals(element.Name.LocalName, "TextBox", StringComparison.Ordinal)))
        {
            field.Attribute("ContextMenu")!.Value.Should().Be("{x:Null}");
            field.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
        }
        Read("EvidenceWindow.axaml.cs").Should().Contain("RoutingStrategies.Tunnel")
            .And.Contain("CopyingToClipboard").And.Contain("CuttingToClipboard").And.NotContain("Clipboard.");
        Read("SystemTrayController.cs").Should().Contain("Evidence & diagnostics");
        Read("Program.cs").Should().Contain("new WindowsSegmentedEvidenceReader(provider.GetRequiredService<WindowsSegmentedEvidenceSink>())");
        Read("App.axaml.cs").Should().Contain("evidenceWindow?.Dispose()");
        Read("DesktopEvidenceAccess.cs").Should().Contain("IsReady").And.Contain("IsHandoffRecoveryRequired")
            .And.Contain("CanRevealPrivatePresentation");
        Read("EvidenceWindowController.cs").Should().Contain("PrivacyClosureRequested +=")
            .And.Contain("window?.Close()").And.NotContain("ShowDialog");
    }

    private static string Read(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".github", "copilot-instructions.md")))
        {
            directory = directory.Parent;
        }
        return File.ReadAllText(Path.Combine(directory!.FullName, "src", "Kora", name));
    }

    private static void AssertTerminal(List<Activity> completed, HostOperationOutcome outcome)
    {
        completed.Should().HaveCount(2);
        var desktop = completed.Single(activity => string.Equals(activity.Source.Name, "Kora.Desktop", StringComparison.Ordinal));
        var query = completed.Single(activity => string.Equals(activity.Source.Name, "Kora.Application", StringComparison.Ordinal));
        query.ParentSpanId.Should().Be(desktop.SpanId);
        completed.Should().OnlyContain(activity => string.Equals(activity.OperationName, "evidence.query", StringComparison.Ordinal)
            && Equals(activity.GetTagItem("kora.outcome"), outcome.ToString())
            && activity.Status == (outcome == HostOperationOutcome.Completed ? ActivityStatusCode.Ok : ActivityStatusCode.Error));
        completed.SelectMany(activity => activity.Baggage).Should().BeEmpty();
        completed.SelectMany(activity => activity.Tags).Should().NotContain(tag => tag.Value != null
            && (tag.Value.Contains("private-query", StringComparison.Ordinal)
                || tag.Value.Contains("private-evidence", StringComparison.Ordinal)
                || tag.Value.Contains("private-path", StringComparison.Ordinal)));
    }

    private static ActivityListener Listen(List<Activity>? completed = null)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => completed?.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class Access : IEvidenceQueryAccess { public bool CanInspect { get; set; } = true; }
    private sealed class RecoverableReader : IEvidenceReader
    {
        internal bool Available { get; set; }
        internal bool Empty { get; init; }
        internal bool IncludedFailure { get; init; }
        internal bool ScanLimit { get; init; }
        internal EvidencePageStatus? Status { get; init; }
        internal List<EvidenceQuery> Queries { get; } = [];
        internal List<EvidenceReadCheckpoint?> Checkpoints { get; } = [];
        internal EvidenceRecord Record { get; private set; } = null!;

        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            Checkpoints.Add(checkpoint);
            if (!Available) { throw new FileNotFoundException("private-path-never-tagged"); }
            var source = query.Source is EvidenceSource.All or EvidenceSource.CombinedLog ? EvidenceSource.Log : query.Source;
            Record = new(new(source, new(Guid.NewGuid())), now, null, EvidenceSegmentStatus.Present,
                request, null, null, null, null, null, null, "private-evidence-never-tagged",
                new Dictionary<string, EvidenceValue>(StringComparer.Ordinal), null, null, []);
            return new(new EvidenceReadBatch(new(1, 1, 1, 1),
                Empty ? [] : [new(new(1, source, Record.Reference.Id.Value.ToString("D"), 0), Record)],
                null, ScanLimit, ScanLimit, Status, UnavailableSources: IncludedFailure ? [source] : null));
        }
    }

    private sealed class HeldReader : IEvidenceReader
    {
        internal TaskCompletionSource<EvidenceReadBatch> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls { get; private set; }
        internal CancellationToken Token { get; private set; }
        public ValueTask<EvidenceReadBatch> ReadAsync(EvidenceQuery query, EvidenceReadCheckpoint? checkpoint,
            HostRequest request, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Calls++;
            Token = cancellationToken;
            return new(Completion.Task);
        }
    }
}
