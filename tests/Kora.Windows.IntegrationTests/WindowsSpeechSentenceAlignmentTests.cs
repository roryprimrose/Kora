using System.Speech.Synthesis;
using AwesomeAssertions;
using Kora.Application.Voice;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests;

public sealed class WindowsSpeechSentenceAlignmentTests
{
    [Fact]
    public void Matching_native_character_events_are_sealed_before_actual_device_positions_can_reveal_text()
    {
        const string source = "Value 12. Next!";
        var prompt = new Prompt(source);
        var token = new SpeechPlaybackToken(Guid.NewGuid(), 4);
        var alignment = new WindowsTextToSpeechService.SentenceAlignment(prompt, token, source);
        alignment.Observe(new Prompt(source), new(token, -1, 0, TimeSpan.Zero));
        alignment.Observe(prompt, new(token with { Generation = 5 }, -1, 0, TimeSpan.Zero));
        alignment.Observe(prompt, new(token with { Id = Guid.NewGuid() }, -1, 0, TimeSpan.Zero));
        alignment.Observe(prompt, new(token, 0, 5, TimeSpan.FromMilliseconds(100)));
        alignment.Observe(prompt, new(token, 6, 2, TimeSpan.FromMilliseconds(200)));
        // A normalized number may generate repeated native ranges; the first verified time wins.
        alignment.Observe(prompt, new(token, 6, 2, TimeSpan.FromMilliseconds(250)));
        alignment.Observe(prompt, new(token, 10, 4, TimeSpan.FromSeconds(1)));
        alignment.At(TimeSpan.FromSeconds(2)).Should().BeNull();
        alignment.Close();
        alignment.Observe(prompt, new(token, -1, 0, TimeSpan.Zero));
        alignment.Seal(TimeSpan.FromSeconds(3), profileQualified: true);
        alignment.Available.Should().BeTrue();
        alignment.At(TimeSpan.FromMilliseconds(99)).Should().BeNull();
        alignment.At(TimeSpan.FromMilliseconds(100))!.Source.CharacterPosition.Should().Be(0);
        alignment.At(TimeSpan.FromMilliseconds(999))!.Source.CharacterPosition.Should().Be(0);
        var next = alignment.At(TimeSpan.FromSeconds(1))!;
        next.Source.Should().Be(new SpeechSourceProgress(token, 10, 4, TimeSpan.FromSeconds(1)));
        next.PlaybackPosition.Should().Be(TimeSpan.FromSeconds(1));
        alignment.At(TimeSpan.FromSeconds(3))!.Source.CharacterPosition.Should().Be(10);
        alignment.At(TimeSpan.FromSeconds(4)).Should().BeNull();
        alignment.At(TimeSpan.FromTicks(-1)).Should().BeNull();
        alignment.Invoking(value => value.Seal(TimeSpan.FromSeconds(3), true)).Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("unqualified")]
    [InlineData("absent")]
    [InlineData("negative")]
    [InlineData("negative-index")]
    [InlineData("count")]
    [InlineData("outside")]
    [InlineData("overlap")]
    [InlineData("partial-overlap")]
    [InlineData("punctuation")]
    [InlineData("reverse-time")]
    [InlineData("reverse-range")]
    [InlineData("cross-sentence")]
    [InlineData("coverage")]
    [InlineData("duration")]
    [InlineData("same-start")]
    [InlineData("surrogate")]
    [InlineData("ambiguous")]
    [InlineData("empty-token")]
    [InlineData("negative-generation")]
    [InlineData("source-limit")]
    [InlineData("sentence-limit")]
    public void Missing_unknown_or_malformed_alignment_never_claims_availability(string failure)
    {
        var source = failure switch
        {
            "surrogate" => "Hi \ud83d\ude00. Next.",
            "ambiguous" => "Dr. Smith.",
            "source-limit" => new string('x', SpeechSentenceMap.MaximumCharacters + 1),
            "sentence-limit" => string.Concat(Enumerable.Repeat("Word. ", SpeechSentenceMap.MaximumSentences + 1)),
            _ => "First. Next.",
        };
        var prompt = new Prompt(source);
        var token = new SpeechPlaybackToken(failure is "empty-token" ? Guid.Empty : Guid.NewGuid(),
            failure is "negative-generation" ? -1 : 1);
        var alignment = new WindowsTextToSpeechService.SentenceAlignment(prompt, token, source);
        void Emit(int start, int count, TimeSpan time) => alignment.Observe(prompt, new(token, start, count, time));
        Emit(0, 5, TimeSpan.FromSeconds(1));
        switch (failure)
        {
            case "negative": Emit(7, 4, TimeSpan.FromTicks(-1)); break;
            case "negative-index": Emit(-1, 4, TimeSpan.FromSeconds(2)); break;
            case "count": Emit(7, 0, TimeSpan.FromSeconds(2)); break;
            case "outside": Emit(7, int.MaxValue, TimeSpan.FromSeconds(2)); break;
            case "overlap": Emit(0, 4, TimeSpan.FromSeconds(2)); break;
            case "partial-overlap": Emit(3, 2, TimeSpan.FromSeconds(2)); break;
            case "punctuation": Emit(5, 1, TimeSpan.FromSeconds(2)); break;
            case "reverse-time": Emit(7, 4, TimeSpan.Zero); break;
            case "reverse-range":
                Emit(7, 4, TimeSpan.FromSeconds(2));
                Emit(0, 5, TimeSpan.FromSeconds(3)); break;
            case "cross-sentence": Emit(4, 5, TimeSpan.FromSeconds(2)); break;
            case "surrogate": Emit(4, 1, TimeSpan.FromSeconds(2)); break;
            case "coverage": Emit(8, 3, TimeSpan.FromSeconds(2)); break;
            case "same-start": Emit(7, 4, TimeSpan.FromSeconds(1)); break;
            case "absent": break;
            default: Emit(7, 4, TimeSpan.FromSeconds(2)); break;
        }
        alignment.Seal(TimeSpan.FromSeconds(failure is "duration" ? 2 : 4), profileQualified: failure is not "unqualified",
            wordProfileQualified: failure is not "unqualified");
        alignment.Available.Should().BeFalse();
        alignment.WordsAvailable.Should().BeFalse();
        alignment.At(TimeSpan.FromSeconds(3)).Should().BeNull();
        alignment.WordAt(TimeSpan.FromSeconds(3)).Should().BeNull();
    }

    [Fact]
    public async Task Retired_prompt_callbacks_cannot_mutate_a_sealed_timeline()
    {
        var prompt = new Prompt("First.");
        var token = new SpeechPlaybackToken(Guid.NewGuid(), 1);
        var alignment = new WindowsTextToSpeechService.SentenceAlignment(prompt, token, "First.");
        alignment.Observe(prompt, new(token, 0, 5, TimeSpan.Zero));
        alignment.Close();
        await Task.Run(() => alignment.Observe(prompt, new(token, -1, -1, TimeSpan.FromTicks(-1))),
            TestContext.Current.CancellationToken);
        alignment.Seal(TimeSpan.FromSeconds(1), true);
        alignment.At(TimeSpan.Zero)!.Source.CharacterPosition.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Output_retirement_clears_alignment_and_late_callbacks_cannot_reseal_or_reveal_it(bool sealedBeforeRetirement)
    {
        var token = new SpeechPlaybackToken(Guid.NewGuid(), 1);
        var prompt = new Prompt("First.");
        var alignment = new WindowsTextToSpeechService.SentenceAlignment(prompt, token, "First.");
        alignment.Observe(prompt, new(token, 0, 5, TimeSpan.Zero));
        if (sealedBeforeRetirement) { alignment.Seal(TimeSpan.FromSeconds(1), true, true); }
        alignment.Retire();
        alignment.Observe(prompt, new(token, 0, 5, TimeSpan.Zero));
        if (!sealedBeforeRetirement) { alignment.Seal(TimeSpan.FromSeconds(1), true, true); }
        alignment.Available.Should().BeFalse();
        alignment.WordsAvailable.Should().BeFalse();
        alignment.At(TimeSpan.Zero).Should().BeNull();
        alignment.WordAt(TimeSpan.Zero).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_words_keep_exact_unicode_repeated_word_and_normalized_number_offsets_at_device_boundaries(bool qualified)
    {
        const string source = "Go go \ud83d\ude00 12. Next!";
        var prompt = new Prompt(source);
        var token = new SpeechPlaybackToken(Guid.NewGuid(), 8);
        var alignment = new WindowsTextToSpeechService.SentenceAlignment(prompt, token, source);
        alignment.Observe(new Prompt(source), new(token, -1, 0, TimeSpan.Zero));
        alignment.Observe(prompt, new(token with { Id = Guid.NewGuid() }, -1, 0, TimeSpan.Zero));
        alignment.Observe(prompt, new(token with { Generation = 9 }, -1, 0, TimeSpan.Zero));
        var ranges = new[] { (0, 2, 100), (3, 2, 200), (6, 2, 300), (9, 2, 400), (13, 4, 1000) };
        foreach (var (start, count, milliseconds) in ranges)
        {
            alignment.Observe(prompt, new(token, start, count, TimeSpan.FromMilliseconds(milliseconds)));
            alignment.Observe(prompt, new(token, start, count, TimeSpan.FromMilliseconds(milliseconds + 20)));
        }
        alignment.WordAt(TimeSpan.FromSeconds(1)).Should().BeNull("synthesis alone never grants playback evidence");
        alignment.Close();
        alignment.Observe(prompt, new(token, -1, -1, TimeSpan.Zero));
        alignment.Seal(TimeSpan.FromSeconds(2), profileQualified: false, wordProfileQualified: qualified);
        alignment.Available.Should().BeFalse("word qualification is independent of sentence qualification");
        alignment.WordsAvailable.Should().Be(qualified);
        alignment.WordAt(TimeSpan.FromMilliseconds(99)).Should().BeNull();
        alignment.WordAt(TimeSpan.FromTicks(-1)).Should().BeNull();
        alignment.WordAt(TimeSpan.FromSeconds(2)).Should().BeNull("audio end is not a playing word");
        foreach (var (start, count, milliseconds) in ranges)
        {
            var word = alignment.WordAt(TimeSpan.FromMilliseconds(milliseconds));
            if (!qualified) { word.Should().BeNull(); continue; }
            word!.Source.Should().Be(new SpeechSourceProgress(token, start, count, TimeSpan.FromMilliseconds(milliseconds)));
            alignment.WordAt(word.EndPosition - TimeSpan.FromTicks(1))!.Source.Should().Be(word.Source);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Word_buffer_is_bounded_without_disabling_valid_sentence_fallback(bool overflow)
    {
        var count = WindowsTextToSpeechService.SentenceAlignment.MaximumWordRanges + (overflow ? 1 : 0);
        var source = string.Concat(Enumerable.Repeat("x ", count));
        var prompt = new Prompt(source);
        var token = new SpeechPlaybackToken(Guid.NewGuid(), 0);
        var alignment = new WindowsTextToSpeechService.SentenceAlignment(prompt, token, source);
        for (var index = 0; index < count; index++)
        {
            alignment.Observe(prompt, new(token, index * 2, 1, TimeSpan.FromTicks(index)));
        }
        alignment.Seal(TimeSpan.FromTicks(count), true, true);
        alignment.Available.Should().BeTrue();
        alignment.WordsAvailable.Should().Be(!overflow);
    }

    [Fact]
    public void Simultaneous_distinct_ranges_have_sentence_fallback_but_no_word_accuracy_claim()
    {
        var prompt = new Prompt("Go go.");
        var token = new SpeechPlaybackToken(Guid.NewGuid(), 1);
        var alignment = new WindowsTextToSpeechService.SentenceAlignment(prompt, token, "Go go.");
        alignment.Observe(prompt, new(token, 0, 2, TimeSpan.Zero));
        alignment.Observe(prompt, new(token, 3, 2, TimeSpan.Zero));
        alignment.Seal(TimeSpan.FromSeconds(1), true, true);
        alignment.Available.Should().BeTrue();
        alignment.WordsAvailable.Should().BeFalse();
    }

    [Fact]
    public void Native_callback_budget_is_fail_closed_even_for_identical_ranges()
    {
        var prompt = new Prompt("Word.");
        var token = new SpeechPlaybackToken(Guid.NewGuid(), 1);
        var alignment = new WindowsTextToSpeechService.SentenceAlignment(prompt, token, "Word.");
        for (var index = 0; index <= SpeechSentenceMap.MaximumCharacters * 2; index++)
        {
            alignment.Observe(prompt, new(token, 0, 4, TimeSpan.Zero));
        }
        alignment.Seal(TimeSpan.FromSeconds(1), true, true);
        alignment.Available.Should().BeFalse();
        alignment.WordsAvailable.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_callback_ranges_reach_the_shared_caption_consumer_without_rewriting_or_finishing_unobserved_text(bool profileQualified)
    {
        const string source = "Caf\u00e9 \ud83d\ude00!\r\nNext?!  Last.";
        var response = Guid.NewGuid();
        var caption = new SpeechCaption();
        var id = caption.Bind(response, HostRequest.Create(RequestOrigin.LocalUi), source,
            static () => true, mode: SpeechTextMode.CurrentSentence);
        var token = new SpeechPlaybackToken(id, 8);
        var prompt = new Prompt(source);
        var native = new WindowsTextToSpeechService.SentenceAlignment(prompt, token, source);
        native.Observe(prompt, new(token, 0, 7, TimeSpan.FromMilliseconds(100)));
        native.Observe(prompt, new(token, 10, 4, TimeSpan.FromSeconds(1)));
        native.Observe(prompt, new(token, 18, 4, TimeSpan.FromSeconds(2)));
        var frame = new SpeechPlaybackFrame(true, 0.4, id, 8);
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().BeNull();
        native.Seal(TimeSpan.FromSeconds(3), profileQualified);
        frame = frame with { SentenceAlignmentAvailable = native.Available, Sentence = native.At(TimeSpan.Zero) };
        caption.Observe(frame, response).Should().BeNull();
        frame = frame with { Sentence = native.At(TimeSpan.FromMilliseconds(100)) };
        caption.Observe(frame, response).Should().Be(profileQualified ? "Caf\u00e9 \ud83d\ude00!\r\n" : null);
        frame = frame with { Sentence = native.At(TimeSpan.FromSeconds(1)) };
        caption.Observe(frame, response).Should().Be(profileQualified ? "Next?!  " : null);
        caption.SetPinned(true, response, frame).Should().Be(profileQualified);
        caption.Complete();
        caption.Observe(SpeechPlaybackFrame.Inactive, response).Should().Be(profileQualified ? "Next?!  " : null);
        caption.IsPreviousSpeech.Should().Be(profileQualified);
        caption.Retire();
        caption.Observe(frame, response).Should().BeNull();
    }
}
