using AwesomeAssertions;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Core.UnitTests.Configuration;

public sealed class SpokenSummaryLimitsTests
{
    [Fact]
    public void Defaults_exact_bounds_and_per_option_reset_preserve_the_companion()
    {
        SpokenSummaryLimits.Default.Should().Be(new SpokenSummaryLimits(3, 80));
        SpokenSummaryLimits.Default.Validate();
        var lower = SpokenSummaryLimits.Default.With(SpeechOption.SummarySentences, "1").With(SpeechOption.SummaryWords, "1");
        lower.Should().Be(new SpokenSummaryLimits(1, 1));
        lower.With(SpeechOption.SummarySentences, null).Should().Be(new SpokenSummaryLimits(3, 1));
        lower.With(SpeechOption.SummaryWords, null).Should().Be(new SpokenSummaryLimits(1, 80));
        lower.With(SpeechOption.SummarySentences, "3").With(SpeechOption.SummaryWords, "80").Should().Be(SpokenSummaryLimits.Default);
        var resetUnknown = () => lower.With(SpeechOption.Provider, null);
        resetUnknown.Should().Throw<ArgumentOutOfRangeException>();
        var setUnknown = () => lower.With(SpeechOption.Voice, "1");
        setUnknown.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 80)]
    [InlineData(4, 80)]
    [InlineData(3, 0)]
    [InlineData(3, 81)]
    public void Invalid_limits_fail_closed(int sentences, int words)
    {
        var validate = () => new SpokenSummaryLimits(sentences, words).Validate();
        validate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("1.0")]
    [InlineData("forty")]
    [InlineData("2147483648")]
    public void Values_are_exact_invariant_integers(string value)
    {
        var set = () => SpokenSummaryLimits.Default.With(SpeechOption.SummaryWords, value);
        set.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData(" \t\n?!...", 0, 0)]
    [InlineData("One. Two! Three?", 3, 3)]
    [InlineData("One?!... Two", 2, 2)]
    [InlineData("\"One!\" 'Two?' (Three.) Four", 4, 4)]
    [InlineData("One\nTwo\r\nThree", 1, 3)]
    [InlineData("Dr. Smith met Mr. Jones. Next.", 2, 6)]
    [InlineData("Mrs. Smith and Ms. Jones met Prof. Grey, Sr. and Jr. today.", 1, 12)]
    [InlineData("Examples e.g. one, i.e. two, etc. remain here.", 1, 10)]
    [InlineData("U.S. and U.K. J. Smith: 3.14.", 1, 9)]
    [InlineData("Dr.foo. Next.", 3, 3)]
    [InlineData("DR. Smith. Next.", 2, 3)]
    [InlineData("Dr.", 1, 1)]
    [InlineData("Dr.. Next", 2, 2)]
    [InlineData("AlphabetDr. Next.", 2, 2)]
    [InlineData("a......b. Next.", 3, 3)]
    [InlineData("\u03A9. Next.", 2, 2)]
    [InlineData("7. Next.", 2, 2)]
    [InlineData("a\u20DD b\u093E c", 1, 3)]
    [InlineData("こんにちは。世界！続き？末尾．", 4, 4)]
    [InlineData("One！Two？Three。", 3, 3)]
    [InlineData("caf\u00e9 cafe\u0301 \U00010400 \U0001D7D8", 1, 4)]
    [InlineData("can't well-being rock\u2019n\u2019roll non\u2010breaking non\u2011breaking", 1, 5)]
    [InlineData("\u0301lead --word word--join trailing- 'quote' /path/file_name@example.com", 2, 10)]
    public void Unicode_punctuation_abbreviations_and_fragments_have_documented_counts(string text, int sentences, int words) =>
        SpokenSummaryMeasure.Count(text).Should().Be(new SpokenSummaryMeasure(sentences, words));

    [Fact]
    public void Both_exact_thresholds_pass_and_either_over_threshold_fails()
    {
        var eighty = string.Join(' ', Enumerable.Repeat("word", 78)) + ". Two. Three.";
        var measured = SpokenSummaryMeasure.Count(eighty);
        measured.Should().Be(new SpokenSummaryMeasure(3, 80));
        measured.Fits(SpokenSummaryLimits.Default).Should().BeTrue();
        SpokenSummaryMeasure.Count(eighty + " extra").Fits(SpokenSummaryLimits.Default).Should().BeFalse();
        SpokenSummaryMeasure.Count("One. Two. Three. Four.").Fits(SpokenSummaryLimits.Default).Should().BeFalse();
        SpokenSummaryMeasure.Count("One. Two.").Fits(new(1, 80)).Should().BeFalse();
        SpokenSummaryMeasure.Count("One two").Fits(new(3, 1)).Should().BeFalse();
        var missingText = () => SpokenSummaryMeasure.Count(null!);
        missingText.Should().Throw<ArgumentNullException>();
        var missingLimits = () => measured.Fits(null!);
        missingLimits.Should().Throw<ArgumentNullException>();
        var invalidLimits = () => measured.Fits(new(0, 80));
        invalidLimits.Should().Throw<ArgumentOutOfRangeException>();
    }
}
