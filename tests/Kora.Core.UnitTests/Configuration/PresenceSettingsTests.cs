using AwesomeAssertions;

using Kora.Core.Configuration;

namespace Kora.Core.UnitTests.Configuration;

public sealed class PresenceSettingsTests
{
    [Theory]
    [InlineData(PresenceSettings.MinimumTimeoutSeconds)]
    [InlineData(PresenceSettings.DefaultTimeoutSeconds)]
    [InlineData(PresenceSettings.MaximumTimeoutSeconds)]
    public void ValidateTimeoutSeconds_accepts_supported_values(int value)
    {
        var action = () => PresenceSettings.ValidateTimeoutSeconds(value);

        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(PresenceSettings.MinimumTimeoutSeconds - 1)]
    [InlineData(PresenceSettings.MaximumTimeoutSeconds + 1)]
    public void ValidateTimeoutSeconds_rejects_unsupported_values(int value)
    {
        var action = () => PresenceSettings.ValidateTimeoutSeconds(value);

        action.Should().Throw<ArgumentOutOfRangeException>();
    }
}
