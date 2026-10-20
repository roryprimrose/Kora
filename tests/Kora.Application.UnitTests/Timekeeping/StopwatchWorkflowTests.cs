using System.Diagnostics;

using AwesomeAssertions;

using Kora.Application.Timekeeping;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Timekeeping;

using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Timekeeping;

[Collection("Host tracing")]
public sealed class StopwatchWorkflowTests : IDisposable
{
    private readonly ActivityListener listener = new()
    {
        ShouldListenTo = source => source.Name.StartsWith("Kora.", StringComparison.Ordinal),
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
    };

    public StopwatchWorkflowTests() => ActivitySource.AddActivityListener(listener);
    public void Dispose() => listener.Dispose();

    [Fact]
    public void ExplicitObservationsUseMonotonicOriginWithoutDriftAndFreezeExactly()
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var time = new ManualTime { Timestamp = 100 };
        using var workflow = new StopwatchWorkflow(time, new Kora.Application.UnitTests.Network.EnabledLogger<StopwatchWorkflow>());
        StopwatchResult Run(StopwatchOperation op) => workflow.Execute(op, workflow.Revision, static () => true, CancellationToken.None);
        Run(StopwatchOperation.Stop).Outcome.Should().Be(StopwatchOutcome.NotRunning);
        Run(StopwatchOperation.Start).Snapshot.Should().Be(new StopwatchSnapshot(StopwatchState.Running, TimeSpan.Zero));
        Run(StopwatchOperation.Start).Outcome.Should().Be(StopwatchOutcome.AlreadyRunning);
        Run(StopwatchOperation.Status).Snapshot!.ObservedElapsed.Should().Be(TimeSpan.Zero);
        time.Timestamp += 1234567;
        Run(StopwatchOperation.Status).Snapshot!.ObservedElapsed.Should().Be(TimeSpan.FromTicks(1234567));
        time.WallClock = DateTimeOffset.MinValue;
        time.Timestamp += 2;
        Run(StopwatchOperation.Status).Snapshot!.ObservedElapsed.Should().Be(TimeSpan.FromTicks(1234569));
        time.Timestamp += 1;
        Run(StopwatchOperation.Stop).Snapshot.Should().Be(new StopwatchSnapshot(StopwatchState.Stopped, TimeSpan.FromTicks(1234570)));
        time.Timestamp += 10000000;
        Run(StopwatchOperation.Status).Snapshot!.ObservedElapsed.Should().Be(TimeSpan.FromTicks(1234570));
        Run(StopwatchOperation.Stop).Outcome.Should().Be(StopwatchOutcome.AlreadyStopped);
        Run(StopwatchOperation.Start).Outcome.Should().Be(StopwatchOutcome.ResetRequired);
        Run(StopwatchOperation.Help).Outcome.Should().Be(StopwatchOutcome.Help);
        Run(StopwatchOperation.Invalid).Outcome.Should().Be(StopwatchOutcome.Invalid);
        Run(StopwatchOperation.Reset).Snapshot.Should().Be(new StopwatchSnapshot(StopwatchState.Idle, TimeSpan.Zero));
        Run(StopwatchOperation.Start).Outcome.Should().Be(StopwatchOutcome.Started);
        Run(StopwatchOperation.Reset).Snapshot!.State.Should().Be(StopwatchState.Idle);
        Run(StopwatchOperation.Stop).Outcome.Should().Be(StopwatchOutcome.NotRunning);
        time.WallClockReads.Should().Be(0);
        time.TimerCreations.Should().Be(0);
    }

    [Theory]
    [InlineData("frequency")]
    [InlineData("changed-frequency")]
    [InlineData("regression")]
    [InlineData("overflow")]
    [InlineData("exception")]
    [InlineData("negative-start")]
    [InlineData("elapsed-regression")]
    public void InvalidProviderOrOverflowIsTruthfulFailureAndRetiresResult(string failure)
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var time = new ManualTime();
        var logger = new Kora.Application.UnitTests.Network.EnabledLogger<StopwatchWorkflow>();
        using var workflow = new StopwatchWorkflow(time, logger);
        StopwatchResult Run(StopwatchOperation op) => workflow.Execute(op, workflow.Revision, static () => true, CancellationToken.None);
        if (string.Equals(failure, "frequency", StringComparison.Ordinal)) { time.Frequency = 0; }
        if (string.Equals(failure, "negative-start", StringComparison.Ordinal)) { time.Timestamp = long.MinValue; }
        var start = Run(StopwatchOperation.Start);
        switch (failure)
        {
            case "frequency": start.Outcome.Should().Be(StopwatchOutcome.TimeUnavailable); break;
            case "changed-frequency": time.Frequency++; break;
            case "regression": time.Timestamp--; break;
            case "overflow": time.Frequency = 1; Run(StopwatchOperation.Reset); Run(StopwatchOperation.Start); time.Timestamp = long.MaxValue; break;
            case "exception": time.Failure = new OverflowException(); break;
            case "negative-start": time.Timestamp = long.MaxValue; break;
            case "elapsed-regression":
                time.Timestamp = 1000000;
                Run(StopwatchOperation.Status);
                time.Frequencies.Enqueue(TimeSpan.TicksPerSecond);
                time.Frequencies.Enqueue(long.MaxValue);
                break;
        }
        if (!string.Equals(failure, "frequency", StringComparison.Ordinal))
        { Run(StopwatchOperation.Status).Should().Be(new StopwatchResult(StopwatchOutcome.TimeUnavailable, null)); }
        time.Failure = null;
        Run(StopwatchOperation.Status).Snapshot.Should().Be(new StopwatchSnapshot(StopwatchState.Idle, TimeSpan.Zero));
        logger.Messages.Should().Contain(message => message.Contains("Run and result retired", StringComparison.Ordinal));
    }

    [Fact]
    public void AdmissionCancellationEpochAndDisposalRetireRatherThanHideOldElapsed()
    {
        using var host = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request);
        var time = new ManualTime();
        using var workflow = new StopwatchWorkflow(time, NullLogger<StopwatchWorkflow>.Instance);
        StopwatchResult Run(StopwatchOperation op) => workflow.Execute(op, workflow.Revision, static () => true, CancellationToken.None);
        Run(StopwatchOperation.Start);
        workflow.Execute(StopwatchOperation.Status, workflow.Revision, static () => false, CancellationToken.None).Snapshot.Should().BeNull();
        Run(StopwatchOperation.Start);
        var checks = 0;
        workflow.Execute(StopwatchOperation.Stop, workflow.Revision, () => ++checks == 1, CancellationToken.None).Outcome.Should().Be(StopwatchOutcome.Denied);
        Run(StopwatchOperation.Start);
        var stale = workflow.Revision;
        workflow.Retire();
        workflow.Execute(StopwatchOperation.Start, stale, static () => true, CancellationToken.None).Outcome.Should().Be(StopwatchOutcome.Denied);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action cancelled = () => workflow.Execute(StopwatchOperation.Start, workflow.Revision, static () => true, cancellation.Token);
        cancelled.Should().Throw<OperationCanceledException>();
        using var during = new CancellationTokenSource();
        time.OnRead = during.Cancel;
        Action cancelledDuring = () => workflow.Execute(StopwatchOperation.Start, workflow.Revision, static () => true, during.Token);
        cancelledDuring.Should().Throw<OperationCanceledException>();
        time.OnRead = null;
        Run(StopwatchOperation.Status).Snapshot!.State.Should().Be(StopwatchState.Idle);
        workflow.Dispose();
        Run(StopwatchOperation.Start).Outcome.Should().Be(StopwatchOutcome.Denied);
    }

    private sealed class ManualTime : TimeProvider
    {
        public long Timestamp { get; set; }
        public long Frequency { get; set; } = TimeSpan.TicksPerSecond;
        public Queue<long> Frequencies { get; } = new();
        public Exception? Failure { get; set; }
        public Action? OnRead { get; set; }
        public DateTimeOffset WallClock { get; set; } = DateTimeOffset.MaxValue;
        public int WallClockReads { get; private set; }
        public int TimerCreations { get; private set; }
        public override long TimestampFrequency => Frequencies.TryDequeue(out var value) ? value : Frequency;
        public override long GetTimestamp()
        {
            if (Failure is { } failure) { throw failure; }
            OnRead?.Invoke();
            return Timestamp;
        }
        public override DateTimeOffset GetUtcNow() { WallClockReads++; return WallClock; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        { TimerCreations++; throw new InvalidOperationException("No timers permitted."); }
    }
}
