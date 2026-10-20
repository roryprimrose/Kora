using AwesomeAssertions;

using Kora.Core.Communication;
using Kora.Core.Platform;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task FileInspectionCapturesOriginalInputAndExactOwnerBindingAndRevisionWithoutEffects()
    {
        var f = await Fixture.CreateInitializedAsync();
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(() => true, () => owner);
        var current = f.ViewModel.CaptureLocalFileInspectionEligibility();
        current().Should().BeTrue();
        owner++;
        current().Should().BeFalse();
        current = f.ViewModel.CaptureLocalFileInspectionEligibility();
        current().Should().BeTrue();
        f.ViewModel.BindCallOwnershipGate(() => true, () => owner);
        current().Should().BeFalse();
        current = f.ViewModel.CaptureLocalFileInspectionEligibility();
        current().Should().BeTrue();
        f.ViewModel.CommandText = "changed input";
        current().Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
        f.ViewModel.CaptureLocalFileInspectionEligibility()().Should().BeFalse();
    }

    [Theory]
    [InlineData("call")]
    [InlineData("privacy")]
    [InlineData("locked")]
    [InlineData("owner")]
    public async Task FileInspectionFailsClosedForChangedCallPrivateHostAndOwnership(string boundary)
    {
        var f = await Fixture.CreateInitializedAsync();
        var owner = true;
        f.ViewModel.BindCallOwnershipGate(() => owner);
        var current = f.ViewModel.CaptureLocalFileInspectionEligibility();
        current().Should().BeTrue();
        switch (boundary)
        {
            case "call": f.CallState.SetState(CallState.Active); break;
            case "privacy":
                var privacy = f.PrivacyObservation.Current;
                f.PrivacyObservation.Current = privacy with { TopologyRevision = privacy.TopologyRevision + 1 };
                f.PrivacyObservation.Publish(new(privacy, f.PrivacyObservation.Current,
                    WindowsPrivacyChangeReason.DeviceTopology));
                break;
            case "locked": f.Session.IsUnlocked = false; break;
            case "owner": owner = false; break;
        }
        current().Should().BeFalse();
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }
}
