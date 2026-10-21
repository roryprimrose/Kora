using System.Text;

namespace Kora.Core.Presentation;

public sealed record DecodedTextMeasurement
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private DecodedTextMeasurement(int utf16CodeUnits, int? unicodeScalars, int? strictUtf8Bytes)
    {
        Utf16CodeUnits = utf16CodeUnits;
        UnicodeScalars = unicodeScalars;
        StrictUtf8Bytes = strictUtf8Bytes;
    }

    public int Utf16CodeUnits { get; }
    public int? UnicodeScalars { get; }
    public int? StrictUtf8Bytes { get; }

    public static DecodedTextMeasurement Measure(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        int bytes;
        try { bytes = StrictUtf8.GetByteCount(text); }
        catch (EncoderFallbackException)
        {
            return new(text.Length, null, null);
        }

        // Strict encoding must succeed before EnumerateRunes can substitute malformed UTF-16.
        var scalars = 0;
        foreach (var rune in text.EnumerateRunes()) { scalars++; }
        return new(text.Length, scalars, bytes);
    }
}
