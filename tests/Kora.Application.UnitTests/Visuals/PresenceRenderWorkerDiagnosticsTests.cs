using System.Diagnostics;
using System.Threading.Channels;

using AwesomeAssertions;

using Kora.Application.Diagnostics;
using Kora.Application.Visuals;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.Visuals;

public sealed class PresenceRenderWorkerDiagnosticsTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task TransitionsDescribeAdmissionAndPositiveOpacityOffCallerWithoutPerFrameLogs()
    {
        using var target = new PresenceRenderWorkerTests.FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        var logger = new DiagnosticLogger();
        using var worker = Create(target, queue, logger);
        var caller = Environment.CurrentManagedThreadId;
        worker.Update(PresenceRenderWorkerTests.Inputs());
        var admission = await Next(logger.Records);
        admission.EventId.Should().Be(1);
        admission.Thread.Should().NotBe(caller);
        admission.Properties["Admitted"].CanonicalValue.Should().Be("true");
        admission.Properties["PositiveOpacityPresented"].CanonicalValue.Should().Be("false");
        admission.Properties.Keys.Should().BeEquivalentTo("Transition", "Admitted", "Generation", "State",
            "RequestedVisible", "ReducedMotion", "Width", "Height", "Scaling", "PositiveOpacityPresented");
        (await Next(queue))();
        (await Next(queue))();
        var visible = await Next(logger.Records);
        visible.EventId.Should().Be(1);
        visible.Properties["PositiveOpacityPresented"].CanonicalValue.Should().Be("true");
        for (var index = 0; index < 8; index++) { (await Next(queue))(); }
        logger.Records.Reader.TryRead(out _).Should().BeFalse();
        worker.Suspend();
        var suspended = await Next(logger.Records);
        suspended.Properties["Admitted"].CanonicalValue.Should().Be("false");
        suspended.Properties["PositiveOpacityPresented"].CanonicalValue.Should().Be("false");
        worker.Dispose();
        var completed = await Next(logger.Records);
        completed.EventId.Should().Be(3);
        completed.Properties["PresentedFrames"].CanonicalValue.Should().Be("10");
        completed.Properties["Limited"].CanonicalValue.Should().Be("false");
        target.Released.Should().BeTrue();
    }

    [Fact]
    public async Task TransitionLimitIsExplicitAndTerminalCountersSurviveWithoutFurtherTransitionLogs()
    {
        using var target = new PresenceRenderWorkerTests.FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        var logger = new DiagnosticLogger();
        using var worker = Create(target, queue, logger);
        for (var index = 0; index < 40; index++)
        {
            var width = 400 + index;
            worker.Update(PresenceRenderWorkerTests.Inputs() with { Width = width, ReducedMotion = true });
            var matched = false;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var callback = await Next(queue);
                matched = target.LastWidth == width;
                callback();
                if (matched) { break; }
            }
            matched.Should().BeTrue("the latest geometry must replace an invalidated queued frame");
        }
        worker.Dispose();
        var records = new List<DiagnosticRecord>();
        while (logger.Records.Reader.TryRead(out var record)) { records.Add(record); }
        records.Count.Should().Be(18);
        records.Count(record => record.EventId == 1).Should().Be(16);
        records.Single(record => record.EventId == 2).Properties["Limit"].CanonicalValue.Should().Be("16");
        records.Single(record => record.EventId == 3).Properties["Limited"].CanonicalValue.Should().Be("true");
        target.Presents.Should().BeGreaterThanOrEqualTo(40);
    }

    [Fact]
    public async Task ChangedAdmissionUsesItsOwnHostRequestAndLinkedTrace()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var target = new PresenceRenderWorkerTests.FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        var logger = new DiagnosticLogger();
        using var first = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Presentation);
        using var worker = Create(target, queue, logger);
        worker.Update(PresenceRenderWorkerTests.Inputs());
        (await Next(logger.Records)).Request.Should().BeSameAs(first.Request);
        var old = await Next(queue);
        using var second = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi),
            HostActivityLayer.Application, HostOperation.Presentation);
        worker.Update(PresenceRenderWorkerTests.Inputs() with { Width = 400 });
        old();
        var changed = await Next(logger.Records);
        changed.Properties["Width"].CanonicalValue.Should().Be("400");
        changed.Request.Should().BeSameAs(second.Request);
        changed.Links.Should().ContainSingle().Which.Context.Should().Be(second.Activity!.Context);
        worker.Dispose();
        var completed = await Next(logger.Records);
        completed.EventId.Should().Be(3);
        completed.Request.Should().BeSameAs(second.Request);
        second.Complete(HostOperationOutcome.Completed);
        first.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task TransitionStorageFailureRetiresRendererAndReportsFailure()
    {
        using var target = new PresenceRenderWorkerTests.FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        var logger = new DiagnosticLogger { TransitionFailure = new IOException("Synthetic diagnostic sink failure.") };
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = new PresenceRenderWorker(target, action => queue.Writer.TryWrite(action),
            exception => reported.TrySetResult(exception), logger);
        worker.Update(PresenceRenderWorkerTests.Inputs());
        (await Next(queue))();
        (await reported.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken))
            .Should().BeSameAs(logger.TransitionFailure);
        var update = () => worker.Update(PresenceRenderWorkerTests.Inputs());
        update.Should().Throw<InvalidOperationException>();
        worker.Dispose();
        target.Presents.Should().Be(0);
        target.Released.Should().BeTrue();
    }

    private static PresenceRenderWorker Create(PresenceRenderWorkerTests.FakeTarget target,
        Channel<Action> queue, DiagnosticLogger logger) =>
        new(target, action => queue.Writer.TryWrite(action),
            exception => throw new InvalidOperationException("Unexpected renderer failure.", exception), logger);

    private static Task<T> Next<T>(Channel<T> channel) =>
        channel.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask()
            .WaitAsync(Deadline, TestContext.Current.CancellationToken);

    private sealed record DiagnosticRecord(int EventId, int Thread,
        IReadOnlyDictionary<string, EvidenceValue> Properties, HostRequest? Request, ActivityLink[] Links);

    private sealed class DiagnosticLogger : ILogger<PresenceRenderWorker>
    {
        public Channel<DiagnosticRecord> Records { get; } = Channel.CreateUnbounded<DiagnosticRecord>();
        public IOException? TransitionFailure { get; init; }
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 1 && TransitionFailure is not null) { throw TransitionFailure; }
            Records.Writer.TryWrite(new DiagnosticRecord(eventId.Id, Environment.CurrentManagedThreadId,
                EvidenceProperties.Capture(state), HostActivity.Current?.Request, Activity.Current?.Links.ToArray() ?? []));
        }
    }
}
