using System.Collections.Immutable;
using System.Globalization;

using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Kora;

internal sealed partial class NativeDetailRenderer
{
    private static NativeClipboardSelection ClipboardSelection(
        MarkdownDocument document, List<NativeTextSection> sections, string semantic, bool strikethrough)
    {
        var index = 0;
        var offset = 0;
        (NativeTextSection Section, int Start) Section()
        {
            if (index >= sections.Count) { throw new InvalidOperationException("Clipboard section mapping failed."); }
            var section = sections[index++];
            if (index > 1) { offset += Environment.NewLine.Length * 2; }
            var start = offset;
            offset += section.Text.Length;
            return (section, start);
        }
        NativeClipboardNode Group(NativeClipboardTag tag, ImmutableArray<NativeClipboardNode> children, long number = 1) =>
            new(children[0].Start, children[^1].End - children[0].Start, tag, children, number);

        ImmutableArray<NativeClipboardNode> Inlines(LeafBlock leaf, int start, string expected)
        {
            var spans = new List<NativeClipboardNode>();
            var text = InlineText(leaf.Inline, strikethrough ? new InlineStrikethroughProjection(0) : null,
                captureFormatting: (node, from, length) =>
                {
                    var tag = node switch
                    {
                        CodeInline => NativeClipboardTag.Code,
                        EmphasisInline { DelimiterChar: '~' } => NativeClipboardTag.Strike,
                        EmphasisInline { DelimiterCount: 2 } => NativeClipboardTag.Strong,
                        EmphasisInline => NativeClipboardTag.Emphasis,
                        _ => throw new InvalidOperationException("Unsupported clipboard inline mapping."),
                    };
                    if (length > 0) { spans.Add(new(start + from, length, tag, [])); }
                });
            if (!string.Equals(text, expected, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Clipboard semantic mapping failed.");
            }
            // Equal ranges are recorded inner-first by the projection's exit callbacks.
            var ordered = spans.AsEnumerable().Reverse().OrderBy(span => span.Start)
                .ThenByDescending(span => span.Length).ToArray();
            var next = 0;
            ImmutableArray<NativeClipboardNode> Children(int end)
            {
                var children = ImmutableArray.CreateBuilder<NativeClipboardNode>();
                while (next < ordered.Length && ordered[next].End <= end)
                {
                    var node = ordered[next++];
                    children.Add(node with { Children = Children(node.End) });
                }
                return children.ToImmutable();
            }
            return Children(start + text.Length);
        }
        NativeClipboardNode TableRow(TableRow row, NativeTextSection section, int start)
        {
            var table = section.Table!.Data;
            var rowNumber = row.IsHeader ? 0 : ((Table)row.Parent!).IndexOf(row);
            var values = row.IsHeader ? table.Headers : table.Rows[rowNumber - 1];
            var cursor = section.Text.IndexOf(Environment.NewLine, StringComparison.Ordinal);
            var cells = ImmutableArray.CreateBuilder<NativeClipboardNode>();
            for (var column = 0; column < row.Count; column++)
            {
                var from = column == 0 ? 0 : cursor;
                var label = row.IsHeader
                    ? $"Column {column + 1}: "
                    : $"Column {column + 1} ({(table.Headers[column].Length == 0 ? "empty header" : table.Headers[column])}): ";
                var valueStart = cursor + Environment.NewLine.Length + label.Length;
                var value = values[column];
                var displayed = value.Length == 0 ? row.IsHeader ? "(empty header)" : "(empty cell)" : value;
                cursor = valueStart + displayed.Length;
                if (cursor > section.Text.Length
                    || !section.Text.AsSpan(valueStart, displayed.Length).SequenceEqual(displayed))
                {
                    throw new InvalidOperationException("Clipboard table mapping failed.");
                }
                var inline = Inlines((LeafBlock)((TableCell)row[column])[0], start + valueStart, value);
                cells.Add(new(start + from, cursor - from,
                    row.IsHeader ? NativeClipboardTag.Header : NativeClipboardTag.Cell, inline));
            }
            if (cursor != section.Text.Length) { throw new InvalidOperationException("Clipboard table extent failed."); }
            return new(start, section.Text.Length, NativeClipboardTag.Row, cells.ToImmutable());
        }
        NativeClipboardNode? Block(Block block)
        {
            if (block is LinkReferenceDefinition) { return null; }
            if (block is Table table)
            {
                var rows = ImmutableArray.CreateBuilder<NativeClipboardNode>();
                foreach (var row in table.Cast<TableRow>())
                {
                    var (section, start) = Section();
                    rows.Add(TableRow(row, section, start));
                }
                return Group(NativeClipboardTag.Table, rows.ToImmutable());
            }
            if (block is ContainerBlock container)
            {
                var children = ImmutableArray.CreateBuilder<NativeClipboardNode>();
                var number = block is ListBlock list && int.TryParse(list.OrderedStart,
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var first) ? first : 1;
                var ordinal = (long)number;
                foreach (var child in container)
                {
                    if (Block(child) is { } mapped)
                    {
                        children.Add(block is ListBlock ? mapped with { Number = ordinal } : mapped);
                    }
                    ordinal++;
                }
                if (children.Count == 0) { return null; }
                var tag = block switch
                {
                    QuoteBlock => NativeClipboardTag.Quote,
                    ListBlock { IsOrdered: true } => NativeClipboardTag.OrderedList,
                    ListBlock => NativeClipboardTag.UnorderedList,
                    ListItemBlock => NativeClipboardTag.ListItem,
                    _ => NativeClipboardTag.None,
                };
                return Group(tag, children.ToImmutable(), number);
            }
            var (part, position) = Section();
            var text = block switch
            {
                CodeBlock code => code.Lines.ToString(),
                ThematicBreakBlock => "—",
                LeafBlock leaf => InlineText(leaf.Inline, strikethrough ? new InlineStrikethroughProjection(0) : null),
                _ => throw new InvalidOperationException("Unsupported clipboard block mapping."),
            };
            var prefix = part.Text.Length - text.Length;
            if (prefix < 0 || !part.Text.AsSpan(prefix).SequenceEqual(text))
            {
                throw new InvalidOperationException("Clipboard block semantic mapping failed.");
            }
            var formatting = block is CodeBlock
                ? ImmutableArray.Create(new NativeClipboardNode(position + prefix, text.Length, NativeClipboardTag.Code, []))
                : block is LeafBlock leafBlock and not ThematicBreakBlock ? Inlines(leafBlock, position + prefix, text) : [];
            var blockTag = block switch
            {
                HeadingBlock heading => heading.Level switch
                {
                    1 => NativeClipboardTag.Heading1, 2 => NativeClipboardTag.Heading2,
                    3 => NativeClipboardTag.Heading3, 4 => NativeClipboardTag.Heading4,
                    5 => NativeClipboardTag.Heading5, _ => NativeClipboardTag.Heading6,
                },
                CodeBlock => NativeClipboardTag.Pre,
                ThematicBreakBlock => NativeClipboardTag.Rule,
                _ => NativeClipboardTag.Paragraph,
            };
            return new(position, part.Text.Length, blockTag, formatting);
        }
        var root = Block(document) ?? throw new InvalidOperationException("Clipboard document mapping is empty.");
        if (index != sections.Count || offset != semantic.Length)
        {
            throw new InvalidOperationException("Clipboard document extent failed.");
        }
        return new(semantic, root);
    }
}
