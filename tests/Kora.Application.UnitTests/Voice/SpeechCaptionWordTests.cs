using AwesomeAssertions;

using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.UnitTests.Voice;

public sealed class SpeechCaptionWordTests
{
    private const string Source = "go go \ud83d\ude00 12. Next!";

    [Theory]
    [InlineData(SpeechTextMode.CurrentUtterance)]
    [InlineData(SpeechTextMode.CurrentSentence)]
    public void Only_reached_source_offsets_are_projected_and_previous_speech_has_no_active_word(SpeechTextMode mode)
    {
        var caption = new SpeechCaption();
        var response = Guid.NewGuid();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), Source, static () => true,
            new(SpeechCaptionPlacement.BottomRight, 30, true), mode);
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().BeNull();
        caption.Emphasis.Should().BeNull();
        var frame = Frame(id, 0, 2, 1, 2);
        caption.Observe(frame with { Word = null }, response).Should().NotBeNull();
        caption.Emphasis.Should().BeNull();
        foreach (var (start, count, time) in new[] { (0, 2, 1), (3, 2, 2), (6, 2, 3), (9, 2, 4), (13, 4, 5) })
        {
            frame = Frame(id, start, count, time, time + 1);
            caption.Observe(frame, response).Should().Be(mode == SpeechTextMode.CurrentUtterance
                ? Source : start < 13 ? "go go \ud83d\ude00 12. " : "Next!");
            caption.Emphasis.Should().Be(new SpeechCaptionEmphasis(mode == SpeechTextMode.CurrentSentence && start == 13 ? 0 : start, count));
            caption.Observe(frame with { Word = frame.Word! with { PlaybackPosition = TimeSpan.FromSeconds(time + 0.5) } }, response);
            caption.Emphasis.Should().NotBeNull();
        }
        caption.SetPinned(true, response, frame with { Word = frame.Word! with { PlaybackPosition = TimeSpan.FromSeconds(5.5) } })
            .Should().BeTrue();
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().BeNull();
        caption.Emphasis.Should().BeNull();
        caption.Complete();
        caption.Observe(frame, response).Should().NotBeNull();
        caption.IsPreviousSpeech.Should().BeTrue();
        caption.Emphasis.Should().BeNull();
        caption.Retire();
        caption.Observe(frame, response).Should().BeNull();
        caption.Emphasis.Should().BeNull();
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("generation")]
    [InlineData("source-null")]
    [InlineData("range")]
    [InlineData("surrogate-start")]
    [InlineData("surrogate-end")]
    [InlineData("negative-time")]
    [InlineData("future")]
    [InlineData("at-end")]
    [InlineData("end-after-duration")]
    [InlineData("duration-change")]
    [InlineData("device-rewind")]
    [InlineData("source-rewind")]
    [InlineData("range-change")]
    [InlineData("end-change")]
    [InlineData("overlap")]
    [InlineData("early-next")]
    public void Invalid_or_regressing_word_evidence_disables_only_emphasis_without_revival(string failure)
    {
        var caption = new SpeechCaption();
        var response = Guid.NewGuid();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), Source, static () => true,
            new(SpeechCaptionPlacement.BottomRight, 5, true));
        var frame = Frame(id, 3, 2, 1, 2);
        caption.Observe(frame, response).Should().Be(Source);
        caption.Emphasis.Should().Be(new SpeechCaptionEmphasis(3, 2));
        var word = frame.Word!;
        frame = failure switch
        {
            "unavailable" => frame with { WordAlignmentAvailable = false },
            "missing" => frame with { Word = null },
            "foreign" => frame with { Word = word with { Source = word.Source with { Playback = new(Guid.NewGuid(), 8) } } },
            "generation" => frame with { Word = word with { Source = word.Source with { Playback = new(id, 9) } } },
            "source-null" => frame with { Word = word with { Source = null! } },
            "range" => frame with { Word = word with { Source = word.Source with { CharacterCount = int.MaxValue } } },
            "surrogate-start" => Frame(id, 7, 1, 2, 3),
            "surrogate-end" => Frame(id, 6, 1, 2, 3),
            "negative-time" => frame with { Word = word with { Source = word.Source with { AudioPosition = TimeSpan.FromTicks(-1) } } },
            "future" => frame with { Word = word with { PlaybackPosition = TimeSpan.Zero } },
            "at-end" => frame with { Word = word with { PlaybackPosition = word.EndPosition } },
            "end-after-duration" => frame with { Word = word with { EndPosition = TimeSpan.FromSeconds(11) } },
            "duration-change" => frame with { Word = word with { AudioDuration = TimeSpan.FromSeconds(11) } },
            "device-rewind" => frame with { Word = word with { Source = word.Source with { AudioPosition = TimeSpan.Zero }, PlaybackPosition = TimeSpan.Zero } },
            "source-rewind" => Frame(id, 0, 2, 2, 3),
            "range-change" => frame with { Word = word with { Source = word.Source with { CharacterCount = 1 } } },
            "end-change" => frame with { Word = word with { EndPosition = TimeSpan.FromSeconds(3) } },
            "overlap" => Frame(id, 4, 1, 2, 3),
            "early-next" => Frame(id, 6, 2, 1, 3),
            _ => throw new InvalidOperationException(),
        };
        caption.Observe(frame, response).Should().Be(Source);
        caption.Emphasis.Should().BeNull();
        caption.Observe(Frame(id, 6, 2, 3, 4), response).Should().Be(Source);
        caption.Emphasis.Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Preference_off_unqualified_and_unsupported_maps_keep_honest_utterance_text(bool requested, bool unsupported)
    {
        var caption = new SpeechCaption();
        var response = Guid.NewGuid();
        var source = unsupported ? "Dr. Smith." : Source;
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), source, static () => true,
            new(SpeechCaptionPlacement.BottomRight, 5, requested));
        caption.Observe(Frame(id, 0, 2, 1, 2) with { WordAlignmentAvailable = unsupported }, response).Should().Be(source);
        caption.Emphasis.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    public void Words_outside_the_current_sentence_cannot_be_projected_into_its_slice(int wordStart)
    {
        var caption = new SpeechCaption();
        var response = Guid.NewGuid();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), Source, static () => true,
            new(SpeechCaptionPlacement.BottomRight, 5, true), SpeechTextMode.CurrentSentence);
        var frame = Frame(id, wordStart, 2, 1, 2);
        var sentenceStart = wordStart == 0 ? 13 : 0;
        frame = frame with { Sentence = new(new(new(id, 8), sentenceStart, 2, TimeSpan.Zero),
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10)) };
        caption.Observe(frame, response).Should().NotBeNull();
        caption.Emphasis.Should().BeNull();
    }

    [Fact]
    public void Replacement_and_eligibility_retirement_clear_word_evidence_and_restart_has_no_replay()
    {
        var caption = new SpeechCaption();
        var response = Guid.NewGuid();
        var request = HostRequest.Create(RequestOrigin.LocalUi);
        var eligible = true;
        var options = new SpeechCaptionOptions(SpeechCaptionPlacement.BottomRight, 5, true);
        var first = caption.Bind(response, request, Source, () => eligible, options);
        caption.Observe(Frame(first, 0, 2, 1, 2), response);
        caption.Emphasis.Should().NotBeNull();
        eligible = false;
        caption.Observe(Frame(first, 3, 2, 2, 3), response).Should().BeNull();
        caption.Emphasis.Should().BeNull();
        eligible = true;
        caption.Observe(Frame(first, 3, 2, 2, 3), response).Should().BeNull();
        var next = caption.Bind(response, request, Source, () => eligible, options);
        caption.Emphasis.Should().BeNull();
        caption.Observe(Frame(next, 0, 2, 1, 2), response);
        caption.Emphasis.Should().NotBeNull();
        caption.Bind(response, request, Source, () => eligible, options);
        caption.Emphasis.Should().BeNull();
        caption.Observe(Frame(next, 3, 2, 2, 3), response).Should().BeNull();
        new SpeechCaption().Observe(Frame(next, 3, 2, 2, 3), response).Should().BeNull();
    }

    private static SpeechPlaybackFrame Frame(Guid id, int start, int count, int seconds, int end) =>
        new(true, 0.5, id, 8,
            Sentence: new(new(new(id, 8), start < 13 ? 0 : 13, 2, TimeSpan.FromSeconds(start < 13 ? 1 : 5)),
                TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(10)),
            SentenceAlignmentAvailable: true,
            Word: new(new(new(id, 8), start, count, TimeSpan.FromSeconds(seconds)),
                TimeSpan.FromSeconds(seconds), TimeSpan.FromSeconds(end), TimeSpan.FromSeconds(10)),
            WordAlignmentAvailable: true);
}
