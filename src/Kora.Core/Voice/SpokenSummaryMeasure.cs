using System.Globalization;
using System.Text;
using Kora.Core.Configuration;

namespace Kora.Core.Voice;

public sealed record SpokenSummaryMeasure(int Sentences, int Words)
{
    private static readonly string[] Abbreviations =
        ["Mr.", "Mrs.", "Ms.", "Dr.", "Prof.", "Sr.", "Jr.", "e.g.", "i.e.", "etc.", "U.S.", "U.K."];
    private static readonly int MaximumAbbreviationLength = Abbreviations.Max(item => item.Length);

    public bool Fits(SpokenSummaryLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        return Sentences <= limits.Sentences && Words <= limits.Words;
    }

    public static SpokenSummaryMeasure Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = 0;
        var sentences = 0;
        var inWord = false;
        var hasWords = false;
        var offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsLetterOrDigit(rune))
            {
                if (!inWord) { words++; }
                inWord = true;
                hasWords = true;
            }
            else if (category is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.EnclosingMark) && rune.Value is not ('\'' or 0x2019 or '-' or 0x2010 or 0x2011))
            {
                inWord = false;
            }
            if (rune.Value is '.' or '!' or '?' or 0x3002 or 0xFF01 or 0xFF1F or 0xFF0E
                && !(rune.Value == '.' && IsNonterminalPeriod(text, offset)) && hasWords)
            {
                sentences++;
                hasWords = false;
            }
            offset += rune.Utf16SequenceLength;
        }
        if (hasWords) { sentences++; }
        return new(sentences, words);
    }

    private static bool IsNonterminalPeriod(string text, int index)
    {
        if (index > 0 && index + 1 < text.Length && char.IsDigit(text[index - 1]) && char.IsDigit(text[index + 1]))
        {
            return true;
        }
        var start = index;
        while (start > 0 && index - start < MaximumAbbreviationLength
            && (char.IsLetter(text[start - 1]) || text[start - 1] == '.')) { start--; }
        if (start > 0 && (char.IsLetter(text[start - 1]) || text[start - 1] == '.')) { return false; }
        var token = text.AsSpan(start);
        foreach (var abbreviation in Abbreviations)
        {
            if (token.StartsWith(abbreviation, StringComparison.OrdinalIgnoreCase)
                && index - start < abbreviation.Length
                && (token.Length == abbreviation.Length || !char.IsLetterOrDigit(token[abbreviation.Length])))
            {
                return true;
            }
        }
        return index - start == 1 && char.IsAsciiLetter(text[start]);
    }
}
