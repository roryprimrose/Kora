namespace Kora.Core.Context;

public static class ClipboardLiteralFind
{
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
