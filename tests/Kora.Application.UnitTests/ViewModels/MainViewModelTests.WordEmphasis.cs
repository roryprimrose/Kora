using AwesomeAssertions;

using Kora.Core.Auditing;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData("typed")]
    [InlineData("native")]
    [InlineData("activated")]
    public async Task Word_emphasis_setting_uses_shared_reviewed_choices_without_enabling_captions_or_output(string route)
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        fixture.TextToSpeech.ClearSpokenResponse();
        if (route is "native")
        {
            await fixture.ViewModel.RefreshSpeechTextCommand.ExecuteAsync();
            fixture.ViewModel.SelectedSpeechCaptionOption = SpeechCaptionOption.WordEmphasis;
            fixture.ViewModel.SelectedSpeechCaptionChoice = fixture.ViewModel.SpeechCaptionOptionChoices
                .Single(choice => choice.CaptionValue == new SpeechCaptionValue.WordEmphasis(true));
            await fixture.ViewModel.SaveSpeechCaptionOptionCommand.ExecuteAsync();
        }
        else if (route is "activated")
        {
            await fixture.RaiseActivatedTranscriptAsync("Kora, set display.speech-text-word-emphasis to true", 1);
        }
        else { await fixture.RunAsync("set display.speech-text-word-emphasis to true"); }
        fixture.CaptionPreferences.Options.Should().Be(new SpeechCaptionOptions(SpeechCaptionPlacement.BottomRight, 5, true));
        fixture.CaptionPreferences.Mode.Should().BeNull();
        fixture.ViewModel.SpeechTextConfigurationStatus.Should().Contain("saved: on").And.Contain("effective preference: on")
            .And.Contain("unavailable");
        fixture.TextToSpeech.SpokenText.Should().BeNull();
        fixture.Reasoner.Requests.Should().BeEmpty();
        fixture.ViewModel.SpeechCaptionEmphasis.Should().BeNull();
        fixture.Audit.Events.Should().Contain(item => string.Equals(item.ActionId, "configuration.speech-caption-word-emphasis", StringComparison.Ordinal)
            && item.Outcome == SecurityAuditOutcome.Succeeded);
        await fixture.RunAsync("get display.speech-text-word-emphasis");
        fixture.ViewModel.ResponseBody.Should().Contain("\"saved\":\"true\"").And.Contain("\"effective\":\"true\"")
            .And.Contain("not alignment readiness");
        fixture.ViewModel.SelectedSpeechCaptionOption = SpeechCaptionOption.WordEmphasis;
        await fixture.ViewModel.ResetSpeechCaptionOptionCommand.ExecuteAsync();
        fixture.CaptionPreferences.Options.Should().Be(SpeechCaptionOptions.Default);
        fixture.CaptionPreferences.Mode.Should().BeNull();
        fixture.TextToSpeech.SpokenText.Should().BeNull();
    }

    [Fact]
    public async Task Word_emphasis_travels_through_admitted_output_and_completion_keeps_only_plain_previous_speech()
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set display.speech-text to CurrentUtterance");
        await fixture.RunAsync("set display.speech-text-word-emphasis to true");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = fixture.TextToSpeech.SpeakCalls;
        var response = fixture.RunAsync("show your window");
        await fixture.TextToSpeech.SpeakStarted.Task;
        fixture.ViewModel.SpeechCaptionText.Should().BeNull();
        fixture.ViewModel.SpeechCaptionEmphasis.Should().BeNull();
        var id = fixture.TextToSpeech.CaptionPlaybackId!.Value;
        var frame = new SpeechPlaybackFrame(true, 0.5, id, 8, WordAlignmentAvailable: true,
            Word: new(new(new(id, 8), 0, 1, TimeSpan.FromSeconds(1)),
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(9)));
        fixture.TextToSpeech.PlaybackFrame = frame;
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionText.Should().Be(fixture.TextToSpeech.SpokenText);
        fixture.ViewModel.SpeechCaptionEmphasis.Should().Be(new Kora.Application.Voice.SpeechCaptionEmphasis(0, 1));
        fixture.TextToSpeech.SpeakGate.SetResult();
        await response;
        fixture.ViewModel.IsPreviousSpeechCaption.Should().BeTrue();
        fixture.ViewModel.SpeechCaptionText.Should().Be(fixture.TextToSpeech.SpokenText);
        fixture.ViewModel.SpeechCaptionEmphasis.Should().BeNull();
        fixture.TextToSpeech.PlaybackFrame = frame;
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        fixture.ViewModel.SpeechCaptionEmphasis.Should().BeNull();
        fixture.TextToSpeech.SpeakCalls.Should().Be(calls + 1);
        fixture.Reasoner.Requests.Should().BeEmpty();
    }
}
