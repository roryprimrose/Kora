using System.Globalization;

using Kora.Core.Conversation;

namespace Kora.Application.Presentation;

/// <summary>Passive, content-free draft feedback shared by native composer surfaces.</summary>
public static class ComposerDraftSizeFeedback
{
    public static string Format(ConversationTextMeasurement? measurement)
    {
        if (measurement is null) { return "Current draft metrics unavailable: private presentation is closed."; }
        var counts = measurement.IsWellFormed
            ? string.Create(CultureInfo.InvariantCulture, $"{measurement.Utf8Bytes} UTF-8 bytes; {measurement.UnicodeScalars} Unicode scalars (no scalar limit)")
            : "Malformed Unicode: UTF-8 bytes and Unicode scalar count unavailable";
        var shape = !measurement.IsWellFormed ? "ill-formed Unicode"
            : !measurement.IsWithinLimits ? "over limit"
            : measurement.HasUnsupportedContent ? "unsupported control or binary content" : "valid";
        var submit = measurement.IsSaveShapeValid && !measurement.IsSubmissionShapeValid
            ? "empty/whitespace cannot be submitted" : shape;
        return string.Create(CultureInfo.InvariantCulture,
            $"Current draft: {counts}; {measurement.Utf16CodeUnits} UTF-16 code units. Save and Submit maxima: {ConversationText.MaximumUtf8Bytes} UTF-8 bytes / {ConversationText.MaximumUtf16Characters} UTF-16 code units each. Save shape: {shape}; Submit shape: {submit}. Size/Unicode only; credential checks and target/authority admission remain separate.");
    }
}
