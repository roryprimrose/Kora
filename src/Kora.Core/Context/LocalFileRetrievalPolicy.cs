using System.Globalization;
using System.Text;

namespace Kora.Core.Context;

public static class LocalFileRetrievalPolicy
{
    public const string Version = "lexical-lines-v1";
    public const int MaximumQueryCharacters = 256;
    public const int MaximumQueryUtf8Bytes = 512;
    public const int MaximumTerms = 32;
    public const int MaximumTermCharacters = 64;
    public const int MaximumChunkCharacters = 2048;
    public const int MaximumChunkLines = 128;
    public const int MaximumCitations = 8;
    public const int MaximumExcerptUtf8Bytes = 16 * 1024;
    public const int MaximumTermFrequency = 16;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static bool TryQuery(string query, out IReadOnlySet<string> terms)
    {
        ArgumentNullException.ThrowIfNull(query);
        terms = new HashSet<string>(StringComparer.Ordinal);
        if (query.Length is 0 or > MaximumQueryCharacters) { return false; }
        try
        {
            if (Utf8.GetByteCount(query) > MaximumQueryUtf8Bytes) { return false; }
        }
        catch (EncoderFallbackException) { return false; }
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in Tokens(query, CancellationToken.None))
        {
            if (token.Length == 0) { return false; }
            unique.Add(token);
            if (unique.Count > MaximumTerms) { return false; }
        }
        terms = unique;
        return unique.Count != 0;
    }

    // Oversized source words are skipped whole, never indexed as a matching prefix/suffix.
    internal static IEnumerable<string> Tokens(string text, CancellationToken token)
    {
        var word = new StringBuilder(MaximumTermCharacters);
        var oversize = false;
        foreach (var rune in text.EnumerateRunes())
        {
            token.ThrowIfCancellationRequested();
            var category = Rune.GetUnicodeCategory(rune);
            var continuation = Rune.IsLetterOrDigit(rune)
                || ((word.Length != 0 || oversize) && category is UnicodeCategory.NonSpacingMark
                    or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark);
            if (continuation)
            {
                if (!oversize && word.Length + rune.Utf16SequenceLength <= MaximumTermCharacters) { word.Append(rune); }
                else { oversize = true; }
            }
            else if (word.Length != 0 || oversize)
            {
                yield return oversize ? string.Empty : word.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
                word.Clear();
                oversize = false;
            }
        }
        if (word.Length != 0 || oversize)
        {
            yield return oversize ? string.Empty : word.ToString().Normalize(NormalizationForm.FormC).ToUpperInvariant();
        }
    }
}
