using System.Text.Json;

using AwesomeAssertions;

using Kora.Application.Configuration;
using Kora.Core.Auditing;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("Kora, turn on speech captions")]
    [InlineData("Kora, set display.speech-text to CurrentUtterance")]
    [InlineData("Kora, pin current speech caption")]
    public async Task NaturalCaptionUnconfirmedActivationNameRefusesPrefixedControl(string command)
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        fixture.NamePreferences.Name = "Supported Commands";
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync(command);
        fixture.ViewModel.Transcript.Should().Contain("prefix routing is unavailable");
        fixture.CaptionPreferences.Mode.Should().BeNull();
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeFalse();
        await fixture.RunAsync("inspect speech caption settings");
        fixture.ViewModel.ResponseBody.Should().Contain("\"source\":\"default\"");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NaturalCaptionTypedAndActivatedRoutesSaveThroughExistingServiceAndAudit(bool voice)
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        var outputMode = fixture.ViewModel.DefaultResponseMode;
        await Run("list speech caption settings");
        using (var settings = JsonDocument.Parse(fixture.ViewModel.ResponseBody))
        {
            settings.RootElement.GetProperty("syntax").GetString().Should().Be(SpeechTextCommand.Syntax);
        }
        await Run("show the words you're saying");
        fixture.CaptionPreferences.Mode.Should().Be(SpeechTextMode.CurrentUtterance);
        await Run("turn on speech captions");
        fixture.CaptionPreferences.Mode.Should().Be(SpeechTextMode.CurrentUtterance);
        foreach (var (corner, placement) in new[]
        {
            ("bottom-left", SpeechCaptionPlacement.BottomLeft), ("top-left", SpeechCaptionPlacement.TopLeft),
            ("top-right", SpeechCaptionPlacement.TopRight), ("bottom-right", SpeechCaptionPlacement.BottomRight),
        })
        {
            await Run("put speech captions in the " + corner);
            fixture.CaptionPreferences.Options!.Placement.Should().Be(placement);
            fixture.CaptionPreferences.Mode.Should().Be(SpeechTextMode.CurrentUtterance);
        }
        await Run("keep speech text visible for 0 seconds");
        fixture.CaptionPreferences.Options!.DismissalDelaySeconds.Should().Be(0);
        await Run("keep speech text visible for 30 seconds");
        fixture.CaptionPreferences.Options!.DismissalDelaySeconds.Should().Be(30);
        await Run("hide speech text");
        fixture.CaptionPreferences.Mode.Should().Be(SpeechTextMode.Off);
        fixture.CaptionPreferences.Options!.DismissalDelaySeconds.Should().Be(30);
        await Run("inspect speech caption settings");
        fixture.ViewModel.ResponseBody.Should().Contain("\"source\":\"saved\"");
        await Run("reset speech caption dismissal delay");
        await Run("reset speech caption placement");
        fixture.CaptionPreferences.Options.Should().Be(SpeechCaptionOptions.Default);
        await Run("reset speech captions");
        fixture.CaptionPreferences.Mode.Should().Be(SpeechTextMode.Off);
        fixture.ViewModel.DefaultResponseMode.Should().Be(outputMode);
        fixture.OutputPreferences.SavedMode.Should().BeNull();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        var writes = fixture.Audit.Events.Where(item => item.ActionId.StartsWith("configuration.speech-", StringComparison.Ordinal)).ToArray();
        writes.Should().HaveCount(24);
        writes.Chunk(2).Should().OnlyContain(pair => pair[0].Outcome == SecurityAuditOutcome.Requested
            && pair[1].Outcome == SecurityAuditOutcome.Succeeded && pair[0].CorrelationId == pair[1].CorrelationId);
        writes.Should().OnlyContain(item => item.Initiator == (voice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.TypedCommand));
        if (voice)
        {
            fixture.Voice.StartedPhrases.Should().Contain(SpeechTextCommand.FixedPhrases);
            fixture.Voice.StartedPhrases.Should().Contain("Kora pin current speech caption");
            fixture.ViewModel.IsVoiceEnabled.Should().BeTrue();
        }

        Task Run(string command) => voice
            ? fixture.RaiseActivatedTranscriptAsync("Kora, " + command, 1)
            : fixture.RunAsync("Kora, " + command);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("invalid")]
    [InlineData("pending")]
    [InlineData("save")]
    [InlineData("late-host")]
    [InlineData("cancel")]
    public async Task NaturalCaptionUnavailableOrUnconfirmedPreferencesNeverEnableOrReplay(string stage)
    {
        var fixture = new Fixture(enableSpeechText: stage is not "missing");
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        if (stage is "invalid") { fixture.CaptionPreferences.Mode = (SpeechTextMode)99; }
        if (stage is "pending") { fixture.CaptionPreferences.Pending = true; }
        if (stage is "save") { fixture.CaptionPreferences.Failure = new IOException(); }
        if (stage is "late-host") { fixture.CaptionPreferences.AfterSave = () => fixture.Session.IsUnlocked = false; }
        if (stage is "cancel")
        {
            await fixture.ViewModel.ExecuteSpeechTextCommandAsync(SpeechTextCommand.Parse("turn on speech captions", "Kora")!,
                SecurityAuditInitiator.TypedCommand, cancellationToken: new CancellationToken(true));
        }
        else { await fixture.RunAsync("turn on speech captions"); }
        fixture.ViewModel.SpeechTextConfigurationStatus.Should().Contain(stage is "missing" ? "unavailable" : "Setting unavailable");
        fixture.ViewModel.SelectedSpeechTextChoice.Should().BeNull();
        fixture.ViewModel.SpeechCaptionPlacement.Should().BeNull();
        fixture.ViewModel.IsSpeechCaptionVisible.Should().BeFalse();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        if (stage is "save")
        {
            fixture.ViewModel.Transcript.Should().Contain("not confirmed");
            fixture.Audit.Events.Where(item => string.Equals(item.ActionId, "configuration.speech-text", StringComparison.Ordinal)).Select(item => item.Outcome)
                .Should().Equal(SecurityAuditOutcome.Requested, SecurityAuditOutcome.Failed);
        }
    }

    [Fact]
    public async Task NaturalCaptionMalformedReservedInputCannotAnswerOrReplaceApproval()
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("hide speech text once");
        fixture.ViewModel.Transcript.Should().Be(SpeechTextCommand.Syntax);
        await fixture.ViewModel.PrepareGrantChangeAsync(new(GrantChangeOperation.Add, BuiltInAction.LockMachine, ModelApprovalScope.Always));
        var title = fixture.ViewModel.ResponseTitle;
        var body = fixture.ViewModel.ResponseBody;
        await fixture.RunAsync("turn on speech captions please");
        fixture.ViewModel.IsResponseInteractionPending.Should().BeTrue();
        fixture.ViewModel.ResponseTitle.Should().Be(title);
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.CaptionPreferences.Mode.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("off")]
    [InlineData("cancel")]
    [InlineData("source")]
    [InlineData("ownership")]
    [InlineData("privacy")]
    [InlineData("call")]
    public async Task NaturalCaptionPinUsesOnlyCurrentObservedIdentityAndNeverResurrectsRetiredText(string transition)
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("pin current speech caption");
        fixture.ViewModel.Transcript.Should().Contain("requires an eligible");
        await fixture.RunAsync("show speech captions");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = fixture.RunAsync("show your window");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var frame = new SpeechPlaybackFrame(true, 0.5, fixture.TextToSpeech.CaptionPlaybackId, 4);
        fixture.TextToSpeech.PlaybackFrame = frame;
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        var exactText = fixture.ViewModel.SpeechCaptionText;
        exactText.Should().NotBeNull();
        await fixture.ViewModel.ExecuteSpeechTextCommandAsync(SpeechTextCommand.Parse("pin current speech caption", "Kora")!,
            SecurityAuditInitiator.TypedCommand, cancellationToken: TestContext.Current.CancellationToken);
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeTrue();
        fixture.TextToSpeech.SpeakGate.SetResult();
        await response;
        fixture.ViewModel.IsPreviousSpeechCaption.Should().BeTrue();
        await fixture.RunAsync("unpin current speech caption");
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeFalse();
        await fixture.RunAsync("pin current speech caption");
        await fixture.RunAsync("pin current speech caption");
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeTrue();
        fixture.ViewModel.SpeechCaptionText.Should().Be(exactText);
        switch (transition)
        {
            case "off": await fixture.RunAsync("turn off speech captions"); break;
            case "cancel": await fixture.ViewModel.CancelCurrentTaskAsync(); break;
            case "source": fixture.ViewModel.ReportSpeechCaptionDisplayUnavailable(); break;
            case "ownership": fixture.ViewModel.BindCallOwnershipGate(static () => false); break;
            case "privacy": fixture.Session.IsUnlocked = false; break;
            case "call": fixture.CallState.SetState(CallState.Unknown); await fixture.Dispatcher.LastInvocation; break;
        }
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeFalse();
        fixture.ViewModel.BindCallOwnershipGate(static () => true);
        fixture.Session.IsUnlocked = true;
        fixture.TextToSpeech.PlaybackFrame = frame;
        await fixture.RunAsync("pin current speech caption");
        await fixture.RunAsync("unpin current speech caption");
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeFalse();
        fixture.ViewModel.Transcript.Should().Contain("requires an eligible");
        fixture.Reasoner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task NaturalCaptionVoiceMutationPreservesProtectedCallOriginalOrigin()
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        fixture.Voice.Microphones = [new MicrophoneDevice("voice-fixture", "Synthetic microphone")];
        fixture.Voice.DefaultMicrophoneId = "voice-fixture";
        await fixture.ViewModel.InitializeAsync();
        fixture.CallState.SetState(CallState.Unknown);
        await fixture.Dispatcher.LastInvocation;
        await fixture.ViewModel.ExecuteSpeechTextCommandAsync(SpeechTextCommand.Parse("show speech text", "Kora")!,
            SecurityAuditInitiator.VoiceCommand, cancellationToken: TestContext.Current.CancellationToken);
        fixture.CaptionPreferences.Mode.Should().BeNull();
        fixture.ViewModel.ResponseBody.Should().Contain("\"outcome\":\"denied\"");
        fixture.Audit.Events.Last(item => string.Equals(item.ActionId, "configuration.speech-text", StringComparison.Ordinal)).Initiator.Should().Be(SecurityAuditInitiator.VoiceCommand);
        await fixture.ViewModel.ExecuteSpeechTextCommandAsync(SpeechTextCommand.Parse("pin current speech caption", "Kora")!,
            SecurityAuditInitiator.VoiceCommand, cancellationToken: TestContext.Current.CancellationToken);
        fixture.ViewModel.IsSpeechCaptionPinned.Should().BeFalse();
        fixture.ViewModel.Transcript.Should().Contain("requires an eligible");
    }
}
