using AwesomeAssertions;

using Kora.Core.Communication;
using Kora.Core.Dependencies;
using Kora.Core.Providers;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("input")]
    [InlineData("call")]
    [InlineData("private")]
    [InlineData("mode")]
    [InlineData("profile")]
    public async Task PromptAuthoringEligibilityPinsInputPrivacyCallAndProviderState(string change)
    {
        var f = new Fixture(enableProviderModeConfiguration: change is "mode",
            enableProviderProfiles: change is "profile");
        await using var admission = f.OutputAdmission;
        await f.ViewModel.InitializeAsync();
        var current = f.ViewModel.CapturePromptAuthoringEligibility();
        current().Should().BeTrue();
        switch (change)
        {
            case "input": f.ViewModel.CommandText = "replacement"; break;
            case "call": f.CallState.SetState(CallState.Unknown); await f.Dispatcher.LastInvocation; break;
            case "private": f.ViewModel.CloseForObservedPrivacyEvent("Synthetic closure", hidePresentation: true); break;
            case "mode":
                await f.ViewModel.RefreshProviderModeCommand.ExecuteAsync();
                f.ViewModel.SelectedProviderModeChoice = f.ViewModel.ProviderModeChoices.Single(choice => choice.Mode == ModelProviderMode.LocalFirst);
                await f.ViewModel.SaveProviderModeCommand.ExecuteAsync();
                break;
            case "profile":
                await f.ViewModel.RefreshProviderProfileCommand.ExecuteAsync();
                f.ViewModel.SelectedProviderServiceChoice = f.ViewModel.ProviderServiceChoices.Single(choice => choice.Service == ProviderService.Codex);
                await f.ViewModel.SaveProviderServiceCommand.ExecuteAsync();
                break;
        }
        current().Should().Be(change is "none");
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
