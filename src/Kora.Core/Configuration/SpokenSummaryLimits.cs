using System.Globalization;

namespace Kora.Core.Configuration;

public sealed record SpokenSummaryLimits(int Sentences, int Words)
{
    public const int MaximumSentences = 3;
    public const int MaximumWords = 80;
    public static SpokenSummaryLimits Default { get; } = new(MaximumSentences, MaximumWords);

    public void Validate()
    {
        if (Sentences is < 1 or > MaximumSentences || Words is < 1 or > MaximumWords)
        {
            throw new ArgumentOutOfRangeException(null, "Summary limits must be 1-3 sentences and 1-80 words.");
        }
    }

    public SpokenSummaryLimits With(SpeechOption option, string? value)
    {
        var number = value is null ? option switch
        {
            SpeechOption.SummarySentences => MaximumSentences,
            SpeechOption.SummaryWords => MaximumWords,
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        } : int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new ArgumentOutOfRangeException(nameof(value), "Use an exact positive integer.");
        var result = option switch
        {
            SpeechOption.SummarySentences => this with { Sentences = number },
            SpeechOption.SummaryWords => this with { Words = number },
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        };
        result.Validate();
        return result;
    }
}
