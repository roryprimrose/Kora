using AwesomeAssertions;
using Kora.Core.Communication;
using Kora.Core.Platform;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task NativeHandoffRequestIsLocalPresentationOnlyAndRetiresOnCallOrDisposal()
    {
        var f = new Fixture();
        await f.ViewModel.InitializeAsync();
        f.ViewModel.ReviewPendingHandoff();
        var requested = 0;
        f.ViewModel.HandoffReviewRequested += (_, _) => requested++;
        var eligible = f.ViewModel.CaptureHandoffPresentationEligibility();
        eligible().Should().BeTrue();
        f.ViewModel.ReviewPendingHandoff();
        requested.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.TextToSpeech.SpokenText.Should().BeNull();
        f.CallState.SetState(CallState.Active);
        await f.Dispatcher.LastInvocation;
        eligible().Should().BeFalse();
        f.ViewModel.Dispose();
        f.ViewModel.CaptureHandoffPresentationEligibility()().Should().BeFalse();
    }

    [Fact]
    public async Task NativeHandoffLifetimeCannotReviveAfterTopologyOrOwnershipChanges()
    {
        var f = new Fixture();
        await f.ViewModel.InitializeAsync();
        var eligibility = f.ViewModel.CaptureHandoffPresentationEligibility();
        eligibility().Should().BeTrue();
        f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = f.PrivacyObservation.Current.TopologyRevision + 1 };
        eligibility().Should().BeFalse();
        f.ViewModel.BindCallOwnershipGate(static () => false);
        f.ViewModel.CaptureHandoffPresentationEligibility()().Should().BeFalse();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task NativeReviewOperationsAndClosureMustBeQuiescentBeforeInstanceHandoff()
    {
        var f = new Fixture();
        await f.ViewModel.InitializeAsync();
        f.ViewModel.BindHandoffReviewQuiescence(static () => false);
        (await f.ViewModel.TryPrepareHandoffAsync()).Should().BeFalse();
        f.ViewModel.BindHandoffReviewQuiescence(static () => true);
        (await f.ViewModel.TryPrepareHandoffAsync()).Should().BeTrue();
        f.ViewModel.Dispose();
    }
}
