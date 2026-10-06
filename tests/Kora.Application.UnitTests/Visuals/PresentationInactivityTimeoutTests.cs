using AwesomeAssertions;

using Kora.Application.Visuals;

namespace Kora.Application.UnitTests.Visuals;

public sealed class PresentationInactivityTimeoutTests
{
    [Fact]
    public void Default_clock_constructor_starts_without_a_deadline()
    {
        var timeout = new PresentationInactivityTimeout();

        timeout.IsScheduled.Should().BeFalse();
        timeout.Remaining.Should().Be(TimeSpan.Zero);
        timeout.TryExpire().Should().BeFalse();
    }

    [Fact]
    public void Constructor_rejects_a_missing_time_provider()
    {
        TimeProvider timeProvider = null!;
        var action = () => new PresentationInactivityTimeout(timeProvider);

        action.Should().Throw<ArgumentNullException>().WithParameterName(nameof(timeProvider));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(60)]
    public void Timeout_expires_exactly_at_the_configured_deadline_and_only_once(int seconds)
    {
        var clock = new FakeTimeProvider();
        var timeout = new PresentationInactivityTimeout(clock);
        timeout.Restart(seconds);
        timeout.IsScheduled.Should().BeTrue();
        timeout.Remaining.Should().Be(TimeSpan.FromSeconds(seconds));

        clock.Advance(TimeSpan.FromSeconds(seconds) - TimeSpan.FromTicks(1));
        timeout.TryExpire().Should().BeFalse();
        timeout.Remaining.Should().Be(TimeSpan.FromTicks(1));
        clock.Advance(TimeSpan.FromTicks(1));

        timeout.TryExpire().Should().BeTrue();
        timeout.IsScheduled.Should().BeFalse();
        timeout.TryExpire().Should().BeFalse();
    }

    [Fact]
    public void New_interaction_restarts_the_full_inactivity_period()
    {
        var clock = new FakeTimeProvider();
        var timeout = new PresentationInactivityTimeout(clock);
        timeout.Restart(10);
        clock.Advance(TimeSpan.FromSeconds(9));
        timeout.Restart(10);
        clock.Advance(TimeSpan.FromSeconds(1));
        timeout.TryExpire().Should().BeFalse();
        timeout.Remaining.Should().Be(TimeSpan.FromSeconds(9));
        clock.Advance(TimeSpan.FromSeconds(9));
        timeout.TryExpire().Should().BeTrue();
    }

    [Fact]
    public void Active_work_or_prompts_can_stop_the_deadline_and_resume_with_a_fresh_period()
    {
        var clock = new FakeTimeProvider();
        var timeout = new PresentationInactivityTimeout(clock);
        timeout.Restart(10);
        clock.Advance(TimeSpan.FromSeconds(9));
        timeout.Stop();
        clock.Advance(TimeSpan.FromMinutes(1));
        timeout.TryExpire().Should().BeFalse();
        timeout.IsScheduled.Should().BeFalse();
        timeout.Restart(10);
        timeout.Remaining.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Presence_and_response_deadlines_are_independent()
    {
        var clock = new FakeTimeProvider();
        var presence = new PresentationInactivityTimeout(clock);
        var response = new PresentationInactivityTimeout(clock);
        presence.Restart(10);
        response.Restart(5);

        clock.Advance(TimeSpan.FromSeconds(5));
        response.TryExpire().Should().BeTrue();
        presence.TryExpire().Should().BeFalse();
        response.Restart(20);
        clock.Advance(TimeSpan.FromSeconds(5));

        presence.TryExpire().Should().BeTrue();
        response.TryExpire().Should().BeFalse();
        response.Remaining.Should().Be(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Late_dispatch_expires_without_starting_a_new_delay()
    {
        var clock = new FakeTimeProvider();
        var timeout = new PresentationInactivityTimeout(clock);
        timeout.Restart(10);
        clock.Advance(TimeSpan.FromMinutes(1));

        timeout.Remaining.Should().Be(TimeSpan.Zero);
        timeout.TryExpire().Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Invalid_timeouts_do_not_replace_a_scheduled_deadline(int seconds)
    {
        var timeout = new PresentationInactivityTimeout(new FakeTimeProvider());
        timeout.Restart(10);
        var action = () => timeout.Restart(seconds);

        action.Should().Throw<ArgumentOutOfRangeException>().WithParameterName(nameof(seconds));
        timeout.Remaining.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void Unscheduled_or_stopped_timeouts_never_expire()
    {
        var timeout = new PresentationInactivityTimeout(new FakeTimeProvider());
        timeout.Remaining.Should().Be(TimeSpan.Zero);
        timeout.TryExpire().Should().BeFalse();
        timeout.Restart(10);
        timeout.Stop();
        timeout.Remaining.Should().Be(TimeSpan.Zero);
        timeout.TryExpire().Should().BeFalse();
    }

    private sealed class FakeTimeProvider : TimeProvider
    {
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => timestamp;

        public void Advance(TimeSpan elapsed) => timestamp += elapsed.Ticks;
    }
}
