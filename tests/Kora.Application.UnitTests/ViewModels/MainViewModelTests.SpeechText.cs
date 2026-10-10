using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Voice;
using Kora.Core.Platform;
using Kora.Application.ViewModels;

using Microsoft.Extensions.Logging;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Fact]
    public async Task DisplayRecoveryPreservesUnconfirmedPreferencesAndRequiredPanels()
    {
        var fixture = new Fixture(enableSpeechText: true, captionReadFailure: new InvalidDataException());
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.ViewModel.PrepareGrantChange(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        var state = fixture.ViewModel.SpeechTextConfigurationStatus;
        fixture.ViewModel.ReportSpeechCaptionDisplayUnavailable();
        fixture.ViewModel.CanChooseSpeechCaptionDisplay.Should().BeFalse();
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.ViewModel.SpeechTextConfigurationStatus.Should().Be(state);
        var recoveryNotice = fixture.ViewModel.Transcript;
        fixture.ViewModel.ReportSpeechCaptionDisplayUnavailable(reportRecovery: false);
        fixture.ViewModel.Transcript.Should().Be(recoveryNotice);
        fixture.ViewModel.SpeechCaptionPlacement.Should().BeNull();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        var unavailable = new Fixture();
        await using var noConfiguration = unavailable.OutputAdmission;
        unavailable.ViewModel.CanChooseSpeechCaptionDisplay.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisplayRecoveryRetiresObservedPinnedSourceWithoutPreferenceRepairOrReplay(bool disposed)
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.CanChooseSpeechCaptionDisplay.Should().BeTrue();
        await fixture.RunAsync("set display.speech-text to CurrentUtterance");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = fixture.RunAsync("show your window");
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.TextToSpeech.PlaybackFrame = new(true, 0.5, fixture.TextToSpeech.CaptionPlaybackId, 4);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        await fixture.ViewModel.ToggleSpeechCaptionPinCommand.ExecuteAsync();
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeTrue();
        fixture.ViewModel.ReportSpeechCaptionDisplayUnavailable();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeFalse();
        fixture.ViewModel.Transcript.Should().Contain("explicitly choose");
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.TextToSpeech.SpeakGate.SetResult();
        await response;
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.CaptionPreferences.Mode.Should().Be(SpeechTextMode.CurrentUtterance);
        if (disposed) { fixture.ViewModel.Dispose(); }
        else { fixture.Session.IsUnlocked = false; }
        fixture.ViewModel.CanChooseSpeechCaptionDisplay.Should().BeFalse();
        fixture.ViewModel.ReportSpeechCaptionDisplayUnavailable();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    private sealed class CaptionPreferences : ISpeechTextPreferences
    {
        public SpeechCaptionOptions? Options { get; set; }
        public SpeechCaptionOptions? LoadOptions() => Pending ? throw new InvalidDataException() : ReadBackOptions();
        public SpeechCaptionOptions? ReadBackOptions() => Options;
        public void SaveOptions(SpeechCaptionOptions options)
        {
            AfterSave?.Invoke();
            if (Failure is { } failure) { throw failure; }
            Options = options;
        }
        public SpeechTextMode? Mode { get; set; }
        public bool Pending { get; set; }
        public Exception? Failure { get; set; }
        public Exception? LoadFailure { get; set; }
        public Action? AfterSave { get; set; }
        public SpeechTextMode? Load() => Pending ? throw new InvalidDataException() : ReadBack();
        public SpeechTextMode? ReadBack() { if (LoadFailure is { } failure) { throw failure; } return Mode; }
        public void BeginWrite() => Pending = true;
        public void Save(SpeechTextMode mode)
        {
            AfterSave?.Invoke();
            if (Failure is { } failure) { throw failure; }
            Mode = mode;
        }
        public void ConfirmWrite() => Pending = false;
    }

    [Fact]
    public async Task Corrupt_caption_preference_does_not_disable_speech_or_create_a_caption()
    {
        var fixture = new Fixture(enableSpeechText: true, captionReadFailure: new InvalidDataException());
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("show your window");
        fixture.ViewModel.SpeechTextConfigurationStatus.Should().Contain("\"available\":false");
        fixture.ViewModel.SpeechCaptionPlacement.Should().BeNull();
        fixture.TextToSpeech.SpokenText.Should().NotBeNull();
        fixture.TextToSpeech.CaptionPlaybackId.Should().BeNull();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
    }

    [Fact]
    public async Task Caption_write_failure_preserves_a_required_visual_grant_proposal()
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.CaptionPreferences.AfterSave = () =>
        {
            fixture.ViewModel.PrepareGrantChange(new(GrantChangeOperation.Add,
                BuiltInAction.LockMachine, ModelApprovalScope.Always));
        };
        fixture.CaptionPreferences.Failure = new IOException();
        await fixture.RunAsync("set display.speech-text to CurrentUtterance");
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.ViewModel.Transcript.Should().Contain("not confirmed");
        fixture.ViewModel.ResponseTitle.Should().NotBe("Speech-text preference not confirmed.");
    }

    [Fact]
    public async Task Missing_disposed_and_malformed_configuration_fail_without_replacing_required_panels()
    {
        var missing = new Fixture();
        await using var missingAdmission = missing.OutputAdmission;
        missing.ViewModel.SpeechTextChoices.Should().BeEmpty();
        missing.ViewModel.SpeechCaptionPlacement.Should().BeNull();
        missing.ViewModel.SpeechCaptionOptionChoices.Should().BeEmpty();
        missing.ViewModel.SpeechCaptionOptionsList.Should().Equal(SpeechCaptionOption.Placement, SpeechCaptionOption.DismissalDelay);
        missing.ViewModel.SpeechCaptionLabel.Should().Be("CURRENT PLAYBACK - UTTERANCE");
        missing.ViewModel.SpeechTextConfigurationStatus.Should().Contain("unavailable");
        await missing.ViewModel.ResetSpeechTextCommand.ExecuteAsync();
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set display.speech-text to bad");
        fixture.ViewModel.Transcript.Should().Contain(SpeechTextCommand.Syntax);
        await fixture.ViewModel.ExecuteSpeechTextCommandAsync(new(AppearanceCommandOperation.Reset),
            Kora.Core.Auditing.SecurityAuditInitiator.TypedCommand, cancellationToken: new CancellationToken(true));
        fixture.ViewModel.ResponseTitle.Should().Be("Speech-text preference not confirmed.");
        fixture.ViewModel.SpeechTextConfigurationStatus.Should().Contain("\"available\":false");
        fixture.ViewModel.Dispose();
        await fixture.ViewModel.ResetSpeechTextCommand.ExecuteAsync();
        fixture.ViewModel.CanChangeSpeechText.Should().BeFalse();
        fixture.ViewModel.SelectedSpeechTextChoice = null;
        fixture.ViewModel.ReportSpeechCaptionPresentationFailure("SyntheticNativeFailure");
    }

    [Theory]
    [InlineData("save")]
    [InlineData("disposed")]
    [InlineData("late-host")]
    [InlineData("late-complete")]
    [InlineData("presentation")]
    [InlineData("no-subscriber")]
    [InlineData("call-denied")]
    public async Task Caption_configuration_failures_and_late_privacy_are_visible_without_automatic_replay(string stage)
    {
        var fixture = new Fixture(enableSpeechText: true, subscribeToWindowActions: stage is not "no-subscriber");
        await using var admission = fixture.OutputAdmission;
        if (stage is "call-denied")
        {
            fixture.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
            fixture.Voice.DefaultMicrophoneId = "voice-fixture";
        }
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        if (stage is "save" or "disposed") { fixture.CaptionPreferences.Failure = new IOException(); }
        if (stage is "disposed") { fixture.CaptionPreferences.AfterSave = fixture.ViewModel.Dispose; }
        if (stage is "late-host" or "late-complete")
        {
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.SpeechTextChoices), StringComparison.Ordinal)
                    && (stage is "late-host" || fixture.ViewModel.SpeechTextConfigurationStatus.Contains("\"available\":true", StringComparison.Ordinal)))
                {
                    fixture.Session.IsUnlocked = false;
                }

            };
        }
        if (stage is "presentation")
        {
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (string.Equals(args.PropertyName, nameof(fixture.ViewModel.ResponseBody), StringComparison.Ordinal))
                {
                    fixture.Session.IsUnlocked = false;
                }
            };
        }
        if (stage is "call-denied")
        {
            fixture.CallState.SetState(CallState.Unknown);
            await fixture.Dispatcher.LastInvocation;
            await fixture.ViewModel.ExecuteSpeechTextCommandAsync(new(AppearanceCommandOperation.Reset),
                Kora.Core.Auditing.SecurityAuditInitiator.VoiceCommand, cancellationToken: TestContext.Current.CancellationToken);
            fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"denied\"");
        }
        else { await fixture.RunAsync(stage is "late-complete" ? "get display.speech-text" : "set display.speech-text to CurrentUtterance"); }
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
    }

    [Fact]
    public async Task Activated_caption_binds_original_capture_generation_and_does_not_reopen_capture()
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        fixture.Voice.DefaultMicrophoneId = "voice-fixture";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set display.speech-text to CurrentUtterance");
        await fixture.RaiseActivatedTranscriptAsync("get display.speech-text", 1);
        await fixture.ViewModel.BeginPushToTalkAsync();
        await fixture.ViewModel.EndPushToTalkAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpeakStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = fixture.Voice.RaiseTranscriptAsync("Kora, show your window", 1);
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.TextToSpeech.CaptionPlaybackId.Should().NotBeNull(fixture.ViewModel.SpeechTextConfigurationStatus);
        fixture.ViewModel.IsSpeaking.Should().BeTrue();
        fixture.ViewModel.IsSpeechResponseEnabled.Should().BeTrue(fixture.ViewModel.ResponseOutputStatus);
        var starts = fixture.Voice.StartCalls;
        fixture.TextToSpeech.PlaybackFrame = new(true, 0.5, fixture.TextToSpeech.CaptionPlaybackId, 2);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().NotBeNull(fixture.ViewModel.SpeechTextConfigurationStatus);
        fixture.ViewModel.IsSpeechCaptionVisible.Should().BeTrue();
        fixture.Voice.AdvanceGeneration();
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.Voice.StartCalls.Should().Be(starts);
        fixture.TextToSpeech.SpeakGate.SetResult();
        await response;
    }

    [Theory]
    [InlineData("visual-only")]
    [InlineData("call")]
    [InlineData("provider")]
    [InlineData("sentence-cap")]
    public async Task Suppressed_or_failed_response_keeps_the_answer_panel_without_a_fictitious_caption(string stage)
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set display.speech-text to CurrentUtterance");
        fixture.TextToSpeech.ClearSpokenResponse();
        if (stage is "visual-only") { fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VisualOnly; }
        if (stage is "call") { fixture.CallState.SetState(CallState.Unknown); await fixture.Dispatcher.LastInvocation; }
        if (stage is "provider") { fixture.TextToSpeech.SpeakException = new InvalidOperationException("Synthetic provider failure"); }
        await fixture.RunAsync(stage is "sentence-cap" ? "what version are you running" : "what power action is pending");
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.IsSpeechCaptionVisible.Should().BeFalse();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        fixture.Reasoner.Requests.Should().BeEmpty();
        if (stage is not "provider") { fixture.TextToSpeech.SpokenText.Should().BeNull(); }
    }

    [Fact]
    public async Task Native_typed_and_activated_configuration_never_speak_capture_or_reason()
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("Kora, list speech text settings");
        fixture.ViewModel.ResponseBody.Should().Contain("\"default\":\"Off\"").And.Contain("\"source\":\"default\"");
        await fixture.RunAsync("set display.speech-text to CurrentUtterance");
        fixture.CaptionPreferences.Mode.Should().Be(SpeechTextMode.CurrentUtterance);
        await fixture.RunAsync("status display.speech-text");
        fixture.ViewModel.ResponseBody.Should().Contain("\"effective\":\"CurrentUtterance\"");
        await fixture.ViewModel.RefreshSpeechTextCommand.ExecuteAsync();
        fixture.ViewModel.SelectedSpeechTextChoice = fixture.ViewModel.SpeechTextChoices.Single(choice => choice.Mode == SpeechTextMode.Off);
        await fixture.ViewModel.SaveSpeechTextCommand.ExecuteAsync();
        await fixture.ViewModel.ResetSpeechTextCommand.ExecuteAsync();
        await fixture.RaiseActivatedTranscriptAsync("Kora, set display.speech-text to CurrentUtterance", 1);
        fixture.CaptionPreferences.Mode.Should().Be(SpeechTextMode.CurrentUtterance);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.OutputPreferences.SavedMode.Should().BeNull();
        fixture.AudioPreferences.SavedOutputDeviceId.Should().BeNull();
        fixture.Voice.StartedPhrases.Should().Contain("get display.speech-text");
    }

    [Fact]
    public async Task Caption_option_native_typed_and_activated_routes_preserve_companions_and_never_enable_captions()
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        await fixture.RunAsync("set display.speech-text-placement to TopLeft");
        fixture.CaptionPreferences.Options.Should().Be(new SpeechCaptionOptions(SpeechCaptionPlacement.TopLeft, 5));
        fixture.ViewModel.SpeechCaptionPlacement.Should().Be(SpeechCaptionPlacement.TopLeft);
        await fixture.RunAsync("set display.speech-text-dismissal-delay to 30");
        await fixture.RunAsync("reset display.speech-text-placement");
        fixture.CaptionPreferences.Options.Should().Be(new SpeechCaptionOptions(SpeechCaptionPlacement.BottomRight, 30));
        await fixture.RaiseActivatedTranscriptAsync("Kora, reset display.speech-text-dismissal-delay", 1);
        fixture.CaptionPreferences.Options.Should().Be(SpeechCaptionOptions.Default);
        fixture.ViewModel.SelectedSpeechCaptionOption = SpeechCaptionOption.DismissalDelay;
        fixture.ViewModel.SelectedSpeechCaptionOption = SpeechCaptionOption.DismissalDelay;
        await fixture.ViewModel.RefreshSpeechTextCommand.ExecuteAsync();
        fixture.ViewModel.SelectedSpeechCaptionChoice = fixture.ViewModel.SpeechCaptionOptionChoices.Single(choice => choice.CaptionValue == new SpeechCaptionValue.Delay(0));
        await fixture.ViewModel.SaveSpeechCaptionOptionCommand.ExecuteAsync();
        fixture.CaptionPreferences.Options!.DismissalDelaySeconds.Should().Be(0);
        await fixture.ViewModel.ResetSpeechCaptionOptionCommand.ExecuteAsync();
        fixture.CaptionPreferences.Options!.DismissalDelaySeconds.Should().Be(5);
        await fixture.RunAsync("get display.speech-text-pin");
        fixture.ViewModel.Transcript.Should().Contain("run-only/current-caption");
        await fixture.RunAsync("set display.speech-text-pin to true");
        fixture.ViewModel.Transcript.Should().Contain("requires");
        fixture.CaptionPreferences.Mode.Should().BeNull();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Voice.StartedPhrases.Should().Contain("set display.speech-text-placement to TopLeft");
    }

    [Fact]
    public async Task Normal_completion_keeps_a_pinned_caption_but_stop_and_source_changes_still_retire_it()
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set display.speech-text to CurrentUtterance");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = fixture.RunAsync("show your window");
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.TextToSpeech.PlaybackFrame = new(true, 0.5, fixture.TextToSpeech.CaptionPlaybackId, 4);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        await fixture.ViewModel.ToggleSpeechCaptionPinCommand.ExecuteAsync();
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeTrue();
        fixture.ViewModel.SpeechCaptionPinLabel.Should().Be("Unpin");
        fixture.TextToSpeech.SpeakGate.SetResult();
        await response;
        fixture.ViewModel.IsSpeechCaptionVisible.Should().BeTrue();
        fixture.ViewModel.IsPreviousSpeechCaption.Should().BeTrue();
        fixture.ViewModel.SpeechCaptionLabel.Should().Be("PREVIOUS SPEECH");
        await fixture.RunAsync("reset display.speech-text-pin");
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeFalse();
        fixture.ViewModel.SpeechCaptionPinLabel.Should().Be("Pin");
        await fixture.RunAsync("set display.speech-text-pin to true");
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeTrue();
        await fixture.RunAsync("get display.speech-text");
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeFalse();
    }

    [Fact]
    public async Task Protected_call_original_voice_cannot_pin_and_remains_visual()
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.Dispatcher.LastInvocation;
        await fixture.ViewModel.ExecuteSpeechTextCommandAsync(
            new(AppearanceCommandOperation.Set, IsPinControl: true, PinValue: true),
            Kora.Core.Auditing.SecurityAuditInitiator.VoiceCommand, cancellationToken: TestContext.Current.CancellationToken);
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeFalse();
        fixture.ViewModel.Transcript.Should().Contain("requires");
    }

    [Fact]
    public async Task Actual_playback_only_exact_text_and_finish_remove_the_caption_without_duplicate_tts()
    {
        var log = new CaptionPresentationLogger();
        var fixture = new Fixture(enableSpeechText: true, logger: log);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set display.speech-text to CurrentUtterance");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = fixture.RunAsync("show your window");
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.TextToSpeech.CaptionPlaybackId.Should().NotBeNull();
        fixture.TextToSpeech.PlaybackFrame = new(true, 0.5, fixture.TextToSpeech.CaptionPlaybackId, 4);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().Be(fixture.TextToSpeech.SpokenText);
        fixture.ViewModel.IsSpeechCaptionVisible.Should().BeTrue();
        fixture.TextToSpeech.PlaybackFrame = SpeechPlaybackFrame.Inactive;
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.TextToSpeech.PlaybackFrame = new(true, 0.5, fixture.TextToSpeech.CaptionPlaybackId, 4);
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.TextToSpeech.SpeakGate.SetResult();
        await response;
        fixture.Reasoner.Requests.Should().BeEmpty();
        log.Messages.Should().NotContain(message => message.Contains(fixture.TextToSpeech.SpokenText!, StringComparison.Ordinal));
    }

    private sealed class CaptionPresentationLogger : ILogger<MainViewModel>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("cancel")]
    [InlineData("ownership")]
    [InlineData("call")]
    [InlineData("lock")]
    [InlineData("disconnected")]
    [InlineData("privacy")]
    [InlineData("topology")]
    [InlineData("segment")]
    [InlineData("dispose")]
    [InlineData("source")]
    [InlineData("configuration")]
    [InlineData("configuration-failure")]
    [InlineData("feedback")]
    [InlineData("stale")]
    [InlineData("failure")]
    [InlineData("native-presentation")]
    [InlineData("native-presentation-locked")]
    [InlineData("native-presentation-no-subscriber")]
    public async Task Caption_is_retired_on_every_applicable_transition_and_never_restored(string transition)
    {
        var log = new CaptionPresentationLogger();
        var fixture = new Fixture(enableSpeechText: true, logger: log,
            enableInCallFeedback: transition is "feedback",
            subscribeToWindowActions: transition is not "native-presentation-no-subscriber");
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set display.speech-text to CurrentUtterance");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = fixture.RunAsync("show your window");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var frame = new SpeechPlaybackFrame(true, 0.5, fixture.TextToSpeech.CaptionPlaybackId, 8);
        fixture.TextToSpeech.PlaybackFrame = frame;
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().NotBeNull();
        switch (transition)
        {
            case "stop": await fixture.ViewModel.StopSpeechCommand.ExecuteAsync(); break;
            case "cancel": await fixture.ViewModel.CancelCurrentTaskAsync(); break;
            case "ownership": fixture.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "call": fixture.CallState.SetState(CallState.Unknown); await fixture.Dispatcher.LastInvocation; break;
            case "lock": fixture.Session.IsUnlocked = false; break;
            case "disconnected":
                fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { SessionState = WindowsSessionState.Disconnected };
                break;
            case "privacy":
                fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { MicrophoneAccess = MicrophoneAccessState.Unknown };
                break;
            case "topology":
                fixture.PrivacyObservation.Current = fixture.PrivacyObservation.Current with { TopologyRevision = 2 };
                break;
            case "segment": fixture.TextToSpeech.PlaybackFrame = frame with { Segment = 1 }; break;
            case "dispose":
                fixture.ViewModel.Dispose();
                fixture.ViewModel.ReportSpeechCaptionPresentationFailure("SyntheticNativeFailure");
                break;
            case "source": fixture.ViewModel.ReportPresenceInputFailure("Synthetic failure"); break;
            case "configuration": await fixture.ViewModel.ResetSpeechTextCommand.ExecuteAsync(); break;
            case "feedback":
                fixture.FeedbackPreferences.Value = InCallFeedbackMode.Inherit;
                fixture.FeedbackConfiguration!.Observe();
                fixture.ViewModel.SpeechCaptionText.Should().BeNull();
                break;
            case "configuration-failure":
                fixture.CaptionPreferences.Failure = new IOException();
                await fixture.ViewModel.ResetSpeechTextCommand.ExecuteAsync();
                break;
            case "stale": fixture.TextToSpeech.PlaybackFrame = frame with { Generation = 9 }; break;
            case "failure": fixture.TextToSpeech.PlaybackFrameException = new("Synthetic output failure"); break;
            case "native-presentation":
                var recoveryRequested = false;
                fixture.ViewModel.WindowActionRequested += (_, _) => recoveryRequested = true;
                fixture.ViewModel.ReportSpeechCaptionPresentationFailure("SyntheticNativeFailure");
                recoveryRequested.Should().BeTrue();
                fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
                break;
            case "native-presentation-locked":
                fixture.Session.IsUnlocked = false;
                fixture.ViewModel.ReportSpeechCaptionPresentationFailure("SyntheticNativeFailure");
                break;
            case "native-presentation-no-subscriber":
                fixture.ViewModel.ReportSpeechCaptionPresentationFailure("SyntheticNativeFailure");
                fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
                break;
        }
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.ViewModel.IsSpeechCaptionVisible.Should().BeFalse();
        fixture.TextToSpeech.PlaybackFrameException = null;
        fixture.ViewModel.BindCallOwnershipGate(static () => true);
        fixture.Session.IsUnlocked = true;
        fixture.TextToSpeech.PlaybackFrame = frame;
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.TextToSpeech.SpeakGate.TrySetResult();
        await response;
        log.Messages.Should().NotContain(message => message.Contains(fixture.TextToSpeech.SpokenText!, StringComparison.Ordinal));
    }
}