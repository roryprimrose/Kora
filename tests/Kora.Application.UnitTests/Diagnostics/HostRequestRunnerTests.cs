using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Diagnostics;

[Collection("Host tracing")]
public sealed class HostRequestRunnerTests
{
    [Fact]
    public async Task Async_ui_boundary_has_live_context_through_await_and_terminates()
    {
        var sink = new Sink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var logger = provider.CreateLogger("fixture");
        HostRequest? admitted = null;

        await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, async () =>
        {
            admitted = HostActivity.RequireCurrent().Request;
            Write(logger);
            await Task.Yield();
            HostActivity.RequireCurrent().Request.Should().Be(admitted);
            Write(logger);
        });

        sink.Diagnostics.Should().HaveCount(2).And.OnlyContain(row => row.Host == admitted && row.Trace != null);
        sink.Gaps.Should().BeEmpty();
        sink.Activities.Should().ContainSingle().Which.Outcome.Should().Be(HostOperationOutcome.Completed);
        HostActivity.Current.Should().BeNull();
    }

    [Fact]
    public void Nested_sync_boundary_preserves_admitted_session_and_reports_failure()
    {
        var sink = new Sink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var host = HostRequest.Create(RequestOrigin.LocalUi);
        using var parent = HostActivity.BeginRoot(host, HostActivityLayer.Desktop, HostOperation.Startup);
        var action = () => HostRequestRunner.Run(RequestOrigin.HostSystem, () =>
        {
            HostActivity.RequireCurrent().Request.Should().Be(host);
            throw new IOException("Synthetic startup failure");
        }, HostActivityLayer.Desktop, HostOperation.Startup);

        action.Should().Throw<IOException>();
        sink.Activities.Should().ContainSingle().Which.Outcome.Should().Be(HostOperationOutcome.Failed);
        HostActivity.RequireCurrent().Should().BeSameAs(parent);
        parent.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public void Deferred_work_uses_a_new_trace_linked_to_its_admitted_cause_not_hostile_ambient_identity()
    {
        var sink = new Sink();
        using var provider = new EvidenceLoggerProvider([sink], sink);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        Func<HostActivity> begin;
        ActivityTraceId originalTrace;
        using (var parent = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request))
        {
            originalTrace = parent.Activity!.TraceId;
            begin = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Presentation);
            parent.Complete(HostOperationOutcome.Completed);
        }
        using var hostile = new Activity("incoming").SetIdFormat(ActivityIdFormat.W3C).Start();
        hostile.SetTag("kora.session.id", Guid.NewGuid());
        using (var deferred = begin())
        {
            deferred.Request.Should().Be(request);
            deferred.Activity!.TraceId.Should().NotBe(originalTrace).And.NotBe(hostile.TraceId);
            deferred.Activity.ParentSpanId.Should().Be(default(ActivitySpanId));
            Write(provider.CreateLogger("fixture"));
            deferred.Complete(HostOperationOutcome.Completed);
        }

        sink.Gaps.Should().BeEmpty();
        sink.Diagnostics.Should().ContainSingle().Which.Host.Should().Be(request);
        sink.Activities.Should().HaveCount(2);
        sink.Activities[1].Links.Should().ContainSingle().Which.TraceId.Should().Be(originalTrace.ToHexString());
        Activity.Current.Should().BeSameAs(hostile);
    }

    private static void Write(ILogger logger)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.Log(LogLevel.Information, new EventId(1),
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["{OriginalFormat}"] = "Boundary fixture." },
                exception: null, static (_, _) => "Boundary fixture.");
        }
    }

    private sealed class Sink : IEvidenceSink, IEvidenceGapReporter
    {
        public string Name => "fixture";
        public List<DiagnosticEnvelope> Diagnostics { get; } = [];
        public List<CompletedActivityEnvelope> Activities { get; } = [];
        public List<EvidenceGap> Gaps { get; } = [];
        public void WriteDiagnostic(DiagnosticEnvelope envelope) => Diagnostics.Add(envelope);
        public void WriteAudit(AuditEnvelope envelope) => throw new InvalidOperationException("No audit is expected.");
        public void WriteActivity(CompletedActivityEnvelope envelope) => Activities.Add(envelope);
        public void Report(EvidenceGap gap) => Gaps.Add(gap);
    }
}
