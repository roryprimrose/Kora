using AwesomeAssertions;
using Kora.Core.Maintenance;

namespace Kora.Core.UnitTests.Maintenance;

public sealed class MaintenanceSettingsTests
{
    [Fact]
    public void DefaultsDisableNetworkAndBetaAndUseSixHours()
    {
        MaintenanceSettings.Default.Should().Be(new MaintenanceSettings(false, false, 6));
        MaintenanceSettings.Default.NetworkEnabled.Should().BeFalse();
        MaintenanceSettings.Default.IncludePrereleases.Should().BeFalse();
        MaintenanceSettings.Default.CheckIntervalHours.Should().Be(6);
    }

    [Theory]
    [InlineData(false, false, 1, "v1,0,0,1")]
    [InlineData(false, true, 6, "v1,0,1,6")]
    [InlineData(true, false, 37, "v1,1,0,37")]
    [InlineData(true, true, 168, "v1,1,1,168")]
    public void ValidSettingsRoundTripExactly(bool enabled, bool beta, int hours, string serialized)
    {
        var settings = new MaintenanceSettings(enabled, beta, hours);
        settings.Serialize().Should().Be(serialized);
        MaintenanceSettings.Parse(serialized).Should().Be(settings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(169)]
    [InlineData(int.MaxValue)]
    public void InvalidIntervalsCannotBeConstructed(int hours)
    {
        var create = () => new MaintenanceSettings(true, true, hours);
        create.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("v2,1,0,6")]
    [InlineData("v1,1,0")]
    [InlineData("v1,1,0,6,extra")]
    [InlineData("v1,true,0,6")]
    [InlineData("v1,1,2,6")]
    [InlineData("v1,1,0,0")]
    [InlineData("v1,1,0,169")]
    [InlineData("v1,1,0,-1")]
    [InlineData("v1,1,0,2147483648")]
    [InlineData("v1,1,0,06")]
    [InlineData("v1,1,0,+6")]
    [InlineData("v1,1,0, 6")]
    [InlineData("v1,1,0,6\n")]
    [InlineData("v1,1,0,6.0")]
    public void InvalidSavedValuesNeverBecomeDefaults(string text)
    {
        var parse = () => MaintenanceSettings.Parse(text);
        parse.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NullTextIsRejected()
    {
        var parse = () => MaintenanceSettings.Parse(null!);
        parse.Should().Throw<ArgumentNullException>();
    }
}
