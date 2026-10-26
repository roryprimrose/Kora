using Kora.Core.Conversation;

namespace Kora.Core.Voice;

/// <summary>A complete replace-only, untrusted hypothesis, not a transcript fragment or a final input.</summary>
public sealed record ProvisionalTranscriptSnapshot(long Generation, long Sequence, string Text)
{
    public const int MaximumUtf16Characters = 512;
    public const int MaximumUtf8Bytes = 1024;

    public bool IsValid()
    {
        if (Sequence <= 0 || Text.Length > MaximumUtf16Characters) { return false; }
        var measurement = ConversationText.Measure(Text);
        return measurement.IsWellFormed && measurement.Utf8Bytes <= MaximumUtf8Bytes
            && !measurement.HasUnsupportedContent;
    }
}
