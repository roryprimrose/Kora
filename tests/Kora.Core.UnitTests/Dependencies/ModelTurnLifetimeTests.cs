using AwesomeAssertions;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.UnitTests.Dependencies;

public sealed class ModelTurnLifetimeTests
{
    private static readonly HostRequest Request = HostRequest.Create(RequestOrigin.LocalUi);

    [Fact]
    public void NestedHostWaitsPauseOnceAndExtendFixedExpiriesByTheWholePausedTime()
    {
        var clock = new Clock();
        var lifetime = new ModelTurnLifetime(Request, clock);
        var changes = new List<bool>();
        lifetime.HostWaitChanged += changes.Add;
        var expiresAt = clock.Now.AddSeconds(15);
        var outer = lifetime.BeginHostWait();
        var inner = lifetime.BeginHostWait();
        lifetime.IsWaitingForHost.Should().BeTrue();
        clock.Advance(TimeSpan.FromMinutes(10));
        lifetime.PausedDuration.Should().Be(TimeSpan.FromMinutes(10));
        lifetime.Extend(expiresAt).Should().Be(expiresAt.AddMinutes(10));
        lifetime.Remaining(expiresAt, clock.Now).Should().Be(TimeSpan.FromSeconds(15));
        inner.Dispose();
        inner.Dispose();
        lifetime.IsWaitingForHost.Should().BeTrue("the outer wait still owns the pause");
        outer.Dispose();
        lifetime.IsWaitingForHost.Should().BeFalse();
        changes.Should().Equal(true, false);
        clock.Advance(TimeSpan.FromSeconds(16));
        lifetime.PausedDuration.Should().Be(TimeSpan.FromMinutes(10), "resumed time is active time");
        lifetime.Remaining(expiresAt, clock.Now).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ReversedMonotonicClockNeverShortensOrRewindsPausedTime()
    {
        var clock = new Clock();
        var lifetime = new ModelTurnLifetime(Request, clock);
        using (lifetime.BeginHostWait())
        {
            clock.Timestamp -= TimeSpan.TicksPerSecond;
            lifetime.PausedDuration.Should().Be(TimeSpan.Zero);
        }
        lifetime.PausedDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void CheckInsAndUntilAnsweredExpiriesHaveNoFixedLimitAndExtensionSaturates()
    {
        var clock = new Clock();
        var lifetime = new ModelTurnLifetime(Request, clock);
        using (lifetime.BeginHostWait()) { clock.Advance(TimeSpan.FromDays(1)); }
        lifetime.Extend(DateTimeOffset.MaxValue.AddHours(-1)).Should().Be(DateTimeOffset.MaxValue);
        lifetime.Extend(HostInteractionLifetime.UntilAnswered).Should().Be(DateTimeOffset.MaxValue);
        lifetime.UsesCheckIns.Should().BeFalse();
        lifetime.UseCheckIns();
        lifetime.UsesCheckIns.Should().BeTrue();
        lifetime.Extend(clock.Now).Should().Be(DateTimeOffset.MaxValue);
        lifetime.Remaining(clock.Now, clock.Now).Should().Be(Timeout.InfiniteTimeSpan);
    }

    [Fact]
    public async Task AmbientLifetimeFlowsOnlyWithinItsScopeAndMatchesOnlyItsRequest()
    {
        var lifetime = new ModelTurnLifetime(Request, new Clock());
        ModelTurnLifetime.Current.Should().BeNull();
        using (ModelTurnLifetime.Enter(lifetime))
        {
            await Task.Yield();
            ModelTurnLifetime.Current.Should().BeSameAs(lifetime);
            ModelTurnLifetime.For(Request).Should().BeSameAs(lifetime);
            ModelTurnLifetime.For(HostRequest.Create(RequestOrigin.LocalUi)).Should().BeNull();
        }
        ModelTurnLifetime.Current.Should().BeNull();
        ModelTurnLifetime.For(Request).Should().BeNull();
        FluentActions.Invoking(() => ModelTurnLifetime.Enter(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ModelTurnLifetime(null!, new Clock())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ModelTurnLifetime(Request, null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DeadlinePausesItsTimerDuringHostWaitsAndRearmsForTheRemainingActiveTime()
    {
        var clock = new Clock();
        var lifetime = new ModelTurnLifetime(Request, clock);
        using var deadline = new ModelTurnDeadline(lifetime, TimeSpan.FromSeconds(15), clock);
        var timer = clock.Timers.Should().ContainSingle().Subject;
        timer.Due.Should().Be(TimeSpan.FromSeconds(15));
        clock.Advance(TimeSpan.FromSeconds(5));
        using (lifetime.BeginHostWait())
        {
            timer.Due.Should().Be(Timeout.InfiniteTimeSpan);
            clock.Advance(TimeSpan.FromMinutes(10));
        }
        timer.Due.Should().Be(TimeSpan.FromSeconds(10));
        deadline.IsCancellationRequested.Should().BeFalse();
        timer.Fire();
        deadline.Token.IsCancellationRequested.Should().BeTrue();
        using (lifetime.BeginHostWait()) { timer.Changes.Should().Be(2, "a fired deadline is not rearmed"); }
    }

    [Fact]
    public void DeadlineStartedDuringAWaitIsPausedAndDisposalStopsFollowingTheLifetime()
    {
        var clock = new Clock();
        var lifetime = new ModelTurnLifetime(Request, clock);
        var wait = lifetime.BeginHostWait();
        var deadline = new ModelTurnDeadline(lifetime, TimeSpan.FromSeconds(15), clock);
        var timer = clock.Timers.Should().ContainSingle().Subject;
        timer.Due.Should().Be(Timeout.InfiniteTimeSpan);
        deadline.Dispose();
        deadline.Dispose();
        wait.Dispose();
        timer.Changes.Should().Be(1, "a disposed deadline ignores later wait transitions");
        using var expired = new ModelTurnDeadline(null, TimeSpan.FromSeconds(-1), clock);
        expired.IsCancellationRequested.Should().BeTrue("a negative remaining limit is already expired");
    }

    [Fact]
    public async Task CheckInDeadlineHasNoTimerAndPlainDeadlineBehavesLikeATimedSource()
    {
        var clock = new Clock();
        var lifetime = new ModelTurnLifetime(Request, clock);
        lifetime.UseCheckIns();
        using (var unbounded = new ModelTurnDeadline(lifetime, TimeSpan.FromSeconds(15), clock))
        {
            clock.Timers.Should().BeEmpty();
            await unbounded.CancelAsync();
            unbounded.Token.IsCancellationRequested.Should().BeTrue();
        }
        using var plain = new ModelTurnDeadline(null, TimeSpan.FromSeconds(15), clock);
        clock.Timers.Should().ContainSingle().Which.Fire();
        plain.IsCancellationRequested.Should().BeTrue();
        FluentActions.Invoking(() => new ModelTurnDeadline(null, TimeSpan.Zero, null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DeadlineWithAnUnrepresentableLimitSaturatesWithoutOverflow()
    {
        var clock = new Clock { Now = DateTimeOffset.MaxValue.AddSeconds(-1) };
        var lifetime = new ModelTurnLifetime(Request, clock);
        using var deadline = new ModelTurnDeadline(lifetime, TimeSpan.FromSeconds(10), clock);
        using (lifetime.BeginHostWait()) { }
        clock.Timers.Single().Due.Should().Be(Timeout.InfiniteTimeSpan, "no finite expiry remains to rearm");
    }

    [Theory]
    [InlineData(10, 10)]
    [InlineData(-10, 0)]
    public void InteractionLifetimeRemainingIsInfiniteOnlyForUntilAnswered(int seconds, int expected)
    {
        var now = DateTimeOffset.UnixEpoch;
        HostInteractionLifetime.IsUntilAnswered(HostInteractionLifetime.UntilAnswered).Should().BeTrue();
        HostInteractionLifetime.IsUntilAnswered(now).Should().BeFalse();
        HostInteractionLifetime.Remaining(HostInteractionLifetime.UntilAnswered, now).Should().Be(Timeout.InfiniteTimeSpan);
        HostInteractionLifetime.Remaining(now.AddSeconds(seconds), now).Should().Be(TimeSpan.FromSeconds(expected));
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
        internal long Timestamp { get; set; }
        internal List<ManualTimer> Timers { get; } = [];
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => Timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        internal void Advance(TimeSpan amount)
        {
            Now += amount;
            Timestamp += amount.Ticks;
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state) { Due = dueTime };
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state) : ITimer
    {
        internal TimeSpan Due { get; set; }
        internal int Changes { get; private set; }
        internal void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Due = dueTime;
            Changes++;
            return true;
        }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
