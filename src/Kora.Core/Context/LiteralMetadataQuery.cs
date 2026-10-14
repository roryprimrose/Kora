using System.Globalization;
using System.Text;

namespace Kora.Core.Context;

/// <summary>Validates volatile literal metadata queries without normalization or truncation.</summary>
public static class LiteralMetadataQuery
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static void Validate(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length > LocalFileRetrievalPolicy.MaximumQueryCharacters)
        { throw new InvalidDataException("Use at most 256 UTF-16 units / 512 UTF-8 bytes for the literal metadata query."); }
        try
        {
            if (Utf8.GetByteCount(query) > LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes
                || query.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control
                    or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator))
            { throw new InvalidDataException("Use a bounded single-line literal metadata query without control or format characters."); }
        }
        catch (EncoderFallbackException exception)
        { throw new InvalidDataException("The metadata query must contain valid Unicode.", exception); }
    }
}
