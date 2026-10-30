using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed class SpeechCaptionTests
{
    [Fact]
    public void Sentence_caption_uses_only_observed_exact_slices_and_completion_does_not_advance()
    {
        var clock = new Clock();
        var caption = new SpeechCaption(clock);
        var response = Guid.NewGuid();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), "First. \r\nSecond! Last?",
            static () => true, mode: SpeechTextMode.CurrentSentence);
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().BeNull();
        var waiting = new SpeechPlaybackFrame(true, 0.5, id, 4, SentenceAlignmentAvailable: true);
        caption.Observe(waiting, response).Should().BeNull();
        var first = new SpeechSentencePosition(new(new(id, 4), 0, 5, TimeSpan.FromSeconds(1)),
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(9));
        var frame = waiting with { Sentence = first };
        caption.Observe(frame, response).Should().Be("First. \r\n");
        caption.Observe(frame with { Sentence = first with { PlaybackPosition = TimeSpan.FromSeconds(2) } }, response)
            .Should().Be("First. \r\n");
        var second = new SpeechSentencePosition(new(new(id, 4), 9, 6, TimeSpan.FromSeconds(3)),
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(9));
        caption.Observe(frame with { Sentence = second }, response).Should().Be("Second! ");
        caption.SetPinned(true, response, frame with { Sentence = second }).Should().BeTrue();
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().BeNull();
        caption.Complete();
        clock.Seconds = 100;
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().Be("Second! ");
        caption.IsPreviousSpeech.Should().BeTrue();
        caption.SetPinned(false, response, SpeechPlaybackFrame.Inactive).Should().BeTrue();
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().BeNull();
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("map")]
    [InlineData("range")]
    [InlineData("index")]
    [InlineData("rewind")]
    [InlineData("time")]
    [InlineData("start")]
    [InlineData("duration")]
    [InlineData("next-start")]
    [InlineData("missing")]
    [InlineData("source")]
    [InlineData("source-generation")]
    [InlineData("source-null")]
    [InlineData("source-change")]
    public void Malformed_unavailable_or_regressing_sentence_evidence_retires_without_revival(string failure)
    {
        var caption = new SpeechCaption();
        var response = Guid.NewGuid();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi),
            failure is "map" ? "Dr. Smith." : "First. Next.", static () => true, mode: SpeechTextMode.CurrentSentence);
        var position = new SpeechSentencePosition(new(new(id, 4), 0, 5, TimeSpan.FromSeconds(1)),
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(8));
        var frame = new SpeechPlaybackFrame(true, 0.5, id, 4, Sentence: position, SentenceAlignmentAvailable: true);
        if (failure is not ("absent" or "map"))
        {
            caption.Observe(frame, response).Should().Be("First. ");
        }
        frame = failure switch
        {
            "absent" => frame with { SentenceAlignmentAvailable = false },
            "range" => frame with { Sentence = position with { Source = position.Source with { CharacterCount = 8 } } },
            "index" => frame with { Sentence = position with { Source = position.Source with { CharacterPosition = 99 } } },
            "rewind" => frame with { Sentence = position with { PlaybackPosition = TimeSpan.FromSeconds(1) } },
            "time" => frame with { Sentence = position with { PlaybackPosition = TimeSpan.Zero } },
            "start" => frame with { Sentence = position with { Source = position.Source with { AudioPosition = TimeSpan.Zero } } },
            "duration" => frame with { Sentence = position with { AudioDuration = TimeSpan.FromSeconds(9) } },
            "next-start" => frame with { Sentence = new(position.Source with { CharacterPosition = 7, CharacterCount = 4 },
                TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(8)) },
            "missing" => frame with { Sentence = null },
            "source" => frame with { Sentence = position with { Source = position.Source with { Playback = new(Guid.NewGuid(), 4) } } },
            "source-generation" => frame with { Sentence = position with { Source = position.Source with { Playback = new(id, 5) } } },
            "source-null" => frame with { Sentence = position with { Source = null! } },
            "source-change" => frame with { Sentence = position with { Source = position.Source with { CharacterCount = 4 } } },
            _ => frame,
        };
        if (failure is "rewind")
        {
            var next = position with
            {
                Source = position.Source with { CharacterPosition = 7, CharacterCount = 4, AudioPosition = TimeSpan.FromSeconds(3) },
                PlaybackPosition = TimeSpan.FromSeconds(4),
            };
            caption.Observe(frame with { Sentence = next }, response).Should().Be("Next.");
        }
        caption.Observe(frame, response).Should().BeNull();
        caption.Observe(new(true, 0.5, id, 4, Sentence: position, SentenceAlignmentAvailable: true), response).Should().BeNull();
        caption.Complete();
        caption.IsPreviousSpeech.Should().BeFalse();
    }

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
    [InlineData(0, SpeechTextMode.CurrentSentence)]
    [InlineData(5, SpeechTextMode.CurrentSentence)]
    [InlineData(30, SpeechTextMode.CurrentSentence)]
    public void Normal_completion_retains_only_observed_text_until_the_exact_deadline(int seconds,
        SpeechTextMode mode = SpeechTextMode.CurrentUtterance)
    {
        var clock = new Clock();
        var caption = new SpeechCaption(clock);
        var response = Guid.NewGuid();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), "Approved text.", static () => true,
            new(SpeechCaptionPlacement.BottomRight, seconds), mode);
        var frame = new SpeechPlaybackFrame(true, 0.5, id, 3,
            Sentence: new(new(new(id, 3), 0, 8, TimeSpan.Zero), TimeSpan.Zero, TimeSpan.FromSeconds(1)),
            SentenceAlignmentAvailable: true);
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
