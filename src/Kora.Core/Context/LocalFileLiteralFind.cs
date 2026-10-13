namespace Kora.Core.Context;

public static class LocalFileLiteralFind
{
    public static bool IsValidQuery(string query) => SnapshotLiteralFind.IsValidQuery(query);

    public static LocalFileLiteralMatch? Find(LocalFileRevision file, string query,
        LocalFileFindDirection direction, LocalFileLiteralMatch? selected = null)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!IsValidQuery(query))
        {
            throw new InvalidDataException("Literal Find requires nonempty, bounded, well-formed, one-line text without unsafe controls.");
        }
        if (direction is not (LocalFileFindDirection.First or LocalFileFindDirection.Next or LocalFileFindDirection.Previous))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        if (direction != LocalFileFindDirection.First && (selected is null || selected.Source != file.Reference))
        {
            throw new InvalidDataException("The selected literal span is not current for this exact source and query.");
        }
        var navigation = direction switch
        {
            LocalFileFindDirection.First => SnapshotFindDirection.First,
            LocalFileFindDirection.Next => SnapshotFindDirection.Next,
            _ => SnapshotFindDirection.Previous,
        };
        var span = SnapshotLiteralFind.Find(file.Text, query, navigation,
            selected is null ? null : new(selected.Start, selected.Length));
        return span is null ? null : new(file.Reference, span.Start, span.Length);
    }
}