using AwesomeAssertions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task LogonStartupNativeAndTypedShareReviewSaveResetAndReadbackWithoutAudioOrModelWork()
    {
        await using var startup = new LogonStartupConfigurationServiceTests.Fixture();
        var f = new Fixture(logonStartupConfiguration: startup.Service);
        f.ViewModel.CanControlLogonStartupNative.Should().BeFalse();
        f.ViewModel.BindLogonStartupNativeLifetime(static () => true);
        f.ViewModel.CanControlLogonStartupNative.Should().BeTrue();
        var events = f.Events.ToArray();
        await f.ViewModel.InspectLogonStartupCommand.ExecuteAsync();
        await f.ViewModel.EnableLogonStartupCommand.ExecuteAsync();
        f.ViewModel.LogonStartupReviewText.Should().Contain("No change yet");
        startup.Registration.Writes.Should().Be(0);
        await f.ViewModel.CancelLogonStartupCommand.ExecuteAsync();
        f.ViewModel.LogonStartupReviewText.Should().Contain("No startup change");
        await f.RunAsync("enable start at logon");
        await f.RunAsync("confirm start at logon change");
        startup.Preferences.Value.Should().BeTrue();
        f.ViewModel.LogonStartupStatus.Should().Contain("desired: True").And.Contain("next logon: True");
        await f.ViewModel.DisableLogonStartupCommand.ExecuteAsync();
        await f.ViewModel.ConfirmLogonStartupCommand.ExecuteAsync();
        startup.Preferences.Value.Should().BeFalse();
        await f.ViewModel.ResetLogonStartupCommand.ExecuteAsync();
        await f.ViewModel.ConfirmLogonStartupCommand.ExecuteAsync();
        startup.Preferences.Value.Should().BeNull();
        f.Events.Should().Equal(events);
        f.Reasoner.Requests.Should().BeEmpty();
        f.Voice.StartedMicrophone.Should().BeNull();
        f.ViewModel.HasVoiceConsent.Should().BeFalse();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("hide")]
    [InlineData("owner")]
    [InlineData("rebind")]
    [InlineData("lock")]
    [InlineData("unrelated")]
    [InlineData("invalid")]
    [InlineData("review call speech")]
    [InlineData("confirm call speech")]
    [InlineData("cancel call speech")]
    [InlineData("call speech status")]
    public async Task LogonStartupReviewCannotSurviveAdmissionOrInputChanges(string boundary)
    {
        await using var startup = new LogonStartupConfigurationServiceTests.Fixture();
        var f = new Fixture(logonStartupConfiguration: startup.Service);
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        f.ViewModel.BindLogonStartupNativeLifetime(static () => true);
        await f.ViewModel.EnableLogonStartupCommand.ExecuteAsync();
        switch (boundary)
        {
            case "hide": f.ViewModel.BindLogonStartupNativeLifetime(static () => false); break;
            case "owner": owner++; break;
            case "rebind": f.ViewModel.BindCallOwnershipGate(static () => true); break;
            case "lock": f.Session.IsUnlocked = false; break;
            case "unrelated": await f.RunAsync("show settings"); break;
            case "invalid": await f.RunAsync("enable start at logon and listen"); break;
            case "review call speech":
            case "confirm call speech":
            case "cancel call speech":
            case "call speech status": await f.RunAsync(boundary); break;
        }
        await f.ViewModel.ConfirmLogonStartupCommand.ExecuteAsync();
        startup.Registration.Writes.Should().Be(0);
        startup.Preferences.Writes.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task LogonStartupUnavailableAndMissingReviewAreExplicit()
    {
        var missing = new Fixture();
        missing.ViewModel.CanControlLogonStartupNative.Should().BeFalse();
        missing.ViewModel.LogonStartupStatus.Should().Contain("unavailable");
        await missing.RunAsync("enable start at logon");
        missing.ViewModel.Transcript.Should().Contain("unavailable");
        await using var startup = new LogonStartupConfigurationServiceTests.Fixture();
        var f = new Fixture(logonStartupConfiguration: startup.Service);
        await f.RunAsync("confirm start at logon change");
        f.ViewModel.Transcript.Should().Contain("Review this exact change first");
        startup.Registration.Writes.Should().Be(0);
    }

    [Fact]
    public async Task LogonStartupCallDriftAtRequestedAuditIsDeniedWithoutAWrite()
    {
        await using var startup = new LogonStartupConfigurationServiceTests.Fixture();
        var f = new Fixture(logonStartupConfiguration: startup.Service);
        await f.RunAsync("enable start at logon");
        startup.Audit.BeforeWrite = item =>
        {
            if (item.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Requested)
            { f.CallState.SetState(Kora.Core.Communication.CallState.Active); }
        };
        await f.RunAsync("confirm start at logon change");
        f.ViewModel.Transcript.Should().Contain("call/channel policy denied");
        startup.Registration.Writes.Should().Be(0);
    }

    [Fact]
    public async Task LogonStartupDoesNotPresentLateSuccessAfterFinalReceiptLosesOwnership()
    {
        await using var startup = new LogonStartupConfigurationServiceTests.Fixture();
        var f = new Fixture(logonStartupConfiguration: startup.Service);
        long owner = 1;
        var revokeAfterReads = 0;
        f.ViewModel.BindCallOwnershipGate(static () => true, () =>
        {
            var observed = owner;
            if (revokeAfterReads > 0 && --revokeAfterReads == 0) { owner++; }
            return observed;
        });
        await f.RunAsync("enable start at logon");
        startup.Preferences.AfterConfirm = () => revokeAfterReads = 2;
        await f.RunAsync("confirm start at logon change");
        f.ViewModel.Transcript.Should().Contain("admission changed before presentation");
        startup.Registration.Writes.Should().Be(1);
        startup.Preferences.Value.Should().BeTrue();
    }

    [Theory]
    [InlineData("no-consent")]
    [InlineData("locked")]
    [InlineData("unknown")]
    [InlineData("owner")]
    public async Task LogonStartupSavedOptInCannotBypassFreshLaunchPrivacyConsentOrAudioGates(string boundary)
    {
        await using var startup = new LogonStartupConfigurationServiceTests.Fixture();
        startup.Preferences.Value = true;
        startup.Registration.State = startup.Registration.State with { Enabled = true };
        var f = new Fixture(logonStartupConfiguration: startup.Service);
        f.Voice.Microphones = [new("fixture-only", "Fake microphone")];
        f.Voice.DefaultMicrophoneId = "fixture-only";
        f.VoiceConsent.Consent = boundary is not "no-consent";
        switch (boundary)
        {
            case "locked": f.Session.IsUnlocked = false; break;
            case "unknown": f.PrivacyObservation.Current = f.PrivacyObservation.Current with { SessionState = Kora.Core.Platform.WindowsSessionState.Unknown }; break;
            case "owner": f.ViewModel.BindVoiceOwnershipGate(static () => false); break;
        }
        await f.ViewModel.InitializeAsync();
        f.Voice.StartCalls.Should().Be(0);
        f.Voice.StartedMicrophone.Should().BeNull();
        if (boundary is not "owner") { f.ViewModel.IsVoiceEnabled.Should().BeFalse(); }
        f.Reasoner.Requests.Should().BeEmpty();
        startup.Registration.Writes.Should().Be(0);
        startup.Preferences.Writes.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LogonStartupDoesNotConsumePendingQuestionOrApproval(bool approval)
    {
        await using var startup = new LogonStartupConfigurationServiceTests.Fixture();
        var f = new Fixture(logonStartupConfiguration: startup.Service);
        f.Probe.Status = new("local.inference", "Local inference", DependencyReadiness.Ready, "fixture");
        if (approval) { f.Reasoner.Action = Kora.Core.Commands.BuiltInAction.EnableHostedModels; }
        else { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("help me");
        await f.ViewModel.ActiveReasoningTask!;
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        var title = f.ViewModel.ResponseTitle;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("enable start at logon");
        await f.RunAsync("confirm start at logon change");
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        f.ViewModel.ResponseTitle.Should().Be(title);
        f.ViewModel.ResponseBody.Should().Be(body);
        startup.Registration.Writes.Should().Be(0);
    }

    [Fact]
    public async Task LogonStartupActivatedVoiceRequiresCurrentPrefixAndSharesSeparateConfirmation()
    {
        await using var startup = new LogonStartupConfigurationServiceTests.Fixture();
        var f = new Fixture(logonStartupConfiguration: startup.Service);
        await f.RaiseActivatedTranscriptAsync("Kora, enable start at logon", 0.99f);
        startup.Registration.Writes.Should().Be(0);
        f.ViewModel.LogonStartupReviewText.Should().Contain("No change yet");
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        await f.Voice.RaiseTranscriptAsync("Kora, confirm start at logon change", 0.99f);
        startup.Preferences.Value.Should().BeTrue(f.ViewModel.Transcript);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        await f.Voice.RaiseTranscriptAsync("disable start at logon", 0.99f);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        await f.Voice.RaiseTranscriptAsync("Kora, confirm start at logon change", 0.99f);
        startup.Registration.Writes.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
    }
}
