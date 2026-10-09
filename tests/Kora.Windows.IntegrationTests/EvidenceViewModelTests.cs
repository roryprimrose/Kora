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

public sealed class EvidenceViewModelTests
{
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
            TaskFilter = request.TaskId.Value.ToString("D"), TraceFilter = trace.TraceId, SafeText = "session.request",
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
        var pending = viewer.SearchAsync();
        await viewer.SearchAsync();
        reader.Calls.Should().Be(1);
        viewer.Close();
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
        document.Descendants().Where(element => string.Equals(element.Name.LocalName, "TextBox", StringComparison.Ordinal)).Should().HaveCount(10);
        source.Should().Contain("Advanced filters (optional)").And.Contain("Both time edges are inclusive");
        foreach (var binding in new[] { nameof(EvidenceViewModel.RequestFilter), nameof(EvidenceViewModel.InvocationFilter),
            nameof(EvidenceViewModel.ApprovalFilter), nameof(EvidenceViewModel.CorrelationFilter),
            nameof(EvidenceViewModel.FromFilter), nameof(EvidenceViewModel.UntilFilter),
            nameof(EvidenceViewModel.Severity), nameof(EvidenceViewModel.AuditOutcome) })
        {
            source.Should().Contain("{Binding " + binding + "}");
        }
        foreach (var field in document.Descendants().Where(element => string.Equals(element.Name.LocalName, "TextBox", StringComparison.Ordinal)))
        {
            field.Attribute("ContextMenu")!.Value.Should().Be("{x:Null}");
            field.Attribute("AutomationProperties.Name")!.Value.Should().NotBeNullOrWhiteSpace();
        }
        Read("EvidenceWindow.axaml.cs").Should().Contain("RoutingStrategies.Tunnel")
            .And.Contain("CopyingToClipboard").And.Contain("CuttingToClipboard").And.NotContain("Clipboard.");
        Read("SystemTrayController.cs").Should().Contain("Evidence (read-only)");
        Read("Program.cs").Should().Contain("new WindowsSqliteEvidenceReader(evidence)");
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
