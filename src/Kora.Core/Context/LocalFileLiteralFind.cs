namespace Kora.Core.Context;

public static class LocalFileLiteralFind
{
    public static bool IsValidQuery(string query) => SnapshotLiteralFind.IsValidQuery(query);

    public static (LocalFileLiteralMatch? Match, int Count, int Ordinal) FindCounted(
        LocalFileRevision file, string query, LocalFileFindDirection direction, LocalFileLiteralMatch? selected = null,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var navigation = ValidateSource(file, query, direction, selected);
        var result = SnapshotLiteralFind.FindCounted(file.Text, query, navigation,
            ToSpan(selected), comparison);
        return (ToMatch(file, result.Match),
            result.Count, result.Ordinal);
    }

    public static LocalFileLiteralMatch? Find(LocalFileRevision file, string query,
        LocalFileFindDirection direction, LocalFileLiteralMatch? selected = null,
        StringComparison comparison = StringComparison.Ordinal)
    {
        var navigation = ValidateSource(file, query, direction, selected);
        var span = SnapshotLiteralFind.Find(file.Text, query, navigation,
            ToSpan(selected), comparison);
        return ToMatch(file, span);
    }

    private static SnapshotLiteralSpan? ToSpan(LocalFileLiteralMatch? selected) => selected is null ? null
        : selected.ComparisonSpan is { } span ? span with { Start = selected.Start, Length = selected.Length }
        : new(selected.Start, selected.Length);

    private static LocalFileLiteralMatch? ToMatch(LocalFileRevision file, SnapshotLiteralSpan? span) =>
        span is null ? null : new(file.Reference, span.Start, span.Length)
        {
            ComparisonSpan = span.Comparison == StringComparison.Ordinal ? null : span,
        };

    private static SnapshotFindDirection ValidateSource(LocalFileRevision file, string query,
        LocalFileFindDirection direction, LocalFileLiteralMatch? selected)
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
        return direction switch
        {
            LocalFileFindDirection.First => SnapshotFindDirection.First,
            LocalFileFindDirection.Next => SnapshotFindDirection.Next,
            _ => SnapshotFindDirection.Previous,
        };
    }
}