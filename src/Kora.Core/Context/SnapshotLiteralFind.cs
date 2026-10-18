using System.Globalization;
using System.Text;

namespace Kora.Core.Context;

public static class SnapshotLiteralFind
{
    public static (SnapshotLiteralSpan? Match, int Count, int Ordinal) FindCounted(
        string text, string query, SnapshotFindDirection direction, SnapshotLiteralSpan? selected = null)
    {
        var match = Find(text, query, direction, selected);
        var count = 0;
        var ordinal = 0;
        var selectedOrdinal = 0;
        SnapshotLiteralSpan? previous = null;
        var current = Find(text, query, SnapshotFindDirection.First);
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
            current = Find(text, query, SnapshotFindDirection.Next, current);
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
        SnapshotFindDirection direction, SnapshotLiteralSpan? selected = null)
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
        if (direction != SnapshotFindDirection.First && (selected is null
            || selected.Length != query.Length || selected.Start < 0 || selected.Start > text.Length - query.Length
            || !text.AsSpan(selected.Start, selected.Length).SequenceEqual(query)))
        {
            throw new InvalidDataException("The selected literal span is not current for this exact text and query.");
        }
        var start = direction switch
        {
            SnapshotFindDirection.First => text.IndexOf(query, StringComparison.Ordinal),
            SnapshotFindDirection.Next => text.IndexOf(query, selected!.Start + selected.Length, StringComparison.Ordinal),
            _ => text.AsSpan(0, selected!.Start).LastIndexOf(query.AsSpan(), StringComparison.Ordinal),
        };
        return start < 0 ? null : new(start, query.Length);
    }
}
