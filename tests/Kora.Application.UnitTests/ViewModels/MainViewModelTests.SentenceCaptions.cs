using AwesomeAssertions;
using Kora.Core;
using Kora.Core.Auditing;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sentence_visual_fallback_revalidates_reentrant_privacy_before_starting_speech(bool afterShow)
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set display.speech-text to CurrentSentence");
        fixture.TextToSpeech.ClearSpokenResponse();
        void Revoke()
        {
            if (fixture.ViewModel.IsSpeaking) { fixture.Session.IsUnlocked = false; }
        }
        if (afterShow) { fixture.ViewModel.WindowActionRequested += (_, _) => Revoke(); }
        else
        {
            fixture.ViewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(fixture.ViewModel.IsVisualResponseVisible)) { Revoke(); }
            };
        }
        await fixture.RunAsync("show your window");
        fixture.Session.IsUnlocked.Should().BeFalse();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.TextToSpeech.CaptionPlaybackId.Should().BeNull();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.ViewModel.IsSpeaking.Should().BeFalse();
    }

    [Theory]
    [InlineData("typed")]
    [InlineData("native")]
    [InlineData("activated")]
    public async Task Sentence_controls_share_audited_preferences_without_starting_speech(string route)
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        if (route is "native")
        {
            await fixture.ViewModel.RefreshSpeechTextCommand.ExecuteAsync();
            fixture.ViewModel.SelectedSpeechTextChoice = fixture.ViewModel.SpeechTextChoices
                .Single(choice => choice.Mode == SpeechTextMode.CurrentSentence);
            await fixture.ViewModel.SaveSpeechTextCommand.ExecuteAsync();
        }
        else if (route is "activated")
        {
            await fixture.RaiseActivatedTranscriptAsync("Kora, set display.speech-text to CurrentSentence", 1);
        }
        else { await fixture.RunAsync("set display.speech-text to CurrentSentence"); }
        fixture.CaptionPreferences.Mode.Should().Be(SpeechTextMode.CurrentSentence);
        fixture.ViewModel.SpeechTextConfigurationStatus.Should().Contain("unavailable");
        if (route is "activated")
        {
            fixture.Voice.StartedPhrases.Should().Contain("set display.speech-text to CurrentSentence");
        }
        else { fixture.Voice.StartedPhrases.Should().BeEmpty("saving a typed/native preference does not open capture"); }
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, "configuration.speech-text", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        await fixture.RunAsync("status display.speech-text");
        fixture.ViewModel.ResponseBody.Should().Contain("\"effective\":\"CurrentSentence\"").And.Contain("unavailable");
        await fixture.RunAsync("reset display.speech-text");
        fixture.CaptionPreferences.Mode.Should().Be(SpeechTextMode.Off);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admitted_speech_consumes_matching_sentence_frames_and_preserves_complete_visual_answer(bool unavailable)
    {
        var fixture = new Fixture(subscribeToWindowActions: !unavailable, enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.DefaultResponseMode = ResponseOutputMode.VoiceOnly;
        await fixture.RunAsync("set display.speech-text to CurrentSentence");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = fixture.TextToSpeech.SpeakCalls;
        var response = fixture.RunAsync("what power action is pending");
        await Task.WhenAny(fixture.TextToSpeech.SpeakStarted.Task, response);
        fixture.TextToSpeech.SpeakStarted.Task.IsCompleted.Should().BeTrue(fixture.ViewModel.ResponseOutputStatus);
        var spoken = fixture.TextToSpeech.SpokenText!;
        var map = SpeechSentenceMap.Create(spoken)!;
        map.Sentences.Should().HaveCountGreaterThan(1);
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        var body = fixture.ViewModel.ResponseBody;
        body.Should().NotBeNullOrWhiteSpace();
        fixture.ViewModel.IsVisualResponseVisible.Should().BeTrue();
        var id = fixture.TextToSpeech.CaptionPlaybackId;
        var frame = new SpeechPlaybackFrame(true, 0.5, id, 4, SentenceAlignmentAvailable: !unavailable);
        fixture.TextToSpeech.PlaybackFrame = frame;
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        var first = map.Sentences[0];
        frame = frame with { Sentence = new(new(new(id!.Value, 4), first.Start, first.Length, TimeSpan.Zero),
            TimeSpan.Zero, TimeSpan.FromSeconds(9)) };
        fixture.TextToSpeech.PlaybackFrame = frame;
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().Be(unavailable ? null : map.GetText(first));
        fixture.ViewModel.SpeechCaptionLabel.Should().Be("CURRENT PLAYBACK - SENTENCE");
        var next = map.Sentences[1];
        fixture.TextToSpeech.PlaybackFrame = frame with
        {
            Sentence = new(new(new(id.Value, 4), next.Start, next.Length, TimeSpan.FromSeconds(2)),
                TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(9)),
        };
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().Be(unavailable ? null : map.GetText(next));
        fixture.TextToSpeech.SpeakGate.SetResult();
        await response;
        fixture.ViewModel.SpeechCaptionText.Should().Be(unavailable ? null : map.GetText(next));
        fixture.ViewModel.ResponseBody.Should().Be(body);
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.TextToSpeech.SpeakCalls.Should().Be(calls + 1);
        await fixture.RunAsync("reset display.speech-text");
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
    }
}
