using System.Globalization;
using System.Text;

namespace Kora.Core.Context;

public static class SnapshotLiteralFind
{
    public static (SnapshotLiteralSpan? Match, int Count, int Ordinal) FindCounted(
        string text, string query, SnapshotFindDirection direction, SnapshotLiteralSpan? selected = null,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var match = Find(text, query, direction, selected, comparison);
        var count = 0;
        var ordinal = 0;
        var selectedOrdinal = 0;
        SnapshotLiteralSpan? previous = null;
        var current = Find(text, query, SnapshotFindDirection.First, comparison: comparison);
        while (current is not null)
        {
            count++;
            if (current == selected)
            {
                selectedOrdinal = count;
                if (direction == SnapshotFindDirection.Previous)
                {
                    // Reverse navigation follows the counted forward sequence, not overlapping alternatives.
                    match = previous;
                    ordinal = count - 1;
                }
            }
            if (current == match) { ordinal = count; }
            previous = current;
            current = Find(text, query, SnapshotFindDirection.Next, current, comparison);
        }
        if (direction != SnapshotFindDirection.First && selectedOrdinal == 0)
        {
            throw new InvalidDataException("The selected literal span is not in this exact text's nonoverlapping sequence.");
        }
        return (match, count, match is null ? selectedOrdinal : ordinal);
    }

    public static bool IsValidQuery(string query) =>
        LocalFileRetrievalPolicy.IsBoundedQuery(query)
        && !query.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control
            or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

    public static SnapshotLiteralSpan? Find(string text, string query,
        SnapshotFindDirection direction, SnapshotLiteralSpan? selected = null,
        StringComparison comparison = StringComparison.Ordinal)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsValidQuery(query))
        {
            throw new InvalidDataException("Literal Find requires nonempty, bounded, well-formed, one-line text without unsafe controls.");
        }
        if (direction is not (SnapshotFindDirection.First or SnapshotFindDirection.Next or SnapshotFindDirection.Previous))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        if (comparison is not (StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(comparison));
        }
        if (direction != SnapshotFindDirection.First && (selected is null
            || selected.Comparison != comparison
            || (comparison == StringComparison.OrdinalIgnoreCase
                && (!string.Equals(selected.SourceText, text, StringComparison.Ordinal)
                    || !string.Equals(selected.Query, query, StringComparison.Ordinal)))
            || selected.Length != query.Length || selected.Start < 0 || selected.Start > text.Length - query.Length
            || !text.AsSpan(selected.Start, selected.Length).Equals(query.AsSpan(), comparison)))
        {
            throw new InvalidDataException("The selected literal span is not current for this exact text and query.");
        }
        var start = direction switch
        {
            SnapshotFindDirection.First => text.IndexOf(query, comparison),
            SnapshotFindDirection.Next => text.IndexOf(query, selected!.Start + selected.Length, comparison),
            _ => text.AsSpan(0, selected!.Start).LastIndexOf(query.AsSpan(), comparison),
        };
        return start < 0 ? null : comparison == StringComparison.Ordinal
            ? new(start, query.Length)
            : new(start, query.Length) { Comparison = comparison, SourceText = text, Query = query };
    }
}
