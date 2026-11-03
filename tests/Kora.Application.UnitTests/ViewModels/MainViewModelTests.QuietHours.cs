using AwesomeAssertions;
using Kora.Application.UnitTests.Configuration;
using Kora.Core.Communication;
using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task ActiveScheduledQuietNeverSuppressesOrdinaryRequestedSpeech()
    {
        await using var quiet = new QuietHoursConfigurationServiceTests.Fixture();
        var f = new Fixture(quietHoursConfiguration: quiet.Service);
        f.Voice.Microphones = [new("0", "Headset")];
        f.Voice.DefaultMicrophoneId = "0";
        await f.ViewModel.InitializeAsync();
        await f.ViewModel.ToggleListeningCommand.ExecuteAsync();
        await f.ViewModel.RefreshCommand.ExecuteAsync();
        await f.RunAsync("set notices.quiet-hours to weekly Mon 09:00 17:00 UTC");
        await f.RunAsync("confirm notices.quiet-hours");
        quiet.Time.Now = quiet.Time.Now.AddHours(1);
        quiet.Service.AllowsSpeech(quiet.Service.SpeechRevision).Should().BeFalse();
        f.TextToSpeech.ClearSpokenResponse();
        await f.RunAsync("what power action is pending");
        f.TextToSpeech.SpokenText.Should().Contain("No power action is pending.");
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task QuietHoursNativeAndExactTypedReviewSetGetAndResetCompleteWithoutModelCaptureOrConsentEffects()
    {
        await using var quiet = new QuietHoursConfigurationServiceTests.Fixture();
        var f = new Fixture(quietHoursConfiguration: quiet.Service);
        f.ViewModel.CanChangeQuietHoursNative.Should().BeFalse();
        f.ViewModel.CanConfirmQuietHoursNative.Should().BeFalse();
        await f.ViewModel.InspectQuietHoursCommand.ExecuteAsync();
        f.ViewModel.BindQuietHoursNativeLifetime(static () => true);
        f.ViewModel.CanConfirmQuietHoursNative.Should().BeFalse();
        var events = f.Events.ToArray();
        await f.ViewModel.InspectQuietHoursCommand.ExecuteAsync();
        await f.ViewModel.ReviewQuietHoursCommand.ExecuteAsync();
        f.ViewModel.CanConfirmQuietHoursNative.Should().BeTrue();
        f.ViewModel.QuietHoursReviewText.Should().Contain("Mon,Tue,Wed,Thu,Fri 09:00 17:00 UTC");
        quiet.Preferences.Writes.Should().Be(0);
        await f.ViewModel.ConfirmQuietHoursCommand.ExecuteAsync();
        quiet.Preferences.Value.Should().Be(quiet.Schedule);
        f.ViewModel.QuietHoursStatus.Should().Contain("effective weekly");
        await f.ViewModel.ResetQuietHoursCommand.ExecuteAsync();
        quiet.Preferences.Value.Should().NotBeNull();
        await f.ViewModel.ConfirmQuietHoursCommand.ExecuteAsync();
        quiet.Preferences.Value.Should().BeNull();
        await f.RunAsync("set notices.quiet-hours to Off");
        await f.RunAsync("confirm notices.quiet-hours");
        quiet.Preferences.Value.Should().Be(QuietHoursSchedule.Off);
        await f.RunAsync("get notices.quiet-hours");
        f.ViewModel.Transcript.Should().Contain("effective Off");
        f.Reasoner.Requests.Should().BeEmpty();
        f.Events.Should().Equal(events);
        f.Voice.StartedMicrophone.Should().BeNull();
        f.ViewModel.IsVoiceEnabled.Should().BeFalse();
        f.ViewModel.HasVoiceConsent.Should().BeFalse();
        f.ViewModel.Dispose();
        f.ViewModel.CanChangeQuietHours.Should().BeFalse();
        f.ViewModel.CanChangeQuietHoursNative.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedScheduleInitializesNativeFieldsWhileExplicitOffRemainsOff(bool off)
    {
        await using var quiet = new QuietHoursConfigurationServiceTests.Fixture();
        quiet.Preferences.Value = off ? QuietHoursSchedule.Off : QuietHoursSchedule.Weekly("Mon,Fri", "22:00", "06:00", "Australia/Sydney");
        var f = new Fixture(quietHoursConfiguration: quiet.CreateService());
        f.ViewModel.QuietHoursDays.Should().Be(off ? "Mon,Tue,Wed,Thu,Fri" : "Mon,Fri");
        f.ViewModel.QuietHoursStart.Should().Be(off ? "09:00" : "22:00");
        f.ViewModel.QuietHoursEnd.Should().Be(off ? "17:00" : "06:00");
        f.ViewModel.QuietHoursZone.Should().Be(off ? "UTC" : "Australia/Sydney");
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task CancelAndInvalidNativeFieldsNeverWriteAndStaleNativeConfirmationIsDisabled()
    {
        await using var quiet = new QuietHoursConfigurationServiceTests.Fixture();
        var f = new Fixture(quietHoursConfiguration: quiet.Service);
        var native = true;
        f.ViewModel.BindQuietHoursNativeLifetime(() => native);
        await f.ViewModel.ReviewQuietHoursCommand.ExecuteAsync();
        f.ViewModel.CanConfirmQuietHoursNative.Should().BeTrue();
        native = false;
        f.ViewModel.CanConfirmQuietHoursNative.Should().BeFalse();
        native = true;
        await f.ViewModel.CancelQuietHoursCommand.ExecuteAsync();
        f.ViewModel.Transcript.Should().Contain("cancelled");
        f.ViewModel.QuietHoursReviewText.Should().StartWith("No quiet-hours");
        await f.RunAsync("set notices.quiet-hours to Off");
        f.ViewModel.CanConfirmQuietHoursNative.Should().BeFalse();
        await f.RunAsync("cancel notices.quiet-hours");
        f.ViewModel.QuietHoursDays = f.ViewModel.QuietHoursDays;
        f.ViewModel.QuietHoursStart = f.ViewModel.QuietHoursStart;
        f.ViewModel.QuietHoursEnd = f.ViewModel.QuietHoursEnd;
        f.ViewModel.QuietHoursZone = f.ViewModel.QuietHoursZone;
        f.ViewModel.QuietHoursDays = "Mon,Fri";
        f.ViewModel.QuietHoursStart = "22:00";
        f.ViewModel.QuietHoursEnd = "06:00";
        f.ViewModel.QuietHoursZone = "missing-zone";
        await f.ViewModel.ReviewQuietHoursCommand.ExecuteAsync();
        quiet.Preferences.Writes.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateNativeReceiptCannotAdvertiseSuccessAfterOwnershipLossOrDisposal(bool dispose)
    {
        await using var quiet = new QuietHoursConfigurationServiceTests.Fixture();
        var f = new Fixture(quietHoursConfiguration: quiet.Service);
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        await f.RunAsync("set notices.quiet-hours to Off");
        quiet.Service.SpeechAdmissionChanged += () =>
        {
            if (quiet.Preferences.Writes == 1 && quiet.Service.Get().Effective is not null)
            {
                if (dispose) { f.ViewModel.Dispose(); }
                else { owner++; }
            }
        };
        await f.RunAsync("confirm notices.quiet-hours");
        quiet.Preferences.Value.Should().Be(QuietHoursSchedule.Off);
        if (!dispose) { f.ViewModel.Transcript.Should().Contain("not confirmed"); }
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task CallDriftAtQuietHoursRequestedAuditIsDeniedWithoutAWrite()
    {
        await using var quiet = new QuietHoursConfigurationServiceTests.Fixture();
        var f = new Fixture(quietHoursConfiguration: quiet.Service);
        await f.RunAsync("set notices.quiet-hours to Off");
        quiet.Audit.BeforeWrite = record =>
        {
            if (record.Outcome == Kora.Core.Auditing.SecurityAuditOutcome.Requested)
            { f.CallState.SetState(CallState.Active); }
        };
        await f.RunAsync("confirm notices.quiet-hours");
        quiet.Preferences.Writes.Should().Be(0);
        f.ViewModel.Transcript.Should().Contain("denied");
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData("hide-reopen")]
    [InlineData("owner")]
    [InlineData("lock")]
    [InlineData("edit")]
    [InlineData("unrelated")]
    [InlineData("typed-to-native")]
    public async Task QuietHoursStaleReviewCannotBeConfirmed(string change)
    {
        await using var quiet = new QuietHoursConfigurationServiceTests.Fixture();
        var f = new Fixture(quietHoursConfiguration: quiet.Service);
        long owner = 1;
        f.ViewModel.BindCallOwnershipGate(static () => true, () => owner);
        f.ViewModel.BindQuietHoursNativeLifetime(static () => true);
        if (change is "typed-to-native") { await f.RunAsync("set notices.quiet-hours to Off"); }
        else { await f.ViewModel.ReviewQuietHoursCommand.ExecuteAsync(); }
        switch (change)
        {
            case "hide-reopen":
                f.ViewModel.BindQuietHoursNativeLifetime(static () => false);
                f.ViewModel.BindQuietHoursNativeLifetime(static () => true);
                break;
            case "owner": owner++; break;
            case "lock": f.Session.IsUnlocked = false; break;
            case "edit": f.ViewModel.QuietHoursZone = "Australia/Sydney"; break;
            case "unrelated": await f.RunAsync("show settings"); break;
        }
        await f.ViewModel.ConfirmQuietHoursCommand.ExecuteAsync();
        quiet.Preferences.Writes.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task QuietHoursActivatedVoiceRequiresCurrentNameAndSeparateOriginalChannelConfirmation()
    {
        await using var quiet = new QuietHoursConfigurationServiceTests.Fixture();
        var f = new Fixture(quietHoursConfiguration: quiet.Service);
        await f.RaiseActivatedTranscriptAsync("Kora, set notices.quiet-hours to weekly Mon 22:00 06:00 UTC", 0.99f);
        quiet.Preferences.Writes.Should().Be(0);
        f.ViewModel.QuietHoursReviewText.Should().Contain("Mon 22:00 06:00 UTC");
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        await f.Voice.RaiseTranscriptAsync("Kora, confirm notices.quiet-hours", 0.99f);
        quiet.Preferences.Value.Should().Be(QuietHoursSchedule.Weekly("Mon", "22:00", "06:00", "UTC"), f.ViewModel.Transcript);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        await f.Voice.RaiseTranscriptAsync("reset notices.quiet-hours", 0.99f);
        await f.ViewModel.BeginPushToTalkAsync();
        await f.ViewModel.EndPushToTalkAsync();
        await f.Voice.RaiseTranscriptAsync("Kora, confirm notices.quiet-hours", 0.99f);
        quiet.Preferences.Writes.Should().Be(1);
        f.Reasoner.Requests.Should().BeEmpty();
        f.ViewModel.Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QuietHoursNeverHideOrAnswerMandatoryQuestionsAndApprovals(bool approval)
    {
        await using var quiet = new QuietHoursConfigurationServiceTests.Fixture();
        var f = new Fixture(quietHoursConfiguration: quiet.Service);
        f.Probe.Status = new("local.inference", "Local inference", DependencyReadiness.Ready, "fixture");
        if (approval) { f.Reasoner.Action = Kora.Core.Commands.BuiltInAction.EnableHostedModels; }
        else { f.Reasoner.Question = new("Which target?", ["One", "Two"]); }
        await f.ViewModel.InitializeAsync();
        await f.RunAsync("help me");
        await f.ViewModel.ActiveReasoningTask!;
        var body = f.ViewModel.ResponseBody;
        await f.RunAsync("set notices.quiet-hours to Off");
        await f.RunAsync("confirm notices.quiet-hours");
        f.ViewModel.IsModelActionApprovalPending.Should().Be(approval);
        f.ViewModel.IsModelQuestionPending.Should().Be(!approval);
        f.ViewModel.ResponseBody.Should().Be(body);
        quiet.Preferences.Writes.Should().Be(0);
        f.ViewModel.Dispose();
    }

    [Fact]
    public async Task QuietHoursUnavailableMalformedAndProtectedCallVoiceRemainExplicit()
    {
        var missing = new Fixture();
        missing.ViewModel.CanChangeQuietHoursNative.Should().BeFalse();
        await missing.RunAsync("get notices.quiet-hours");
        missing.ViewModel.Transcript.Should().Contain("requires");
        missing.ViewModel.QuietHoursStatus.Should().Contain("unavailable");
        missing.ViewModel.Dispose();
        await using var quiet = new QuietHoursConfigurationServiceTests.Fixture();
        var f = new Fixture(quietHoursConfiguration: quiet.Service);
        await f.RunAsync("set notices.quiet-hours to weekly Funday 09:00 17:00 UTC");
        quiet.Preferences.Writes.Should().Be(0);
        f.CallState.SetState(CallState.Active);
        await f.RaiseActivatedTranscriptAsync("Kora, set notices.quiet-hours to Off", 0.99f);
        quiet.Preferences.Writes.Should().Be(0);
        f.ViewModel.Dispose();
    }
}
