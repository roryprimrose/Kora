using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed class SpeechCaptionTests
{
    [Fact]
    public void Replacement_during_admission_cannot_publish_old_text_or_retire_the_new_source()
    {
        var caption = new SpeechCaption();
        var response = Guid.NewGuid();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var next = Guid.Empty;
        var first = caption.Bind(response, request, "Old text.", () =>
        {
            next = caption.Bind(response, request, "New approved text.", static () => true);
            return true;
        });
        caption.Observe(new(true, 0.8, first, 10), response).Should().BeNull();
        caption.Observe(new(true, 0.8, next, 11), response).Should().Be("New approved text.");
    }

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

    private sealed class Clock : TimeProvider
    {
        public long Seconds { get; set; }
        public Action? OnTimestamp { get; set; }
        public override long TimestampFrequency => 1;
        public override long GetTimestamp()
        {
            var callback = OnTimestamp;
            OnTimestamp = null;
            callback?.Invoke();
            return Seconds;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(30)]
    public void Normal_completion_retains_only_observed_text_until_the_exact_deadline(int seconds)
    {
        var clock = new Clock();
        var caption = new SpeechCaption(clock);
        var response = Guid.NewGuid();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), "Approved text.", static () => true,
            new(SpeechCaptionPlacement.BottomRight, seconds));
        var frame = new SpeechPlaybackFrame(true, 0.5, id, 3);
        caption.Observe(frame, response).Should().Be("Approved text.");
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().BeNull();
        caption.Complete();
        caption.IsPreviousSpeech.Should().BeTrue();
        if (seconds > 0)
        {
            clock.Seconds = seconds - 1;
            caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().Be("Approved text.");
        }
        clock.Seconds = seconds;
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().BeNull();
        caption.SetPinned(true, response, frame).Should().BeFalse();
        caption.Observe(frame, response).Should().BeNull();
    }

    [Fact]
    public void Pinning_extends_only_observed_text_and_unpinning_keeps_the_original_deadline()
    {
        var clock = new Clock();
        var eligible = true;
        var caption = new SpeechCaption(clock);
        var response = Guid.NewGuid();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), "Approved text.", () => eligible);
        caption.SetPinned(true, response, SpeechPlaybackFrame.Inactive).Should().BeFalse();
        var frame = new SpeechPlaybackFrame(true, 0.5, id, 3);
        caption.Observe(frame, response).Should().Be("Approved text.");
        caption.SetPinned(true, response, frame).Should().BeTrue();
        caption.Complete();
        clock.Seconds = 100;
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().Be("Approved text.");
        caption.IsPinned.Should().BeTrue();
        caption.SetPinned(false, response, SpeechPlaybackFrame.Inactive).Should().BeTrue();
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().BeNull();
        id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), "Next text.", () => eligible);
        frame = frame with { PlaybackId = id };
        caption.SetPinned(true, response, frame).Should().BeTrue();
        caption.Complete();
        eligible = false;
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().BeNull();
        caption.IsPinned.Should().BeFalse();
    }

    [Fact]
    public void Unobserved_completion_and_foreign_playback_cannot_create_or_retain_a_caption()
    {
        var caption = new SpeechCaption();
        var response = Guid.NewGuid();
        caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), "Unplayed text.", static () => true);
        caption.Complete();
        caption.IsPreviousSpeech.Should().BeFalse();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), "Played text.", static () => true);
        caption.Observe(new(true, 0.5, id, 3), response).Should().NotBeNull();
        caption.Complete();
        caption.Observe(new(true, 0.5, Guid.NewGuid(), 3), response).Should().BeNull();
    }

    [Theory]
    [InlineData("same")]
    [InlineData("generation")]
    [InlineData("segment")]
    [InlineData("unqualified")]
    public void Completed_caption_accepts_no_foreign_frame_and_never_claims_renewed_playback(string stage)
    {
        var caption = new SpeechCaption(new Clock());
        var response = Guid.NewGuid();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), "Played text.", static () => true);
        var frame = new SpeechPlaybackFrame(true, 0.5, id, 3);
        caption.Observe(frame, response).Should().NotBeNull();
        caption.Complete();
        frame = stage switch
        {
            "generation" => frame with { Generation = 4 },
            "segment" => frame with { Segment = 1 },
            "unqualified" => frame with { PlaybackId = null },
            _ => frame,
        };
        if (stage is "same")
        {
            caption.Observe(frame, response).Should().Be("Played text.");
            caption.IsPreviousSpeech.Should().BeTrue();
        }
        else { caption.Observe(frame, response).Should().BeNull(); }
    }

    [Fact]
    public void Replacement_during_pin_expiry_check_cannot_pin_the_new_source()
    {
        var clock = new Clock();
        var caption = new SpeechCaption(clock);
        var response = Guid.NewGuid();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var id = caption.Bind(response, request, "Played text.", static () => true);
        caption.Observe(new(true, 0.5, id, 3), response).Should().NotBeNull();
        caption.Complete();
        clock.OnTimestamp = () =>
        {
            var next = caption.Bind(response, request, "New text.", static () => true);
            caption.Observe(new(true, 0.5, next, 4), response).Should().Be("New text.");
        };
        caption.SetPinned(true, response, SpeechPlaybackFrame.Inactive).Should().BeFalse();
        caption.IsPinned.Should().BeFalse();
    }
}
