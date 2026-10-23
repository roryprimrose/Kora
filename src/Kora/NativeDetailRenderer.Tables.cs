using System.Globalization;
using System.Text;

using Kora.Core.Presentation;

using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace Kora;

internal sealed partial class NativeDetailRenderer
{
    private static string? ValidateTables(MarkdownDocument document, string source)
    {
        var cells = 0;
        var tableCount = 0;
        foreach (var table in document.Descendants<Table>())
        {
            tableCount++;
            if (table.Parent is not MarkdownDocument || table.Count is 0 or > NativeMarkdownTableProfile.MaximumRowsPerTable
                || table[0] is not TableRow { IsHeader: true } header
                || header.Count is 0 or > NativeMarkdownTableProfile.MaximumColumns
                || table.ColumnDefinitions.Count != header.Count)
            {
                return "Unsupported table structure or row/column limit; exact source fallback.";
            }
            foreach (var block in table)
            {
                if (block is not TableRow row || row.Count != header.Count
                    || row.IsHeader != ReferenceEquals(row, header))
                {
                    return "Malformed table rows; exact source fallback.";
                }
                foreach (var child in row)
                {
                    // Max-width normalization only pads rows. Padded cells have no paragraph;
                    // reject them rather than silently accepting a ragged source table.
                    if (child is not TableCell { ColumnSpan: 1, RowSpan: 1, Count: 1 } cell
                        || cell[0] is not ParagraphBlock)
                    {
                        return "Malformed or unsupported table cell; exact source fallback.";
                    }
                    if (++cells > NativeMarkdownTableProfile.MaximumCellsPerDocument)
                    {
                        return "Table document cell limit exceeded; exact source fallback.";
                    }
                }
            }
        }
        var separators = 0;
        foreach (var line in source.AsSpan().EnumerateLines())
        {
            if (line.Contains('|') && IsTableSeparator(line.Trim())) { separators++; }
        }
        if (separators != tableCount)
        {
            return "Malformed or unsupported table delimiter; exact source fallback.";
        }
        foreach (var paragraph in document.Descendants<ParagraphBlock>())
        {
            if (paragraph.Parent is not TableCell && paragraph.Span.Length > 0
                && source.AsSpan(paragraph.Span.Start, paragraph.Span.Length).Contains('|'))
            {
                return "Unsupported pipe-containing paragraph or malformed table; exact source fallback.";
            }
        }
        return null;
    }

    private static IEnumerable<NativeTextSection> ProjectTable(Table table, int number)
    {
        static string CellText(Block block)
        {
            var cell = (TableCell)block;
            var text = InlineText(((ParagraphBlock)cell[0]).Inline);
            if (Utf8.GetByteCount(text) > NativeMarkdownTableProfile.MaximumCellUtf8Bytes)
            {
                throw new InvalidDataException("Table cell semantic text exceeds its UTF-8 limit.");
            }
            return text;
        }
        var headers = ((TableRow)table[0]).Select(CellText).ToArray();
        var heading = new StringBuilder().Append(CultureInfo.InvariantCulture,
            $"Table {number}: {headers.Length} columns, {table.Count - 1} data rows. Headers:");
        for (var column = 0; column < headers.Length; column++)
        {
            heading.AppendLine().Append(CultureInfo.InvariantCulture,
                $"Column {column + 1}: {(headers[column].Length == 0 ? "(empty header)" : headers[column])}");
        }
        yield return new(heading.ToString(), NativeTextStyle.Table);
        for (var row = 1; row < table.Count; row++)
        {
            var text = new StringBuilder().Append(CultureInfo.InvariantCulture, $"Table {number}, data row {row}:");
            for (var column = 0; column < headers.Length; column++)
            {
                var value = CellText(((TableRow)table[row])[column]);
                text.AppendLine().Append(CultureInfo.InvariantCulture,
                    $"Column {column + 1} ({(headers[column].Length == 0 ? "empty header" : headers[column])}): ")
                    .Append(value.Length == 0 ? "(empty cell)" : value);
            }
            yield return new(text.ToString(), NativeTextStyle.Table);
        }
    }
}
