using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Core.UnitTests.Configuration;

public sealed class SessionQueuePreferencesTests
{
    [Fact]
    public void Exact_fixed_profile_defaults_independent_overrides_and_resets()
    {
        var defaults = new SessionQueuePreferences();
        defaults.IsDefault.Should().BeTrue();
        defaults.Limits.Should().Be(new SessionQueueLimits(10, 1));
        defaults.PendingLifetimeMinutes.Should().BeNull();
        defaults.Limits.PendingLifetimeMinutes.Should().Be(30);
        var both = defaults.With(SessionQueueOption.PendingPerSession, "1").With(SessionQueueOption.ExecutionSlots, "2");
        both.Limits.Should().Be(new SessionQueueLimits(1, 2));
        both.IsDefault.Should().BeFalse();
        both.With(SessionQueueOption.PendingPerSession, null).Should().Be(new SessionQueuePreferences(null, 2));
        both.With(SessionQueueOption.ExecutionSlots, null).Should().Be(new SessionQueuePreferences(1));
        both.With(SessionQueueOption.PendingPerSession, "10").Limits.PendingPerSession.Should().Be(10);
        both.With(SessionQueueOption.ExecutionSlots, "1").Limits.ExecutionSlots.Should().Be(1);
        var unknown = () => defaults.With((SessionQueueOption)99, null);
        unknown.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(120)]
    public void Lifetime_bounds_and_per_option_reset_preserve_capacity_and_slots(int minutes)
    {
        var value = new SessionQueuePreferences(3, 2).With(SessionQueueOption.PendingLifetimeMinutes,
            minutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        value.PendingLifetimeMinutes.Should().Be(minutes);
        value.Limits.Should().Be(new SessionQueueLimits(3, 2, minutes));
        value.With(SessionQueueOption.PendingLifetimeMinutes, null).Should().Be(new SessionQueuePreferences(3, 2));
        value.With(SessionQueueOption.PendingPerSession, null).Should().Be(new SessionQueuePreferences(null, 2, minutes));
        value.With(SessionQueueOption.ExecutionSlots, null).Should().Be(new SessionQueuePreferences(3, null, minutes));
        new SessionQueuePreferences(pendingLifetimeMinutes: minutes).IsDefault.Should().BeFalse();
    }

    [Theory]
    [InlineData(SessionQueueOption.PendingPerSession, "0")]
    [InlineData(SessionQueueOption.PendingPerSession, "11")]
    [InlineData(SessionQueueOption.PendingPerSession, "50")]
    [InlineData(SessionQueueOption.ExecutionSlots, "0")]
    [InlineData(SessionQueueOption.ExecutionSlots, "3")]
    [InlineData(SessionQueueOption.ExecutionSlots, "02")]
    [InlineData(SessionQueueOption.PendingPerSession, "")]
    [InlineData(SessionQueueOption.PendingPerSession, "+1")]
    [InlineData(SessionQueueOption.PendingPerSession, " 1")]
    [InlineData(SessionQueueOption.PendingPerSession, "1 ")]
    [InlineData(SessionQueueOption.PendingPerSession, "1.0")]
    [InlineData(SessionQueueOption.PendingPerSession, "2147483648")]
    [InlineData(SessionQueueOption.PendingLifetimeMinutes, "0")]
    [InlineData(SessionQueueOption.PendingLifetimeMinutes, "-1")]
    [InlineData(SessionQueueOption.PendingLifetimeMinutes, "121")]
    [InlineData(SessionQueueOption.PendingLifetimeMinutes, "030")]
    [InlineData(SessionQueueOption.PendingLifetimeMinutes, "+30")]
    [InlineData(SessionQueueOption.PendingLifetimeMinutes, "30 ")]
    [InlineData(SessionQueueOption.PendingLifetimeMinutes, " 30")]
    [InlineData(SessionQueueOption.PendingLifetimeMinutes, "30.0")]
    [InlineData(SessionQueueOption.PendingLifetimeMinutes, "30 minutes")]
    [InlineData(SessionQueueOption.PendingLifetimeMinutes, "٣٠")]
    [InlineData(SessionQueueOption.PendingLifetimeMinutes, "2147483648")]
    public void Malformed_noncanonical_or_broader_proposed_ranges_never_enter_the_fixed_profile(SessionQueueOption option, string text)
    {
        var action = () => new SessionQueuePreferences().With(option, text);
        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
