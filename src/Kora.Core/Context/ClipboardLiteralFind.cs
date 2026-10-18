namespace Kora.Core.Context;

public static class ClipboardLiteralFind
{
    public static (ClipboardLiteralMatch? Match, int Count, int Ordinal) FindCounted(
        ClipboardSnapshot snapshot, string query, SnapshotFindDirection direction,
        ClipboardLiteralMatch? selected = null)
    {
        ValidateSource(snapshot, direction, selected);
        var result = SnapshotLiteralFind.FindCounted(snapshot.Text, query, direction, selected?.Span);
        return (result.Match is null ? null : new(snapshot, result.Match), result.Count, result.Ordinal);
    }

    public static ClipboardLiteralMatch? Find(ClipboardSnapshot snapshot, string query,
        SnapshotFindDirection direction, ClipboardLiteralMatch? selected = null)
    {
        ValidateSource(snapshot, direction, selected);
        var span = SnapshotLiteralFind.Find(snapshot.Text, query, direction, selected?.Span);
        return span is null ? null : new(snapshot, span);
    }

    private static void ValidateSource(ClipboardSnapshot snapshot, SnapshotFindDirection direction,
        ClipboardLiteralMatch? selected)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (direction != SnapshotFindDirection.First && selected is not null
            && !ReferenceEquals(selected.Source, snapshot))
        {
            throw new InvalidDataException("Literal Find cannot navigate a different clipboard snapshot.");
        }
    }
}
