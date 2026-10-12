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

    public static void Validate(string text, bool submission = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            if (StrictUtf8.GetByteCount(text) > MaximumUtf8Bytes)
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
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control
                && rune.Value is not ('\t' or '\r' or '\n')
                || rune.Value is 0xfffe or 0xffff)
            {
                throw new InvalidDataException("Conversation text contains unsupported control or binary content.");
            }
        }
        if (submission && string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidDataException("An empty conversation message cannot be submitted.");
        }
    }
}
