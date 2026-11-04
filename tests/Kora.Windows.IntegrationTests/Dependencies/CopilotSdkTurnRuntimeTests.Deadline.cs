using AwesomeAssertions;
using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Windows.IntegrationTests.Dependencies;

public sealed partial class CopilotSdkTurnRuntimeTests
{
    [Theory]
    [InlineData(RequestOrigin.LocalUi, 61)]
    [InlineData(RequestOrigin.ActivatedVoice, 61)]
    [InlineData(RequestOrigin.LocalUi, 119)]
    [InlineData(RequestOrigin.ActivatedVoice, 119)]
    public async Task Exact_review_can_complete_after_the_sdk_default_wait_without_losing_the_original_answer(
        RequestOrigin origin, int seconds)
    {
        var clock = new SdkWaitClock();
        var fixture = new Fixture(origin);
        fixture.Client.WaitClock = clock;
        var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var approved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Review = _ => { offered.TrySetResult(); return new(approved.Task); };
        using var root = fixture.Root();
        var running = fixture.Run(Token);
        await offered.Task.WaitAsync(Token);
        fixture.Network.Sends.Should().Be(0);
        clock.Advance(TimeSpan.FromSeconds(seconds));
        approved.SetResult(true);
        var result = await running;
        result.Should().Be(new CopilotTurnReply(ModelTurnOutcome.Succeeded, "genuine-sdk-boundary-answer"));
        fixture.Client.WaitTimeout.Should().Be(Timeout.InfiniteTimeSpan);
        fixture.Network.Sends.Should().Be(1);
        fixture.Provenance.Request.Should().BeSameAs(fixture.Request);
        fixture.Provenance.Request.Origin.Should().Be(origin);
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Denial_or_host_cancellation_after_sixty_seconds_never_sends_or_returns_an_answer(bool cancel)
    {
        var clock = new SdkWaitClock();
        var fixture = new Fixture();
        fixture.Client.WaitClock = clock;
        var offered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var approved = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Review = _ => { offered.TrySetResult(); return new(approved.Task); };
        using var root = fixture.Root();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var running = fixture.Run(cancellation.Token);
        await offered.Task.WaitAsync(Token);
        clock.Advance(TimeSpan.FromSeconds(61));
        if (cancel) { await cancellation.CancelAsync(); }
        approved.SetResult(false);
        if (cancel) { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running); }
        else { (await running).Should().Be(new CopilotTurnReply(ModelTurnOutcome.DeniedEgress)); }
        fixture.Network.Sends.Should().Be(0);
        fixture.Client.Stops.Should().Be(1);
        fixture.Client.Disposals.Should().Be(1);
        fixture.Runtime.IsQuiescent.Should().BeTrue();
    }

    internal sealed class SdkWaitClock : TimeProvider
    {
        private readonly Lock gate = new();
        private readonly List<WaitTimer> timers = [];
        private TimeSpan elapsed;

        public override DateTimeOffset GetUtcNow() { lock (gate) { return DateTimeOffset.UnixEpoch + elapsed; } }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            period.Should().Be(Timeout.InfiniteTimeSpan);
            lock (gate)
            {
                var timer = new WaitTimer(this, callback, state);
                timers.Add(timer);
                timer.Change(dueTime, period);
                return timer;
            }
        }

        internal void Advance(TimeSpan amount)
        {
            WaitTimer[] due;
            lock (gate)
            {
                elapsed += amount;
                due = timers.Where(timer => timer.Due <= elapsed).ToArray();
                foreach (var timer in due) { timer.Due = TimeSpan.MaxValue; }
            }
            foreach (var timer in due) { timer.Fire(); }
        }

        private sealed class WaitTimer(SdkWaitClock owner, TimerCallback callback, object? state) : ITimer
        {
            internal TimeSpan Due { get; set; } = TimeSpan.MaxValue;
            internal void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                period.Should().Be(Timeout.InfiniteTimeSpan);
                lock (owner.gate)
                {
                    Due = dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : owner.elapsed + dueTime;
                    return true;
                }
            }
            public void Dispose() { lock (owner.gate) { Due = TimeSpan.MaxValue; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
