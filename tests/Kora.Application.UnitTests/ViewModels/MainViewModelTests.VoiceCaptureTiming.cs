using AwesomeAssertions;
using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Voice;
using Kora.Core.Hosting;
using Kora.Core.Diagnostics;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task CaptureTimingNativeAndExactTypedReviewShareConfirmedPreferenceWithoutOpeningAudioOrModel()
    {
        var f = new Fixture(enableSpeechInput: true, enableCaptureTiming: true);
        await using var configuration = f.InputConfiguration!;
        await using var admission = f.OutputAdmission;
        using var model = f.ViewModel;
        await model.InitializeAsync();
        var starts = f.Voice.StartCalls;
        model.VoiceTimingStatus.Should().Contain("Default (not saved)").And.Contain("5 s").And.Contain("15 s");
        model.SelectedVoiceTimingOption = VoiceCaptureTiming.CommandStartId;
        model.VoiceTimingSeconds = "7";
        model.VoiceTimingOptions.Should().Contain(model.SelectedVoiceTimingOption);
        await model.ReviewVoiceTimingCommand.ExecuteAsync();
        model.CanConfirmVoiceTiming.Should().BeTrue();
        model.VoiceTimingReviewStatus.Should().Contain("7 s");
        f.CaptureTimingPreferences.Writes.Should().Be(0);
        await model.ConfirmVoiceTimingCommand.ExecuteAsync();
        f.CaptureTimingPreferences.Value.Should().Be(new VoiceCaptureTiming(7, 15, 1));
        model.HasVoiceTimingReview.Should().BeFalse();
        await f.RunAsync("set voice.reply-start-wait to 20");
        await f.RunAsync("confirm voice.timing");
        await f.RunAsync("Kora, set voice.trailing-silence to 2");
        await f.RunAsync("confirm voice.timing");
        f.CaptureTimingPreferences.Value.Should().Be(new VoiceCaptureTiming(7, 20, 2));
        await f.RunAsync("get voice.timing");
        model.ResponseBody.Should().Contain("Saved:").And.Contain("20 s");
        await f.RunAsync("reset voice.command-start-wait");
        await model.CancelVoiceTimingCommand.ExecuteAsync();
        f.CaptureTimingPreferences.Value.Should().Be(new VoiceCaptureTiming(7, 20, 2));
        await f.RunAsync("reset voice.command-start-wait");
        await f.RunAsync("confirm voice.timing");
        f.CaptureTimingPreferences.Value.Should().Be(new VoiceCaptureTiming(5, 20, 2));
        f.Voice.StartCalls.Should().Be(starts);
        f.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("owner")]
    [InlineData("native")]
    [InlineData("invalid")]
    [InlineData("call")]
    [InlineData("failed")]
    public async Task CaptureTimingCancelledStaleDeniedAndFailedChangesNeverClaimSuccess(string boundary)
    {
        var f = new Fixture(enableSpeechInput: true, enableCaptureTiming: true);
        await using var configuration = f.InputConfiguration!;
        await using var admission = f.OutputAdmission;
        using var model = f.ViewModel;
        await model.InitializeAsync();
        await f.RunAsync("set voice.trailing-silence to 2");
        if (boundary is "cancel") { await f.RunAsync("cancel voice.timing"); }
        if (boundary is "owner") { model.BindCallOwnershipGate(static () => false); }
        if (boundary is "native") { model.BindSpeechInputNativeLifetime(static () => false); }
        if (boundary is "invalid") { await f.RunAsync("set voice.trailing-silence to 4"); await f.RunAsync("cancel voice.timing"); }
        if (boundary is "call") { f.CallState.SetState(Kora.Core.Communication.CallState.Active); }
        if (boundary is "failed") { f.CaptureTimingPreferences.SaveFailure = new IOException(); }
        await model.ConfirmVoiceTimingCommand.ExecuteAsync();
        f.CaptureTimingPreferences.Writes.Should().Be(0);
        model.CanReviewVoiceTiming.Should().Be(boundary is "cancel" or "invalid" or "call");
        if (boundary is "failed") { model.VoiceTimingStatus.Should().Contain("unavailable"); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureTimingUnavailableOrUnknownProfileRemainsTruthful(bool configured)
    {
        var f = new Fixture(enableCaptureTiming: configured);
        await using var admission = f.OutputAdmission;
        using var model = f.ViewModel;
        await model.InitializeAsync();
        model.BindSpeechInputNativeLifetime(static () => true);
        model.VoiceTimingStatus.Should().Contain(configured ? "Effective capture unavailable" : "Capture timing unavailable");
        model.CanReviewVoiceTiming.Should().Be(configured);
        model.CanConfirmVoiceTiming.Should().BeFalse();
        model.VoiceTimingReviewStatus.Should().Contain("No capture timing");
        await f.RunAsync("get voice.timing");
        f.CaptureTimingPreferences.Writes.Should().Be(0);
        if (configured)
        {
            f.CaptureTimingPreferences.LoadFailure = new InvalidDataException();
            f.CaptureTimingConfiguration!.Reload();
            model.CanReviewVoiceTiming.Should().BeFalse();
            await f.RunAsync("get voice.timing");
            model.VoiceTimingStatus.Should().Contain("Capture timing unavailable");
        }
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("option")]
    [InlineData("name")]
    [InlineData("consent")]
    [InlineData("device")]
    [InlineData("owner-revision")]
    [InlineData("native-rebind")]
    [InlineData("disposed")]
    public async Task CaptureTimingReviewCannotSurviveOriginalInputOrOwnershipChanges(string boundary)
    {
        var f = new Fixture(enableSpeechInput: true, enableCaptureTiming: true);
        await using var configuration = f.InputConfiguration!;
        await using var admission = f.OutputAdmission;
        using var model = f.ViewModel;
        if (boundary is "device") { f.Voice.Microphones = [new("timing-microphone", "Timing microphone")]; }
        await model.InitializeAsync();
        var owner = 0L;
        model.BindCallOwnershipGate(static () => true, () => owner);
        await model.ReviewVoiceTimingCommand.ExecuteAsync();
        model.SelectedVoiceTimingOption = model.SelectedVoiceTimingOption;
        model.VoiceTimingSeconds = model.VoiceTimingSeconds;
        switch (boundary)
        {
            case "draft": model.VoiceTimingSeconds = "8"; break;
            case "option": model.SelectedVoiceTimingOption = VoiceCaptureTiming.ReplyStartId; break;
            case "name": await model.SetAssistantNameAsync("Nova"); break;
            case "consent": await model.SetVoiceConsentAsync(true); break;
            case "device": model.SelectedMicrophone = model.Microphones.First(device => device != model.SelectedMicrophone); break;
            case "owner-revision": owner++; break;
            case "native-rebind": model.BindSpeechInputNativeLifetime(static () => true); break;
            case "disposed": model.Dispose(); break;
        }
        model.CanConfirmVoiceTiming.Should().BeFalse();
        await model.ConfirmVoiceTimingCommand.ExecuteAsync();
        f.CaptureTimingPreferences.Writes.Should().Be(0);
    }

    [Fact]
    public async Task CaptureTimingActivatedReviewRequiresOriginalVoiceConfirmationAndDeniesProtectedCalls()
    {
        var f = new Fixture(enableSpeechInput: true, enableCaptureTiming: true);
        await using var configuration = f.InputConfiguration!;
        await using var admission = f.OutputAdmission;
        using var model = f.ViewModel;
        await model.InitializeAsync();
        await f.RaiseActivatedTranscriptAsync("Kora, set voice.command-start-wait to 7", 1);
        model.HasVoiceTimingReview.Should().BeTrue();
        await model.ConfirmVoiceTimingCommand.ExecuteAsync();
        f.CaptureTimingPreferences.Writes.Should().Be(0);
        await f.RaiseActivatedTranscriptAsync("Kora, set voice.reply-start-wait to 20", 2);
        using (var confirmation = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Request))
        {
            await model.ExecuteVoiceCaptureTimingAsync(new(AppearanceCommandOperation.Set, ReviewDecision: true), SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
            confirmation.Complete(HostOperationOutcome.Completed);
        }
        f.CaptureTimingPreferences.Value.Should().Be(new VoiceCaptureTiming(5, 20, 1));
        f.CallState.SetState(Kora.Core.Communication.CallState.Active);
        using var voice = HostActivity.BeginRoot(HostRequest.Create(RequestOrigin.ActivatedVoice), HostActivityLayer.Application, HostOperation.Request);
        await model.ExecuteVoiceCaptureTimingAsync(new(AppearanceCommandOperation.Set, VoiceCaptureTiming.TrailingSilenceId, "2"), SecurityAuditInitiator.VoiceCommand, TestContext.Current.CancellationToken);
        f.CaptureTimingPreferences.Writes.Should().Be(1);
        voice.Complete(HostOperationOutcome.Completed);
    }

    [Fact]
    public async Task CaptureTimingMalformedCommandsAndReentrantInputsHaveNoExtraWrite()
    {
        var f = new Fixture(enableSpeechInput: true, enableCaptureTiming: true);
        await using var configuration = f.InputConfiguration!;
        await using var admission = f.OutputAdmission;
        using var model = f.ViewModel;
        await model.InitializeAsync();
        await f.RunAsync("set voice.timing to 7");
        model.ResponseTitle.Should().Contain("Clarify");
        await model.ExecuteVoiceCaptureTimingAsync(new(AppearanceCommandOperation.Set), SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        await model.ExecuteVoiceCaptureTimingAsync(new(AppearanceCommandOperation.Set, VoiceCaptureTiming.CommandStartId), SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        await model.ReviewVoiceTimingCommand.ExecuteAsync();
        Task? reentrant = null;
        f.Audit.BeforeWrite = item =>
        {
            if (!string.Equals(item.ActionId, "configuration.voice-capture-timing", StringComparison.Ordinal) || item.Outcome != SecurityAuditOutcome.Requested) { return; }
            model.CanReviewVoiceTiming.Should().BeFalse();
            model.CanConfirmVoiceTiming.Should().BeFalse();
            reentrant = model.ExecuteVoiceCaptureTimingAsync(new(AppearanceCommandOperation.Get), SecurityAuditInitiator.TypedCommand, TestContext.Current.CancellationToken);
        };
        await model.ConfirmVoiceTimingCommand.ExecuteAsync();
        await reentrant!;
        f.CaptureTimingPreferences.Writes.Should().Be(1);
    }

    [Fact]
    public async Task CaptureTimingRevocationAtFinalPolicyCommitProducesDenialWithoutPersisting()
    {
        var f = new Fixture(enableSpeechInput: true, enableCaptureTiming: true);
        await using var configuration = f.InputConfiguration!;
        await using var admission = f.OutputAdmission;
        using var model = f.ViewModel;
        await model.InitializeAsync();
        var allowed = true;
        model.BindCallOwnershipGate(() => allowed);
        await model.ReviewVoiceTimingCommand.ExecuteAsync();
        f.Audit.BeforeWrite = item =>
        {
            if (string.Equals(item.ActionId, "configuration.voice-capture-timing", StringComparison.Ordinal) && item.Outcome == SecurityAuditOutcome.Requested)
            { allowed = false; }
        };
        f.ManualCallStore.AfterOperation = () => allowed = true;
        await model.ConfirmVoiceTimingCommand.ExecuteAsync();
        model.ResponseTitle.Should().Be("Capture timing denied.");
        f.CaptureTimingPreferences.Writes.Should().Be(0);
        f.CaptureTimingConfiguration!.GetTiming().Should().Be(VoiceCaptureTiming.Default);
    }

    [Fact]
    public async Task CaptureTimingCannotUseAnUnavailableAssistantPrefix()
    {
        var f = new Fixture(enableCaptureTiming: true);
        await using var admission = f.OutputAdmission;
        using var model = f.ViewModel;
        f.NamePreferences.Name = "Supported Commands";
        await model.InitializeAsync();
        await f.RunAsync("Kora, set voice.command-start-wait to 7");
        model.Transcript.Should().Contain("Assistant prefix routing is unavailable");
        f.CaptureTimingPreferences.Writes.Should().Be(0);
        await f.RunAsync("get voice.timing");
        model.ResponseTitle.Should().Be("Capture timing.");
    }
}
