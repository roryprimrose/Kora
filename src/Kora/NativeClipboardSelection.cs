using System.Globalization;
using System.Net;
using System.Text;

namespace Kora;

internal sealed class NativeClipboardSelection(string semanticText, NativeClipboardNode root)
{
    internal string SemanticText { get; } = semanticText;

    internal string Serialize(int start, int length)
    {
        if (start < 0 || length <= 0 || start > SemanticText.Length - length)
        {
            throw new InvalidOperationException("The selected clipboard range is invalid.");
        }
        var end = start + length;
        if (char.IsLowSurrogate(SemanticText[start])
            || end < SemanticText.Length && char.IsLowSurrogate(SemanticText[end]))
        {
            throw new InvalidOperationException("The selected clipboard range splits a Unicode character.");
        }
        var html = new StringBuilder();
        void Text(int from, int to, bool lineBreaks)
        {
            if (from >= to) { return; }
            var encoded = WebUtility.HtmlEncode(SemanticText[from..to]);
            html.Append(lineBreaks
                ? encoded.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal)
                    .Replace("\n", "<br>", StringComparison.Ordinal)
                : encoded);
        }
        void Write(NativeClipboardNode node, bool preformatted)
        {
            var from = Math.Max(start, node.Start);
            var to = Math.Min(end, node.End);
            if (from >= to) { return; }
            var tag = node.Tag switch
            {
                NativeClipboardTag.None => null,
                NativeClipboardTag.Paragraph => "p",
                NativeClipboardTag.Heading1 => "h1",
                NativeClipboardTag.Heading2 => "h2",
                NativeClipboardTag.Heading3 => "h3",
                NativeClipboardTag.Heading4 => "h4",
                NativeClipboardTag.Heading5 => "h5",
                NativeClipboardTag.Heading6 => "h6",
                NativeClipboardTag.Strong => "strong",
                NativeClipboardTag.Emphasis => "em",
                NativeClipboardTag.Strike => "s",
                NativeClipboardTag.Pre => "pre",
                NativeClipboardTag.Code => "code",
                NativeClipboardTag.Quote => "blockquote",
                NativeClipboardTag.OrderedList => "ol",
                NativeClipboardTag.UnorderedList => "ul",
                NativeClipboardTag.ListItem => "li",
                NativeClipboardTag.Table => "table",
                NativeClipboardTag.Row => "tr",
                NativeClipboardTag.Header => "th",
                NativeClipboardTag.Cell => "td",
                NativeClipboardTag.Rule => "hr",
                _ => throw new InvalidOperationException("Unsupported clipboard formatting."),
            };
            if (node.Tag == NativeClipboardTag.Rule) { html.Append("<hr>"); return; }
            if (tag is not null)
            {
                html.Append('<').Append(tag);
                if (node.Tag == NativeClipboardTag.OrderedList)
                {
                    var first = 0;
                    while (first < node.Children.Length && node.Children[first].End <= from) { first++; }
                    var number = first < node.Children.Length ? node.Children[first].Number : node.Number;
                    html.Append(" start=\"").Append(number.ToString(CultureInfo.InvariantCulture)).Append('"');
                }
                html.Append('>');
            }
            preformatted |= node.Tag == NativeClipboardTag.Pre;
            var lineBreaks = !preformatted && node.Tag is NativeClipboardTag.Paragraph
                or NativeClipboardTag.Heading1 or NativeClipboardTag.Heading2 or NativeClipboardTag.Heading3
                or NativeClipboardTag.Heading4 or NativeClipboardTag.Heading5 or NativeClipboardTag.Heading6
                or NativeClipboardTag.Strong or NativeClipboardTag.Emphasis or NativeClipboardTag.Strike
                or NativeClipboardTag.Code or NativeClipboardTag.Header or NativeClipboardTag.Cell;
            var cursor = from;
            foreach (var child in node.Children)
            {
                if (child.End <= from || child.Start >= to) { continue; }
                Text(cursor, Math.Max(cursor, child.Start), lineBreaks);
                Write(child, preformatted);
                cursor = Math.Min(to, child.End);
            }
            Text(cursor, to, lineBreaks);
            if (tag is not null) { html.Append("</").Append(tag).Append('>'); }
        }
        Write(root, false);
        return html.ToString();
    }
}
