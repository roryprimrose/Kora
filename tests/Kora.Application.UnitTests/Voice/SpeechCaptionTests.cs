using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed class SpeechCaptionTests
{
    [Fact]
    public void Queued_text_is_not_visible_and_replacement_identity_cannot_revive_retired_text()
    {
        var caption = new SpeechCaption();
        var response = Guid.NewGuid();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var first = caption.Bind(response, request, "Exact approved text.", static () => true);
        caption.Source.Should().Be(request);
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().BeNull();
        caption.Observe(new(true, 0.8, first, 10), response).Should().Be("Exact approved text.");
        var next = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), "New approved text.", static () => true);
        next.Should().NotBe(first);
        caption.Observe(new(true, 0.8, first, 10), response).Should().BeNull();
        caption.Observe(new(true, 0.8, next, 10), response).Should().BeNull();
    }

    [Fact]
    public async Task Legacy_provider_overload_never_claims_caption_playback_identity()
    {
        await using ISpeechPlaybackService playback = new LegacyPlayback();
        await playback.SpeakAsync("Exact approved legacy speech.",
            new("voice", "Voice", "en-US", SpeechVoiceGender.Female),
            new("output", "Output"), Guid.NewGuid(), TestContext.Current.CancellationToken);
        playback.PlaybackFrame.PlaybackId.Should().BeNull();
    }

    private sealed class LegacyPlayback : ISpeechPlaybackService
    {
        public bool IsSpeaking => false;
        public SpeechPlaybackFrame PlaybackFrame => SpeechPlaybackFrame.Inactive;
        public void InvalidateOutput() { }
        public Task SpeakAsync(string text, SpeechVoice voice, AudioOutputDevice outputDevice, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("segment")]
    [InlineData("response")]
    [InlineData("ownership")]
    [InlineData("finish")]
    [InlineData("retire")]
    [InlineData("unqualified")]
    public void Unknown_stale_and_retired_playback_never_reveals_text(string transition)
    {
        var caption = new SpeechCaption();
        var eligible = true;
        var response = Guid.NewGuid();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.ActivatedVoice), "Sensitive utterance.", () => eligible);
        var frame = new SpeechPlaybackFrame(true, 0, id, 8);
        caption.Observe(frame, response).Should().Be("Sensitive utterance.");
        switch (transition)
        {
            case "generation": frame = frame with { Generation = 9 }; break;
            case "segment": frame = frame with { Segment = 1 }; break;
            case "response": response = Guid.NewGuid(); break;
            case "ownership": eligible = false; break;
            case "finish": frame = SpeechPlaybackFrame.Inactive; break;
            case "retire": caption.Retire(); break;
            case "unqualified": frame = frame with { PlaybackId = null }; break;
        }
        caption.Observe(frame, response).Should().BeNull();
        eligible = true;
        caption.Observe(new(true, 0, id, 8), response).Should().BeNull();
    }
}
