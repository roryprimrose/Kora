using System.Globalization;
using System.Text;

namespace Kora.Core.Conversation;

/// <summary>Exact intentional user content, never normalized, truncated or treated as host instructions.</summary>
public static class ConversationText
{
    public const int MaximumUtf16Characters = 4096;
    public const int MaximumUtf8Bytes = 16384;
    public const int MaximumSessionMessages = 100;
    public const int MaximumPageRecords = 50;
    public const int MaximumPageUtf8Bytes = 65536;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>Applies the shared reject-only credential policy without imposing conversation retention limits.</summary>
    public static void RequireNoCredentials(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ConversationCredentialScreening.RequireAccepted(text);
    }

    public static void Validate(string text, bool submission = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        ConversationTextMeasurement measurement;
        try
        {
            measurement = MeasureWellFormed(text);
            if (measurement.Utf8Bytes > MaximumUtf8Bytes)
            {
                throw new InvalidDataException("Conversation text exceeds 16384 UTF-8 bytes; nothing was truncated.");
            }
        }
        catch (EncoderFallbackException exception)
        {
            throw new InvalidDataException("Conversation text contains malformed Unicode.", exception);
        }
        if (text.Length > MaximumUtf16Characters)
        {
            throw new InvalidDataException("Conversation text exceeds 4096 UTF-16 characters; nothing was truncated.");
        }
        if (measurement.HasUnsupportedContent)
        {
            throw new InvalidDataException("Conversation text contains unsupported control or binary content.");
        }
        if (submission && measurement.IsWhiteSpace)
        {
            throw new InvalidDataException("An empty conversation message cannot be submitted.");
        }
        RequireNoCredentials(text);
    }

    /// <summary>Measures without retention, normalization, credential screening or input repair.</summary>
    public static ConversationTextMeasurement Measure(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try { return MeasureWellFormed(text); }
        catch (EncoderFallbackException)
        {
            // No UTF-8 byte or scalar total exists for malformed UTF-16; never count replacement characters.
            return new(text.Length, null, null, false, string.IsNullOrWhiteSpace(text));
        }
    }

    private static ConversationTextMeasurement MeasureWellFormed(string text)
    {
        var bytes = StrictUtf8.GetByteCount(text);
        var scalars = 0;
        var unsupported = false;
        foreach (var rune in text.EnumerateRunes())
        {
            scalars++;
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control
                && rune.Value is not ('\t' or '\r' or '\n')
                || rune.Value is 0xfffe or 0xffff)
            { unsupported = true; }
        }
        return new(text.Length, bytes, scalars, unsupported, string.IsNullOrWhiteSpace(text));
    }
}
