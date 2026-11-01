using AwesomeAssertions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task NativeReviewConfirmationResetAndInspectionDoNotEnableOtherWork()
    {
        await using var startup = new StartupPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(startupPresentationConfiguration: startup.Service);
        f.ViewModel.CanControlStartupPresentation.Should().BeFalse();
        f.ViewModel.BindStartupPresentationNativeLifetime(static () => true);
        var events = f.Events.ToArray();
        await f.ViewModel.InspectStartupPresentationCommand.ExecuteAsync();
        await f.ViewModel.ReviewWorkspaceStartupCommand.ExecuteAsync();
        f.ViewModel.StartupPresentationReviewText.Should().Contain("No change yet");
        startup.Preferences.Writes.Should().Be(0);
        await f.ViewModel.CancelStartupPresentationCommand.ExecuteAsync();
        f.ViewModel.StartupPresentationReviewText.Should().Contain("No manual");
        await f.ViewModel.ReviewWorkspaceStartupCommand.ExecuteAsync();
        await f.ViewModel.ConfirmStartupPresentationCommand.ExecuteAsync();
        startup.Preferences.Value.Should().Be(StartupPresentation.Workspace);
        await f.ViewModel.ReviewTrayStartupCommand.ExecuteAsync();
        await f.ViewModel.ConfirmStartupPresentationCommand.ExecuteAsync();
        startup.Preferences.Value.Should().Be(StartupPresentation.Tray);
        await f.ViewModel.ResetStartupPresentationCommand.ExecuteAsync();
        await f.ViewModel.ConfirmStartupPresentationCommand.ExecuteAsync();
        startup.Preferences.Value.Should().BeNull();
        f.Events.Should().Equal(events);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.HasVoiceConsent.Should().BeFalse();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("owner")]
    [InlineData("binding")]
    [InlineData("lock")]
    [InlineData("unknown")]
    [InlineData("private")]
    [InlineData("call")]
    [InlineData("input")]
    [InlineData("name")]
    [InlineData("voice")]
    [InlineData("dispose")]
    public async Task OriginalNativeReviewCannotSurviveBoundaryChanges(string boundary)
    {
        await using var startup = new StartupPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(startupPresentationConfiguration: startup.Service);
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        f.ViewModel.BindStartupPresentationNativeLifetime(static () => true);
        await f.ViewModel.ReviewWorkspaceStartupCommand.ExecuteAsync();
        switch (boundary)
        {
            case "hide": f.ViewModel.BindStartupPresentationNativeLifetime(static () => false); break;
            case "owner": owner++; break;
            case "binding": f.ViewModel.BindCallOwnershipGate(static () => true); break;
            case "lock": f.Session.IsUnlocked = false; break;
            case "unknown": f.PrivacyObservation.Current = f.PrivacyObservation.Current with { SessionState = Kora.Core.Platform.WindowsSessionState.Unknown }; break;
            case "private": await f.ViewModel.HideApplicationAsync(); break;
            case "call": f.CallState.SetState(Kora.Core.Communication.CallState.Active); break;
            case "input": await f.RunAsync("show settings"); break;
            case "name": await f.RunAsync("set appearance.theme to dark"); break;
            case "dispose": f.ViewModel.Dispose(); break;
        }
        using var origin = boundary is "voice" ? Kora.Core.Diagnostics.HostActivity.BeginRoot(
            HostRequest.Create(RequestOrigin.ActivatedVoice), Kora.Core.Diagnostics.HostActivityLayer.Application,
            Kora.Core.Diagnostics.HostOperation.Request) : null;
        await f.ViewModel.ConfirmStartupPresentationCommand.ExecuteAsync();
        startup.Preferences.Writes.Should().Be(0);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Voice.StartCalls.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task MissingServiceAndMissingReviewNeverClaimSuccess()
    {
        var absent = new Fixture();
        absent.ViewModel.StartupPresentationStatus.Should().Contain("unavailable");
        await absent.ViewModel.ReviewWorkspaceStartupCommand.ExecuteAsync();
        absent.ViewModel.Transcript.Should().Contain("requires this native");
        absent.ViewModel.Dispose();
        await using var startup = new StartupPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(startupPresentationConfiguration: startup.Service);
        f.ViewModel.BindStartupPresentationNativeLifetime(static () => true);
        await f.ViewModel.ConfirmStartupPresentationCommand.ExecuteAsync();
        f.ViewModel.Transcript.Should().Contain("Review this exact");
        startup.Preferences.Writes.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task LosingOwnershipAfterDurableConfirmationDoesNotPresentLateSuccess()
    {
        await using var startup = new StartupPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(startupPresentationConfiguration: startup.Service);
        long owner = 1;
        var revokeAfterReads = 0;
        f.ViewModel.BindCallOwnershipGate(static () => true, () =>
        {
            var observed = owner;
            if (revokeAfterReads > 0 && --revokeAfterReads == 0) { owner++; }
            return observed;
        });
        f.ViewModel.BindStartupPresentationNativeLifetime(static () => true);
        await f.ViewModel.ReviewWorkspaceStartupCommand.ExecuteAsync();
        startup.Preferences.AfterConfirm = () => revokeAfterReads = 2;
        await f.ViewModel.ConfirmStartupPresentationCommand.ExecuteAsync();
        f.ViewModel.Transcript.Should().Contain("not confirmed");
        startup.Preferences.Value.Should().Be(StartupPresentation.Workspace);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativePreferenceCannotConsumeAnActiveQuestionOrApproval(bool approval)
    {
        await using var startup = new StartupPresentationConfigurationServiceTests.Fixture();
        var f = new Fixture(startupPresentationConfiguration: startup.Service);
        f.ViewModel.BindStartupPresentationNativeLifetime(static () => true);
        f.Probe.Status = new("local.inference", "Local inference", Kora.Core.Dependencies.DependencyReadiness.Ready, "fixture");
        if (approval) { f.Reasoner.Action = Kora.Core.Commands.BuiltInAction.EnableHostedModels; }
        else { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("help me");
        await f.ViewModel.ActiveReasoningTask!;
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        var eligible = f.ViewModel.CaptureStartupPresentationEligibility();
        eligible().Should().BeFalse();
        await f.ViewModel.ReviewWorkspaceStartupCommand.ExecuteAsync();
        await f.ViewModel.ConfirmStartupPresentationCommand.ExecuteAsync();
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        startup.Preferences.Writes.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("unknown")]
    [InlineData("owner")]
    [InlineData("call")]
    public void StartupCaptureWhileHeldCannotReviveWhenTheHostBecomesEligible(string hold)
    {
        var f = new Fixture();
        switch (hold)
        {
            case "locked": f.Session.IsUnlocked = false; break;
            case "unknown": f.PrivacyObservation.Current = f.PrivacyObservation.Current with { SessionState = Kora.Core.Platform.WindowsSessionState.Unknown }; break;
            case "owner": f.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "call": f.CallState.SetState(Kora.Core.Communication.CallState.Active); break;
        }
        var eligible = f.ViewModel.CaptureStartupPresentationEligibility();
        eligible().Should().BeFalse();
        f.Session.IsUnlocked = true;
        f.PrivacyObservation.Current = f.PrivacyObservation.Current with { SessionState = Kora.Core.Platform.WindowsSessionState.Unlocked };
        f.ViewModel.BindCallOwnershipGate(static () => true);
        f.CallState.SetState(Kora.Core.Communication.CallState.Clear);
        eligible().Should().BeFalse();
        f.ViewModel.Dispose();
    }
}
