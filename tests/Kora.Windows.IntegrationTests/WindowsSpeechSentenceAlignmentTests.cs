using System.Speech.Synthesis;
using AwesomeAssertions;
using Kora.Windows.Audio;

namespace Kora.Windows.IntegrationTests;

public sealed class WindowsSpeechSentenceAlignmentTests
{
    [Fact]
    public void Matching_native_character_events_are_sealed_before_actual_device_positions_can_reveal_text()
    {
        const string source = "Value 12. Next!";
        var prompt = new Prompt(source);
        var alignment = new WindowsTextToSpeechService.SentenceAlignment(prompt, 4, source);
        alignment.Observe(new Prompt(source), 4, -1, 0, TimeSpan.Zero);
        alignment.Observe(prompt, 5, -1, 0, TimeSpan.Zero);
        alignment.Observe(prompt, 4, 0, 5, TimeSpan.FromMilliseconds(100));
        alignment.Observe(prompt, 4, 6, 2, TimeSpan.FromMilliseconds(200));
        // A normalized number may generate repeated native ranges; the first verified time wins.
        alignment.Observe(prompt, 4, 6, 2, TimeSpan.FromMilliseconds(250));
        alignment.Observe(prompt, 4, 10, 4, TimeSpan.FromSeconds(1));
        alignment.At(TimeSpan.FromSeconds(2)).Should().BeNull();
        alignment.Close();
        alignment.Observe(prompt, 4, -1, 0, TimeSpan.Zero);
        alignment.Seal(TimeSpan.FromSeconds(3), profileQualified: true);
        alignment.Available.Should().BeTrue();
        alignment.At(TimeSpan.FromMilliseconds(99)).Should().BeNull();
        alignment.At(TimeSpan.FromMilliseconds(100))!.Index.Should().Be(0);
        alignment.At(TimeSpan.FromMilliseconds(999))!.Index.Should().Be(0);
        var next = alignment.At(TimeSpan.FromSeconds(1))!;
        next.Index.Should().Be(1);
        next.Start.Should().Be(10);
        next.Length.Should().Be(5);
        next.AudioStart.Should().Be(TimeSpan.FromSeconds(1));
        next.PlaybackPosition.Should().Be(TimeSpan.FromSeconds(1));
        alignment.At(TimeSpan.FromSeconds(3))!.Index.Should().Be(1);
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
    public void Missing_unknown_or_malformed_alignment_never_claims_availability(string failure)
    {
        var source = failure switch { "surrogate" => "Hi \ud83d\ude00. Next.", "ambiguous" => "Dr. Smith.", _ => "First. Next." };
        var prompt = new Prompt(source);
        var alignment = new WindowsTextToSpeechService.SentenceAlignment(prompt, 1, source);
        alignment.Observe(prompt, 1, 0, 5, TimeSpan.FromSeconds(1));
        switch (failure)
        {
            case "negative": alignment.Observe(prompt, 1, 7, 4, TimeSpan.FromTicks(-1)); break;
            case "negative-index": alignment.Observe(prompt, 1, -1, 4, TimeSpan.FromSeconds(2)); break;
            case "count": alignment.Observe(prompt, 1, 7, 0, TimeSpan.FromSeconds(2)); break;
            case "outside": alignment.Observe(prompt, 1, 7, int.MaxValue, TimeSpan.FromSeconds(2)); break;
            case "overlap": alignment.Observe(prompt, 1, 0, 4, TimeSpan.FromSeconds(2)); break;
            case "partial-overlap": alignment.Observe(prompt, 1, 3, 2, TimeSpan.FromSeconds(2)); break;
            case "punctuation": alignment.Observe(prompt, 1, 5, 1, TimeSpan.FromSeconds(2)); break;
            case "reverse-time": alignment.Observe(prompt, 1, 7, 4, TimeSpan.Zero); break;
            case "reverse-range":
                alignment.Observe(prompt, 1, 7, 4, TimeSpan.FromSeconds(2));
                alignment.Observe(prompt, 1, 0, 5, TimeSpan.FromSeconds(3)); break;
            case "cross-sentence": alignment.Observe(prompt, 1, 4, 5, TimeSpan.FromSeconds(2)); break;
            case "surrogate": alignment.Observe(prompt, 1, 4, 1, TimeSpan.FromSeconds(2)); break;
            case "coverage": alignment.Observe(prompt, 1, 8, 3, TimeSpan.FromSeconds(2)); break;
            case "same-start": alignment.Observe(prompt, 1, 7, 4, TimeSpan.FromSeconds(1)); break;
            case "absent": break;
            default: alignment.Observe(prompt, 1, 7, 4, TimeSpan.FromSeconds(2)); break;
        }
        alignment.Seal(TimeSpan.FromSeconds(failure is "duration" ? 2 : 4), profileQualified: failure is not "unqualified");
        alignment.Available.Should().BeFalse();
        alignment.At(TimeSpan.FromSeconds(3)).Should().BeNull();
    }

    [Fact]
    public async Task Retired_prompt_callbacks_cannot_mutate_a_sealed_timeline()
    {
        var prompt = new Prompt("First.");
        var alignment = new WindowsTextToSpeechService.SentenceAlignment(prompt, 1, "First.");
        alignment.Observe(prompt, 1, 0, 5, TimeSpan.Zero);
        alignment.Close();
        await Task.Run(() => alignment.Observe(prompt, 1, -1, -1, TimeSpan.FromTicks(-1)),
            TestContext.Current.CancellationToken);
        alignment.Seal(TimeSpan.FromSeconds(1), true);
        alignment.At(TimeSpan.Zero)!.Start.Should().Be(0);
    }
}
