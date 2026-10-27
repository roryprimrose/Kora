using AwesomeAssertions;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Communication;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("local", true)]
    [InlineData("system", false)]
    [InlineData("invocation", false)]
    [InlineData("completed", false)]
    [InlineData("stopped", false)]
    public void SettingInspectionOriginalPrivateInputIsNotLaundered(string mode, bool expected)
    {
        var fixture = new Fixture();
        var request = HostRequest.Create(mode is "system" ? RequestOrigin.HostSystem : RequestOrigin.LocalUi);
        if (mode is "invocation") { request = new(request.RequestId, request.SessionId, request.TaskId, request.Origin, new(Guid.NewGuid())); }
        using var root = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        if (mode is "completed") { root.Complete(HostOperationOutcome.Completed); }
        if (mode is "stopped") { root.Activity!.Stop(); }
        if (mode is "stopped")
        {
            FluentActions.Invoking(() => fixture.ViewModel.CaptureSettingInspectionAdmission()).Should().Throw<InvalidOperationException>();
            fixture.ViewModel.Dispose();
            return;
        }
        var admitted = fixture.ViewModel.CaptureSettingInspectionAdmission();
        admitted().Should().Be(expected);
        fixture.ViewModel.CloseForObservedPrivacyEvent("fixture", hidePresentation: true);
        admitted().Should().BeFalse();
        fixture.ViewModel.Dispose();
        admitted().Should().BeFalse();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("binding")]
    [InlineData("privacy")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("consent")]
    [InlineData("voice")]
    public async Task SettingInspectionSharedOriginalVoiceAdmissionCannotReviveAfterChanges(string change)
    {
        var f = await Fixture.CreateInitializedAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, show spoken summary limits", 1);
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(() => true, () => owner);
        using var root = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice),
            HostActivityLayer.Application, HostOperation.Request);
        var admitted = f.ViewModel.CaptureSettingInspectionAdmission();
        admitted().Should().BeTrue();
        if (change is "owner") { owner++; }
        if (change is "binding") { f.ViewModel.BindCallOwnershipGate(() => true, () => owner); }
        if (change is "privacy") { f.PrivacyObservation.Current = f.PrivacyObservation.Current with { TopologyRevision = 2 }; }
        if (change is "call") { f.CallState.SetState(CallState.Unknown); await f.Dispatcher.LastInvocation; }
        if (change is "input") { await f.RaiseActivatedTranscriptAsync("Kora, show spoken summary limits", 1); }
        if (change is "consent") { await f.ViewModel.SetVoiceConsentAsync(false); }
        if (change is "voice") { await f.ViewModel.ToggleListeningCommand.ExecuteAsync(); }
        admitted().Should().BeFalse();
        await f.ViewModel.DisposeAsync();
    }

    [Fact]
    public async Task SettingInspectionAssistantNameAbaCannotRenewOriginalReview()
    {
        var f = await Fixture.CreateInitializedAsync();
        Func<bool> admitted;
        using (HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.LocalUi), HostActivityLayer.Application, HostOperation.Request))
        { admitted = f.ViewModel.CaptureSettingInspectionAdmission(); }
        admitted().Should().BeTrue();
        f.ViewModel.AssistantNameInput = "Nova";
        await f.ViewModel.ApplyAssistantNameCommand.ExecuteAsync();
        await f.ViewModel.ResetAssistantNameCommand.ExecuteAsync();
        f.ViewModel.AssistantName.Should().Be("Kora");
        admitted().Should().BeFalse();
        await f.ViewModel.DisposeAsync();
    }
}
