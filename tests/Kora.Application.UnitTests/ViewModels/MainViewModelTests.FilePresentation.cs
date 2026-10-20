using AwesomeAssertions;

using Kora.Core.Communication;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("clipboard-owner")]
    [InlineData("host-owner")]
    [InlineData("owner-revision")]
    [InlineData("binding")]
    [InlineData("input")]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("dispose")]
    public async Task FileMeasurementPresentationCapturesExistingHostPrivacyInputCallAndOwnershipLifetime(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(() => true, () => owner);
        var current = f.ViewModel.CaptureLocalFilePresentationEligibility();
        current().Should().BeTrue();
        switch (change)
        {
            case "clipboard-owner": f.ViewModel.BindClipboardOwnershipGate(() => false); break;
            case "host-owner": f.ViewModel.BindCallOwnershipGate(() => false); break;
            case "owner-revision": owner++; break;
            case "binding": f.ViewModel.BindCallOwnershipGate(() => true, () => owner); break;
            case "input": await f.RunAsync("hide response"); break;
            case "call":
                f.CallState.SetState(CallState.Active);
                await f.Dispatcher.LastInvocation;
                break;
            case "privacy":
                f.PrivacyObservation.Current = f.PrivacyObservation.Current with
                { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 };
                break;
            case "dispose": f.ViewModel.Dispose(); break;
        }
        current().Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }
}
