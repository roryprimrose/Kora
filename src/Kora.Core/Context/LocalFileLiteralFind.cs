using System.Globalization;
using System.Text;

namespace Kora.Core.Context;

public static class LocalFileLiteralFind
{
    public static bool IsValidQuery(string query) =>
        LocalFileRetrievalPolicy.IsBoundedQuery(query)
        && !query.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control
            or UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);

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
        if (direction != LocalFileFindDirection.First && (selected is null || selected.Source != file.Reference
            || selected.Length != query.Length || selected.Start < 0 || selected.Start > file.Text.Length - query.Length
            || !file.Text.AsSpan(selected.Start, selected.Length).SequenceEqual(query)))
        {
            throw new InvalidDataException("The selected literal span is not current for this exact source and query.");
        }
        var start = direction switch
        {
            LocalFileFindDirection.First => file.Text.IndexOf(query, StringComparison.Ordinal),
            LocalFileFindDirection.Next => file.Text.IndexOf(query, selected!.Start + selected.Length, StringComparison.Ordinal),
            _ => file.Text.AsSpan(0, selected!.Start).LastIndexOf(query.AsSpan(), StringComparison.Ordinal),
        };
        return start < 0 ? null : new(file.Reference, start, query.Length);
    }
}