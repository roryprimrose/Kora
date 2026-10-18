namespace Kora.Core.Context;

public static class ClipboardLiteralFind
{
    public static (ClipboardLiteralMatch? Match, int Count, int Ordinal) FindCounted(
        ClipboardSnapshot snapshot, string query, SnapshotFindDirection direction,
        ClipboardLiteralMatch? selected = null)
    {
        var match = Find(snapshot, query, direction, selected);
        var count = 0;
        var ordinal = 0;
        var selectedOrdinal = 0;
        ClipboardLiteralMatch? previous = null;
        var current = Find(snapshot, query, SnapshotFindDirection.First);
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
            current = Find(snapshot, query, SnapshotFindDirection.Next, current);
        }
        if (direction != SnapshotFindDirection.First && selectedOrdinal == 0)
        {
            throw new InvalidDataException("The selected literal span is not in this clipboard snapshot's nonoverlapping sequence.");
        }
        return (match, count, match is null ? selectedOrdinal : ordinal);
    }

    public static ClipboardLiteralMatch? Find(ClipboardSnapshot snapshot, string query,
        SnapshotFindDirection direction, ClipboardLiteralMatch? selected = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (direction != SnapshotFindDirection.First && selected is not null
            && !ReferenceEquals(selected.Source, snapshot))
        {
            throw new InvalidDataException("Literal Find cannot navigate a different clipboard snapshot.");
        }
        var span = SnapshotLiteralFind.Find(snapshot.Text, query, direction, selected?.Span);
        return span is null ? null : new(snapshot, span);
    }
}
