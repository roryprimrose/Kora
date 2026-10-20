using System.Diagnostics;
using AwesomeAssertions;
using Kora.Application.Interaction;
using Kora.Core.Commands;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora.Application.UnitTests.Interaction;

[Collection("Host tracing")]
public sealed class CountdownWorkflowTests
{
    [Fact]
    public void MonotonicTimeControlsExpiryRegardlessOfUtcJumpsAndLateTicks()
    {
        var time = new CountdownTestClock();
        using var state = new CountdownWorkflow(time, new Dispatcher(), NullLogger<CountdownWorkflow>.Instance);
        state.Execute(new(CountdownOperation.Status), () => true).Should().Contain("No timer");
        state.Execute(new(CountdownOperation.Cancel), () => true).Should().Contain("No timer");
        state.Execute(new(CountdownOperation.Start, 86400), () => true).Should().Contain("86400 seconds");
        state.IsRunning.Should().BeTrue();
        time.UtcNow = DateTimeOffset.MaxValue;
        time.Advance(TimeSpan.FromMilliseconds(1500));
        state.Status.Should().Contain("86399 seconds");
        state.Execute(new(CountdownOperation.Start, 1), () => true).Should().Contain("not replaced");
        time.Timers.Should().ContainSingle();
        time.UtcNow = DateTimeOffset.MinValue;
        time.Advance(TimeSpan.FromSeconds(86399));
        state.Status.Should().Be("Timer complete. Visual completion only.");
        state.IsRunning.Should().BeFalse();
        time.Timers[0].Disposed.Should().BeTrue();
        time.Timers[0].Fire();
        state.Status.Should().Contain("complete");
        state.Execute(new(CountdownOperation.Start, 1), () => true);
        time.Advance(TimeSpan.FromSeconds(1));
        state.Status.Should().Contain("complete");
    }

    [Fact]
    public void CancelRestartDisposeAndQueuedStaleCallbacksAreTerminal()
    {
        var time = new CountdownTestClock();
        var dispatcher = new Dispatcher { Defer = true };
        using var state = new CountdownWorkflow(time, dispatcher, NullLogger<CountdownWorkflow>.Instance);
        state.Execute(new(CountdownOperation.Start, 1), () => true);
        time.Advance(TimeSpan.FromSeconds(1));
        state.Execute(new(CountdownOperation.Cancel), () => true).Should().Contain("complete");
        state.Execute(new(CountdownOperation.Start, 10), () => true);
        time.Timers[0].Fire();
        dispatcher.Flush();
        state.Status.Should().Contain("10 seconds");
        state.Execute(new(CountdownOperation.Cancel), () => true).Should().Contain("cancelled");
        state.Execute(new(CountdownOperation.Cancel), () => true).Should().Contain("cancelled");
        state.Execute(new(CountdownOperation.Start, 10), () => true);
        time.Advance(TimeSpan.FromSeconds(1));
        var changes = 0;
        state.PropertyChanged += (_, _) => changes++;
        state.Dispose();
        var afterDispose = changes;
        state.Dispose();
        time.Timers[^1].Fire();
        dispatcher.Flush();
        changes.Should().Be(afterDispose);
        state.IsRunning.Should().BeFalse();
        time.Timers.Should().OnlyContain(timer => timer.Disposed);
        var disposed = () => state.Execute(new(CountdownOperation.Status), () => true);
        disposed.Should().Throw<ObjectDisposedException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OriginalLifetimeCannotBeReplacedByFreshStatusAdmission(bool tick)
    {
        var time = new CountdownTestClock();
        using var state = new CountdownWorkflow(time, new Dispatcher(), NullLogger<CountdownWorkflow>.Instance);
        var eligible = true;
        state.Execute(new(CountdownOperation.Start, 2), () => eligible);
        eligible = false;
        if (tick) { time.Advance(TimeSpan.FromSeconds(5)); }
        else { state.Execute(new(CountdownOperation.Status), () => true).Should().Contain("cancelled"); }
        state.Status.Should().Contain("cancelled").And.NotContain("complete");
        state.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void InvalidAndUnavailableRequestsDoNotCreateTimers()
    {
        var time = new CountdownTestClock();
        using var state = new CountdownWorkflow(time, new Dispatcher(), NullLogger<CountdownWorkflow>.Instance);
        state.Execute(new(CountdownOperation.Start, 1), () => false).Should().Contain("unavailable");
        foreach (var command in new CountdownCommand[]
        {
            new(CountdownOperation.Invalid), new((CountdownOperation)99), new(CountdownOperation.Start, 0),
            new(CountdownOperation.Start, 86401), new(CountdownOperation.Help),
        })
        {
            state.Execute(command, () => true).Should().Be(CountdownCommand.Syntax);
        }
        state.IsRunning.Should().BeFalse();
        time.Timers.Should().BeEmpty();
        CountdownWorkflow.Scope.Should().Contain("No sound");
    }

    [Fact]
    public void DeferredExpiryLinksToAdmittedRequestWithoutContentOrBaggage()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, "Kora.Application", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var time = new CountdownTestClock();
        using var state = new CountdownWorkflow(time, new Dispatcher(), NullLogger<CountdownWorkflow>.Instance);
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        using (var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request))
        {
            state.Execute(new(CountdownOperation.Start, 1), () => true);
            root.Complete(HostOperationOutcome.Completed);
        }
        var cause = stopped.Single(activity => string.Equals(activity.OperationName, "presentation.update", StringComparison.Ordinal));
        time.Advance(TimeSpan.FromSeconds(1));
        var expiry = stopped.Last();
        expiry.ParentId.Should().BeNull();
        expiry.Links.Should().ContainSingle().Which.Context.Should().Be(cause.Context);
        expiry.GetTagItem("kora.request.id").Should().Be(request.RequestId.Value);
        expiry.Status.Should().Be(ActivityStatusCode.Ok);
        expiry.Baggage.Should().BeEmpty();
        expiry.TagObjects.Should().NotContain(pair => pair.Key.Contains("seconds", StringComparison.Ordinal));
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        private readonly Queue<Action> actions = new();
        internal bool Defer { get; init; }
        public Task InvokeAsync(Func<Task> action) => action();
        public void Post(Action action) { if (Defer) { actions.Enqueue(action); } else { action(); } }
        internal void Flush() { while (actions.TryDequeue(out var action)) { action(); } }
    }
}
