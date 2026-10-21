namespace Kora.Core.Conversation;

/// <summary>Content-free shape of an exact buffer; not credential clearance or admission authority.</summary>
public sealed record ConversationTextMeasurement(
    int Utf16CodeUnits, int? Utf8Bytes, int? UnicodeScalars, bool HasUnsupportedContent, bool IsWhiteSpace)
{
    public bool IsWellFormed => Utf8Bytes is not null;
    public bool IsWithinLimits => IsWellFormed && Utf8Bytes <= ConversationText.MaximumUtf8Bytes
        && Utf16CodeUnits <= ConversationText.MaximumUtf16Characters;
    public bool IsSaveShapeValid => IsWithinLimits && !HasUnsupportedContent;
    public bool IsSubmissionShapeValid => IsSaveShapeValid && !IsWhiteSpace;
}
