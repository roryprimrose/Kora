using AwesomeAssertions;
using Kora.Core.Voice;
using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests.Audio;

public sealed class UnavailableHandsFreeAudioStageTests
{
    [Fact]
    public async Task Unqualified_profile_never_opens_capture_or_claims_primary_hands_free()
    {
        var stage = new UnavailableHandsFreeAudioStage();
        stage.IsAvailable.Should().BeFalse();
        var open = () => stage.OpenAsync(new(-2, new("mic", "Microphone"), "Kora",
            new(1, SpeechInputSelection.Default)), TestContext.Current.CancellationToken);
        await open.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no qualified*");
    }

    [Fact]
    public async Task Cancelled_open_reports_cancellation_without_native_acquisition()
    {
        var stage = new UnavailableHandsFreeAudioStage();
        var open = () => stage.OpenAsync(new(-2, new("mic", "Microphone"), "Kora",
            new(1, SpeechInputSelection.Default)), new CancellationToken(canceled: true));
        await open.Should().ThrowAsync<OperationCanceledException>();
    }
}
