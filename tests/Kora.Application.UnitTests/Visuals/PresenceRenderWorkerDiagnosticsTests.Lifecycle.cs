using System.Threading.Channels;

using AwesomeAssertions;

using Kora.Application.Visuals;
using Kora.Core;

namespace Kora.Application.UnitTests.Visuals;

public sealed partial class PresenceRenderWorkerDiagnosticsTests
{
    [Fact]
    public async Task SuspensionDuringDrawDiscardsPixelsAndResumeCreatesExactlyOneFreshGeneration()
    {
        using var draw = new PresenceRenderWorkerTests.WorkerGate();
        using var target = new PresenceRenderWorkerTests.FakeTarget { BeforeDrawReturns = draw.BlockOnce };
        var queue = Channel.CreateUnbounded<Action>();
        var logger = new DiagnosticLogger();
        using var worker = Create(target, queue, logger);
        try
        {
            worker.Update(PresenceRenderWorkerTests.Inputs());
            await draw.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            (await Next(logger.Records)).Properties["Admitted"].CanonicalValue.Should().Be("true");

            worker.Suspend();
            worker.HasVisibleFrame.Should().BeFalse();
        }
        finally
        {
            draw.Release();
        }

        var suspended = await Next(logger.Records);
        suspended.Properties["Admitted"].CanonicalValue.Should().Be("false");
        suspended.Properties["PositiveOpacityPresented"].CanonicalValue.Should().Be("false");
        target.Draws.Should().Be(1);
        queue.Reader.TryRead(out _).Should().BeFalse("suspended in-flight pixels must be discarded before dispatch");

        worker.Update(PresenceRenderWorkerTests.Inputs());
        var fresh = await Next(queue);
        target.Draws.Should().Be(2);
        worker.Dispose();
        fresh();

        var records = new List<DiagnosticRecord>();
        while (logger.Records.Reader.TryRead(out var record)) { records.Add(record); }
        var resumed = records.Single(record => record.EventId == 1);
        resumed.Properties["Admitted"].CanonicalValue.Should().Be("true");
        resumed.Properties["Generation"].CanonicalValue.Should().Be("3");
        var completed = records.Single(record => record.EventId == 3);
        completed.Properties["DrawnFrames"].CanonicalValue.Should().Be("2");
        completed.Properties["PresentedFrames"].CanonicalValue.Should().Be("0");
        target.Presents.Should().Be(0);
        target.Releases.Should().Be(1);
    }

    [Fact]
    public async Task SettledHiddenFrameWaitsWithoutDrawingAndAQueuedInputChangeWakesIt()
    {
        using var dispatch = new PresenceRenderWorkerTests.WorkerGate();
        using var settled = new PresenceRenderWorkerTests.WorkerGate();
        using var target = new PresenceRenderWorkerTests.FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        var inspectSettledFrame = 0;
        var logger = new DiagnosticLogger
        {
            BeforeInformationCheck = () =>
            {
                if (Volatile.Read(ref inspectSettledFrame) != 0)
                {
                    settled.BlockOnce();
                }
            },
        };
        using var worker = new PresenceRenderWorker(target,
            action =>
            {
                queue.Writer.TryWrite(action);
                dispatch.BlockOnce();
            },
            exception => throw new InvalidOperationException("Unexpected renderer failure.", exception), logger);
        var inputs = PresenceRenderWorkerTests.Inputs() with { State = AssistantState.Hidden };
        try
        {
            worker.Update(inputs);
            var first = await Next(queue);
            await dispatch.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);
            first();
            Volatile.Write(ref inspectSettledFrame, 1);
            dispatch.Release();
            await settled.Entered.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken);

            // The worker has captured unchanged hidden inputs; this wake must survive its settled-frame wait.
            worker.Update(inputs with { IsLightTheme = true });
            target.Draws.Should().Be(1);
            queue.Reader.TryRead(out _).Should().BeFalse();
            settled.Release();

            var changed = await Next(queue);
            target.Draws.Should().Be(2);
            target.LastRed.Should().Be(101);
            target.LastGreen.Should().Be(119);
            target.LastBlue.Should().Be(130);
            worker.Dispose();
            changed();
        }
        finally
        {
            dispatch.Release();
            settled.Release();
        }

        var records = new List<DiagnosticRecord>();
        while (logger.Records.Reader.TryRead(out var record)) { records.Add(record); }
        records.Count(record => record.EventId == 1).Should().Be(1,
            "theme changes do not create a new admission or a positive-opacity transition");
        var completed = records.Single(record => record.EventId == 3);
        completed.Properties["DrawnFrames"].CanonicalValue.Should().Be("2");
        completed.Properties["PresentedFrames"].CanonicalValue.Should().Be("1");
        worker.PresentedOpacity.Should().Be(0);
        target.Presents.Should().Be(1);
        target.Releases.Should().Be(1);
    }

    [Fact]
    public async Task CompletionDiagnosticFailureIsReportedWithoutReleasingDrawingResourcesTwice()
    {
        using var target = new PresenceRenderWorkerTests.FakeTarget();
        var queue = Channel.CreateUnbounded<Action>();
        var logger = new DiagnosticLogger { CompletionFailure = new IOException("Synthetic completion sink failure.") };
        var reported = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = new PresenceRenderWorker(target, action => queue.Writer.TryWrite(action),
            exception => reported.TrySetResult(exception), logger);
        worker.Update(PresenceRenderWorkerTests.Inputs());
        var stale = await Next(queue);

        worker.Dispose();
        stale();
        (await Next(queue))();

        (await reported.Task.WaitAsync(Deadline, TestContext.Current.CancellationToken))
            .Should().BeSameAs(logger.CompletionFailure);
        var records = new List<DiagnosticRecord>();
        while (logger.Records.Reader.TryRead(out var record)) { records.Add(record); }
        records.Should().NotContain(record => record.EventId == 3);
        records.Single(record => record.EventId == 0).Exception.Should().BeSameAs(logger.CompletionFailure);
        target.Releases.Should().Be(1);
        target.ReleaseThread.Should().Be(target.DrawingThread);
        target.Presents.Should().Be(0);
        worker.HasVisibleFrame.Should().BeFalse();
        queue.Reader.TryRead(out _).Should().BeFalse();
    }
}
