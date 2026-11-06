using AwesomeAssertions;
using Kora.Core.Maintenance;

namespace Kora.Core.UnitTests.Maintenance;

public sealed class MaintenanceUpdateCommandTests
{
    [Theory]
    [InlineData("maintenance check", false, null)]
    [InlineData("maintenance check latest", false, null)]
    [InlineData("Kora, maintenance upgrade", true, null)]
    [InlineData("KORA\tmaintenance upgrade LATEST", true, null)]
    [InlineData("maintenance upgrade 1.2.3", true, "1.2.3")]
    [InlineData("maintenance check 1.2.3-beta7", false, "1.2.3-beta7")]
    public void Typed_and_activated_requests_select_only_a_version(string text, bool upgrade, string? version)
    {
        var parsed = MaintenanceUpdateCommand.Parse(text, "Kora");
        parsed.Should().NotBeNull();
        parsed!.IsValid.Should().BeTrue();
        parsed.Upgrade.Should().Be(upgrade);
        parsed.Version?.ToString().Should().Be(version);
    }

    [Theory]
    [InlineData("maintenance upgrade https://example.com/installer.exe")]
    [InlineData("maintenance check 1.2.3 & calc.exe")]
    [InlineData("maintenance upgrade v1.2.3")]
    [InlineData("maintenance check 1.2.3-rc1")]
    [InlineData("maintenance upgrade now")]
    public void Invalid_targets_are_explicit_and_never_become_commands_or_urls(string text) =>
        MaintenanceUpdateCommand.Parse(text, "Kora")!.IsValid.Should().BeFalse();

    [Theory]
    [InlineData("Kora")]
    [InlineData("Korax maintenance check")]
    [InlineData("maintenance status")]
    [InlineData("maintenance upgradeable")]
    public void Other_input_is_not_an_update_request(string text) =>
        MaintenanceUpdateCommand.Parse(text, "Kora").Should().BeNull();

    [Fact]
    public void Complete_input_is_bounded()
    {
        MaintenanceUpdateCommand.Parse("maintenance check".PadLeft(MaintenanceCommandParser.MaximumLength), "Kora")!.IsValid.Should().BeTrue();
        MaintenanceUpdateCommand.Parse("maintenance check".PadLeft(MaintenanceCommandParser.MaximumLength + 1), "Kora")!.IsValid.Should().BeFalse();
    }
}
