using AwesomeAssertions;
using Kora.Core.Tools;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("list capabilities", "capabilities.list")]
    [InlineData("Kora, runtime.list", "local.inference")]
    [InlineData("show local runtime status", "NotObserved")]
    [InlineData("show dependency readiness", "kora.storage")]
    [InlineData("show registry version", "1.2.3")]
    [InlineData("describe capability application.get_version", "application.get_version")]
    [InlineData("capabilities.get computer.lock", "unknown-capability")]
    [InlineData("capabilities.get", "invalid-id-input")]
    [InlineData("capabilities.list {\"lane\":\"Execution\"}", "invalid-input")]
    [InlineData("runtime.list foreign", "invalid-input")]
    public async Task Native_read_only_discovery_is_composed_and_does_not_invoke_a_model(string command, string expected)
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.CommandText = command;
        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();
        fixture.ViewModel.ResponseBody.Should().Contain(expected);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Session.LockCalls.Should().Be(0);
    }

    [Fact]
    public void Native_discovery_rechecks_host_eligibility_before_dispatch()
    {
        var fixture = new Fixture();
        fixture.Session.IsUnlocked = false;
        var run = () => fixture.ViewModel.TryPresentCapabilityCommand("list capabilities");
        run.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Existing_help_points_to_the_same_canonical_registry_without_changing_selector_actions()
    {
        var fixture = await Fixture.CreateInitializedAsync();
        fixture.ViewModel.CommandText = "help";
        await fixture.ViewModel.RunTypedCommand.ExecuteAsync();
        fixture.ViewModel.ResponseBody.Should().Contain("list capabilities").And.Contain("describe capability");
        fixture.Catalog.GetCommands().Should().HaveCount(25);
        ReadOnlyCapabilityCatalog.Descriptors.Should().HaveCount(6);
    }
}
