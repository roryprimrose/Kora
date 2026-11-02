using System.Globalization;
using System.Net;
using System.Text;

using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Kora;

internal sealed partial class NativeDetailRenderer
{
    // Only the already validated, bounded document enters this serializer.
    // No renderer HTML, attributes, resources, styles or active destinations are copied.
    private static string ClipboardHtml(MarkdownDocument document)
    {
        var html = new StringBuilder();
        void Text(string? text) => html.Append(WebUtility.HtmlEncode(text));
        void Element(string tag, Action body)
        {
            html.Append('<').Append(tag).Append('>');
            body();
            html.Append("</").Append(tag).Append('>');
        }
        void Inline(Inline node)
        {
            switch (node)
            {
                case LiteralInline literal: Text(literal.Content.ToString()); break;
                case CodeInline code: Element("code", () => Text(code.Content)); break;
                case LineBreakInline: html.Append("<br>"); break;
                case AutolinkInline link:
                    Text(link.Url);
                    Text(" (inert destination)");
                    break;
                case TaskList task: Text(task.Checked ? "Checked:" : "Unchecked:"); break;
                case ContainerInline container:
                    void Children()
                    {
                        for (var child = container.FirstChild; child is not null; child = child.NextSibling)
                        { Inline(child); }
                    }
                    if (container is EmphasisInline emphasis)
                    {
                        Element(emphasis.DelimiterChar == '~' ? "s"
                            : emphasis.DelimiterCount == 2 ? "strong" : "em", Children);
                    }
                    else
                    {
                        Children();
                        if (container is LinkInline link)
                        {
                            Text(" (destination: ");
                            Text(link.Url);
                            Text(")");
                        }
                    }
                    break;
                default: throw new InvalidOperationException("Unsupported validated clipboard inline.");
            }
        }
        void Block(Block block)
        {
            switch (block)
            {
                case LinkReferenceDefinition: break;
                case CodeBlock code:
                    Element("pre", () => Element("code", () => Text(code.Lines.ToString())));
                    break;
                case ThematicBreakBlock: html.Append("<hr>"); break;
                case LeafBlock leaf:
                    Element(block is HeadingBlock heading
                        ? "h" + heading.Level.ToString(CultureInfo.InvariantCulture) : "p",
                        () => { if (leaf.Inline is { } inline) { Inline(inline); } });
                    break;
                case ContainerBlock container:
                    void Children()
                    {
                        foreach (var child in container) { Block(child); }
                    }
                    var tag = container switch
                    {
                        Table => "table",
                        TableRow => "tr",
                        TableCell { Parent: TableRow { IsHeader: true } } => "th",
                        TableCell => "td",
                        ListBlock { IsOrdered: true } => "ol",
                        ListBlock => "ul",
                        ListItemBlock => "li",
                        QuoteBlock => "blockquote",
                        _ => null,
                    };
                    if (tag is null) { Children(); }
                    else if (container is ListBlock { IsOrdered: true } list
                        && int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out var start))
                    {
                        html.Append("<ol start=\"").Append(start.ToString(CultureInfo.InvariantCulture)).Append("\">");
                        Children();
                        html.Append("</ol>");
                    }
                    else { Element(tag, Children); }
                    break;
                default: throw new InvalidOperationException("Unsupported validated clipboard block.");
            }
        }
        Block(document);
        return html.ToString();
    }
}
