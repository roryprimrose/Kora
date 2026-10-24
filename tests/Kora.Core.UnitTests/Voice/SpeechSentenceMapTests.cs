using AwesomeAssertions;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Voice;

public sealed class SpeechSentenceMapTests
{
    [Theory]
    [InlineData("  Hello! \r\n\"Next?\"  Last", "  Hello! \r\n", "\"Next?\"  ", "Last")]
    [InlineData("Value 3.14 is ready. Next.", "Value 3.14 is ready. ", "Next.")]
    [InlineData("Hello?!  Next...", "Hello?!  ", "Next...")]
    [InlineData("Hello!!?? Next.", "Hello!!?? ", "Next.")]
    [InlineData("First.) Next.] Last.'", "First.) ", "Next.] ", "Last.'")]
    [InlineData("Caf\u00e9 \ud83d\ude00! \u4f60\u597d?", "Caf\u00e9 \ud83d\ude00! ", "\u4f60\u597d?")]
    [InlineData("No terminal punctuation", "No terminal punctuation")]
    public void Versioned_ranges_preserve_every_exact_source_character(string source, params string[] expected)
    {
        var map = SpeechSentenceMap.Create(source)!;
        map.Sentences.Select(map.GetText).Should().Equal(expected);
        string.Concat(map.Sentences.Select(map.GetText)).Should().Be(source);
        SpeechSentenceMap.Version.Should().Be(1);
        map.Sentences.Select((range, index) => range.Index == index).Should().OnlyContain(value => value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t\r\n")]
    [InlineData("bad\0text")]
    [InlineData("Dr. Smith.")]
    [InlineData("Mr. Smith.")]
    [InlineData("Mrs. Smith.")]
    [InlineData("Ms. Smith.")]
    [InlineData("Prof. Smith.")]
    [InlineData("Words etc. More.")]
    [InlineData("Words vs. More.")]
    [InlineData("A. Name.")]
    public void Unsupported_or_ambiguous_text_has_no_map(string source) => SpeechSentenceMap.Create(source).Should().BeNull();

    [Fact]
    public void Malformed_utf16_is_tested_without_theory_argument_serialization()
    {
        SpeechSentenceMap.Create(new string('\ud800', 1)).Should().BeNull();
        SpeechSentenceMap.Create(new string('\ud800', 1) + "x").Should().BeNull();
        SpeechSentenceMap.Create(new string('\udc00', 1)).Should().BeNull();
    }

    [Fact]
    public void Exact_bounds_surrogates_ranges_and_positions_are_validated()
    {
        SpeechSentenceMap.Create(new string('x', SpeechSentenceMap.MaximumCharacters)).Should().NotBeNull();
        SpeechSentenceMap.Create(new string('x', SpeechSentenceMap.MaximumCharacters + 1)).Should().BeNull();
        SpeechSentenceMap.Create(string.Concat(Enumerable.Repeat("Word. ", SpeechSentenceMap.MaximumSentences)))!
            .Sentences.Should().HaveCount(SpeechSentenceMap.MaximumSentences);
        SpeechSentenceMap.Create(string.Concat(Enumerable.Repeat("Word. ", SpeechSentenceMap.MaximumSentences + 1))).Should().BeNull();
        SpeechSentenceMap.Create(string.Concat(Enumerable.Repeat("Word. ", SpeechSentenceMap.MaximumSentences)) + "Last").Should().BeNull();
        var map = SpeechSentenceMap.Create("Hi \ud83d\ude00.")!;
        map.IsCharacterBoundary(-1).Should().BeFalse();
        map.IsCharacterBoundary(7).Should().BeFalse();
        map.IsCharacterBoundary(6).Should().BeTrue();
        map.IsCharacterBoundary(3).Should().BeTrue();
        map.IsCharacterBoundary(4).Should().BeFalse();
        map.Invoking(value => value.GetText(new(-1, 0, 1))).Should().Throw<ArgumentOutOfRangeException>();
        map.Invoking(value => value.GetText(new(1, 0, 1))).Should().Throw<ArgumentOutOfRangeException>();
        map.Invoking(value => value.GetText(new(0, 1, 1))).Should().Throw<ArgumentOutOfRangeException>();
        map.Invoking(value => value.GetText(null!)).Should().Throw<ArgumentNullException>();
        var position = new SpeechSentencePosition(0, 0, 6, TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        map.Matches(position).Should().BeTrue();
        foreach (var malformed in new[]
        {
            position with { Index = -1 }, position with { Index = 1 }, position with { Start = 1 },
            position with { AudioStart = TimeSpan.FromTicks(-1) },
            position with { AudioStart = TimeSpan.FromSeconds(2) },
            position with { PlaybackPosition = TimeSpan.FromSeconds(3) },
            position with { AudioStart = TimeSpan.FromSeconds(2), PlaybackPosition = TimeSpan.FromSeconds(2) },
        })
        {
            map.Matches(malformed).Should().BeFalse();
        }
        Action nullSource = () => SpeechSentenceMap.Create(null!);
        nullSource.Should().Throw<ArgumentNullException>();
    }
}
