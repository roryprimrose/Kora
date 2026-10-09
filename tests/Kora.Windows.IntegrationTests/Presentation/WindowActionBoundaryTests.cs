using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.IntegrationTests.Presentation;

public sealed class WindowActionBoundaryTests
{
    [Fact]
    public async Task Uncorrelated_window_action_has_live_host_context_through_await_and_terminates()
    {
        var sink = new Sink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var logger = provider.CreateLogger("window-action-fixture");
        HostRequest? request = null;
        Activity? activity = null;

        var outcome = await MainWindow.RunWindowActionAsync(async () =>
        {
            request = HostActivity.RequireCurrent().Request;
            activity = HostActivity.RequireCurrent().Activity;
            request.Origin.Should().Be(RequestOrigin.HostSystem);
            Write(logger);
            await Task.Yield();
            HostActivity.RequireCurrent().Request.Should().BeSameAs(request);
            Write(logger);
        }, _ => throw new InvalidOperationException("Unexpected failure."));

        outcome.Should().Be(HostOperationOutcome.Completed);
        activity!.Status.Should().Be(ActivityStatusCode.Ok);
        sink.Diagnostics.Should().HaveCount(2).And.OnlyContain(row => row.Host == request && row.Trace != null);
        sink.Gaps.Should().BeEmpty();
        HostActivity.Current.Should().BeNull();
    }

    [Fact]
    public async Task Window_action_preserves_voice_identity_and_parentage_without_leaking_into_its_caller()
    {
        var sink = new Sink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
        using var parent = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Activity? child = null;
        var route = MainWindow.RunWindowActionAsync(async () =>
        {
            child = HostActivity.RequireCurrent().Activity;
            HostActivity.RequireCurrent().Request.Should().BeSameAs(request);
            child!.ParentSpanId.Should().Be(parent.Activity!.SpanId);
            await gate.Task;
            HostActivity.RequireCurrent().Request.Should().BeSameAs(request);
            Write(provider.CreateLogger("window-action-fixture"));
        }, _ => throw new InvalidOperationException("Unexpected failure."));

        HostActivity.RequireCurrent().Should().BeSameAs(parent);
        gate.SetResult();
        (await route).Should().Be(HostOperationOutcome.Completed);
        HostActivity.RequireCurrent().Should().BeSameAs(parent);
        child!.Status.Should().Be(ActivityStatusCode.Ok);
        sink.Diagnostics.Should().ContainSingle().Which.Host.Should().BeSameAs(request);
        sink.Gaps.Should().BeEmpty();
        parent.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task Cancelled_window_transition_reports_cancelled_without_failure_notification()
    {
        var sink = new Sink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        Activity? activity = null;
        var failures = new List<Exception>();

        var outcome = await MainWindow.RunWindowActionAsync(async () =>
        {
            activity = HostActivity.RequireCurrent().Activity;
            await Task.Yield();
            throw new OperationCanceledException("Synthetic superseded transition.");
        }, failures.Add);

        outcome.Should().Be(HostOperationOutcome.Cancelled);
        activity!.GetTagItem("kora.outcome").Should().Be(nameof(HostOperationOutcome.Cancelled));
        failures.Should().BeEmpty();
        sink.Gaps.Should().BeEmpty();
        HostActivity.Current.Should().BeNull();
    }

    [Fact]
    public async Task Failed_window_action_reports_failure_with_the_original_live_context()
    {
        var sink = new Sink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var request = HostRequest.Create(RequestOrigin.ActivatedVoice);
        using var parent = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var expected = new IOException("Synthetic window-action failure.");
        var failures = new List<Exception>();
        Activity? activity = null;

        var outcome = await MainWindow.RunWindowActionAsync(async () =>
        {
            activity = HostActivity.RequireCurrent().Activity;
            await Task.Yield();
            throw expected;
        }, exception =>
        {
            HostActivity.RequireCurrent().Request.Should().BeSameAs(request);
            failures.Add(exception);
            Write(provider.CreateLogger("window-action-fixture"));
        });

        outcome.Should().Be(HostOperationOutcome.Failed);
        failures.Should().ContainSingle().Which.Should().BeSameAs(expected);
        activity!.Status.Should().Be(ActivityStatusCode.Error);
        sink.Diagnostics.Should().ContainSingle().Which.Host.Should().BeSameAs(request);
        sink.Gaps.Should().BeEmpty();
        HostActivity.RequireCurrent().Should().BeSameAs(parent);
        parent.Complete(HostOperationOutcome.Completed);
    }

    private static void Write(ILogger logger)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.Log(LogLevel.Information, new EventId(1),
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["{OriginalFormat}"] = "Window-action boundary fixture." },
                exception: null, static (_, _) => "Window-action boundary fixture.");
        }
    }

    private sealed class Sink : IEvidenceSink, IEvidenceGapReporter
    {
        public string Name => "window-action-fixture";
        public List<DiagnosticEnvelope> Diagnostics { get; } = [];
        public List<EvidenceGap> Gaps { get; } = [];
        public void WriteDiagnostic(DiagnosticEnvelope envelope) => Diagnostics.Add(envelope);
        public void WriteActivity(CompletedActivityEnvelope envelope) { }
        public void WriteAudit(AuditEnvelope envelope) => throw new InvalidOperationException("No audit is expected.");
        public void Report(EvidenceGap gap) => Gaps.Add(gap);
    }
}
