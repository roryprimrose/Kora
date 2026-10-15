using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.ViewModels;

public sealed partial class MainViewModelTests
{
    [Theory]
    [InlineData(75)]
    [InlineData(100)]
    [InlineData(200)]
    public async Task ConfirmedReaderChangesKeepObservedPlaybackIdentityAndPinnedLifetime(int percent)
    {
        var fixture = new Fixture(enableSpeechText: true);
        await using var admission = fixture.OutputAdmission;
        await fixture.ViewModel.InitializeAsync();
        await fixture.RunAsync("set display.speech-text to CurrentUtterance");
        fixture.TextToSpeech.ClearSpokenResponse();
        fixture.ViewModel.ReaderTextScalePercent = percent;
        fixture.TextToSpeech.SpeakGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = fixture.RunAsync("show your window");
        await fixture.TextToSpeech.SpeakStarted.Task;
        var frame = new SpeechPlaybackFrame(true, .5, fixture.TextToSpeech.CaptionPlaybackId, 4);
        fixture.TextToSpeech.PlaybackFrame = frame;
        fixture.ViewModel.RefreshSpeechPlaybackFrame();
        var text = fixture.ViewModel.SpeechCaptionText;
        text.Should().NotBeNullOrEmpty();
        var placement = fixture.ViewModel.SpeechCaptionPlacement;
        await fixture.ViewModel.ToggleSpeechCaptionPinCommand.ExecuteAsync();
        ISpeechCaptionPresentation caption = fixture.ViewModel;
        foreach (var scale in new[] { percent, 200, 75, 100, percent })
        {
            fixture.ViewModel.ReaderTextScalePercent = scale;
            caption.EnsureReaderTypographyAvailable();
            caption.ReaderTextScalePercent.Should().Be(scale);
            caption.RefreshSpeechPlaybackFrame();
            caption.SpeechCaptionText.Should().Be(text);
            caption.IsSpeechCaptionVisible.Should().BeTrue();
            caption.SpeechCaptionPlacement.Should().Be(placement);
            caption.SpeechCaptionPinLabel.Should().Be("Unpin");
            fixture.TextToSpeech.PlaybackFrame.Should().Be(frame);
            fixture.CaptionPreferences.Mode.Should().Be(SpeechTextMode.CurrentUtterance);
        }
        fixture.TextToSpeech.SpeakGate.SetResult();
        await response;
        caption.SpeechCaptionText.Should().Be(text);
        caption.SpeechCaptionLabel.Should().Be("PREVIOUS SPEECH");
        fixture.ViewModel.ReaderTextScalePercent = percent == 200 ? 75 : 200;
        caption.RefreshSpeechPlaybackFrame();
        caption.SpeechCaptionText.Should().Be(text);
        caption.SpeechCaptionPinLabel.Should().Be("Unpin");
        fixture.ViewModel.ReportSpeechCaptionPresentationFailure(nameof(InvalidOperationException));
        fixture.TextToSpeech.PlaybackFrame = frame;
        caption.RefreshSpeechPlaybackFrame();
        caption.SpeechCaptionText.Should().BeNull();
        caption.IsSpeechCaptionVisible.Should().BeFalse();
        fixture.Reasoner.Requests.Should().BeEmpty();
    }
}
