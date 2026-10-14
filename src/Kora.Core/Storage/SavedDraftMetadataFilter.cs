using System.Globalization;

using Kora.Core.Conversation;

namespace Kora.Core.Storage;

/// <summary>Content-free saved-draft predicates; empty native fields mean Any, never repaired input.</summary>
public sealed record SavedDraftMetadataFilter
{
    public static SavedDraftMetadataFilter Any { get; } = new(null, null, null);

    public SavedDraftMetadataFilter(long? draftRevision, long? minimumUtf8Bytes, long? maximumUtf8Bytes)
    {
        if (draftRevision is <= 0
            || minimumUtf8Bytes is < 0 or > ConversationText.MaximumUtf8Bytes
            || maximumUtf8Bytes is < 0 or > ConversationText.MaximumUtf8Bytes
            || minimumUtf8Bytes > maximumUtf8Bytes)
        { throw new InvalidDataException("Saved draft revision must be positive; UTF-8 bounds must be within the conversation maximum and minimum <= maximum."); }
        DraftRevision = draftRevision;
        MinimumUtf8Bytes = minimumUtf8Bytes;
        MaximumUtf8Bytes = maximumUtf8Bytes;
    }

    public long? DraftRevision { get; }
    public long? MinimumUtf8Bytes { get; }
    public long? MaximumUtf8Bytes { get; }

    public bool Matches(SavedDraftSummary row) =>
        (DraftRevision is null || row.DraftRevision == DraftRevision)
        && (MinimumUtf8Bytes is null || row.Utf8Bytes >= MinimumUtf8Bytes)
        && (MaximumUtf8Bytes is null || row.Utf8Bytes <= MaximumUtf8Bytes);

    public static SavedDraftMetadataFilter Parse(string revision, string minimum, string maximum) =>
        new(ParseInteger(revision), ParseInteger(minimum), ParseInteger(maximum));

    public static bool IsValid(string revision, string minimum, string maximum)
    {
        try { _ = Parse(revision, minimum, maximum); return true; }
        catch (InvalidDataException) { return false; }
    }

    private static long? ParseInteger(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) { return null; }
        if (text.Any(character => character is < '0' or > '9')
            || !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        { throw new InvalidDataException("Enter invariant decimal integers only; no signs, whitespace, separators or overflow. Empty means Any."); }
        return value;
    }
}
