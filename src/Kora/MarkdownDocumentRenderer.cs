using System.Globalization;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Kora;

internal static class MarkdownDocumentRenderer
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .Build();

    public static IReadOnlyList<Control> Render(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var document = Markdown.Parse(markdown, Pipeline);
        return document
            .Select(RenderBlock)
            .Where(control => control is not null)
            .Cast<Control>()
            .ToArray();
    }

    private static Control? RenderBlock(Block block) =>
        block switch
        {
            HeadingBlock heading => CreateText(
                ExtractInlineText(heading.Inline),
                heading.Level switch
                {
                    1 => 32,
                    2 => 23,
                    _ => 17,
                },
                FontWeight.SemiBold,
                heading.Level == 1
                    ? new Thickness(0, 0, 0, 15)
                    : new Thickness(0, 20, 0, 7)),
            ParagraphBlock paragraph => CreateText(
                ExtractInlineText(paragraph.Inline),
                14,
                FontWeight.Normal,
                new Thickness(0, 0, 0, 10)),
            ListBlock list => CreateList(list),
            QuoteBlock quote => CreateQuote(quote),
            FencedCodeBlock code => CreateCodeBlock(code),
            CodeBlock code => CreateCodeBlock(code),
            ThematicBreakBlock => CreateDivider(),
            _ => CreateFallback(block),
        };

    private static StackPanel CreateList(ListBlock list)
    {
        var panel = new StackPanel
        {
            Spacing = 6,
            Margin = new Thickness(0, 1, 0, 12),
        };
        var number = int.TryParse(
            list.OrderedStart,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var start)
            ? start
            : 1;
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("32,*"),
            };
            var marker = new TextBlock
            {
                Text = list.IsOrdered ? $"{number++}." : "-",
                FontSize = 14,
            };
            marker.Classes.Add("markdown-list-marker");
            row.Children.Add(marker);
            var text = CreateText(
                ExtractBlockText(item),
                14,
                FontWeight.Normal,
                default);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            panel.Children.Add(row);
        }

        return panel;
    }

    private static Border CreateQuote(QuoteBlock quote)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(16, 8),
            Margin = new Thickness(0, 4, 0, 12),
            Child = CreateText(
                ExtractBlockText(quote),
                14,
                FontWeight.Normal,
                default),
        };
        border.Classes.Add("markdown-quote");
        return border;
    }

    private static Border CreateCodeBlock(CodeBlock code)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 11),
            Margin = new Thickness(0, 4, 0, 12),
            Child = new SelectableTextBlock
            {
                Text = code.Lines.ToString(),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
            },
        };
        border.Classes.Add("markdown-code");
        return border;
    }

    private static Border CreateDivider()
    {
        var border = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 14),
        };
        border.Classes.Add("markdown-divider");
        return border;
    }

    private static SelectableTextBlock? CreateFallback(Block block)
    {
        var text = ExtractBlockText(block);
        return string.IsNullOrWhiteSpace(text)
            ? null
            : CreateText(
                text,
                14,
                FontWeight.Normal,
                new Thickness(0, 0, 0, 10));
    }

    private static SelectableTextBlock CreateText(
        string text,
        double fontSize,
        FontWeight fontWeight,
        Thickness margin) =>
        new()
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = fontWeight,
            Margin = margin,
            LineHeight = fontSize * 1.55,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

    private static string ExtractBlockText(Block block)
    {
        if (block is LeafBlock { Inline: not null } leaf)
        {
            return ExtractInlineText(leaf.Inline);
        }

        if (block is CodeBlock code)
        {
            return code.Lines.ToString();
        }

        if (block is ContainerBlock container)
        {
            return string.Join(
                Environment.NewLine,
                container.Select(ExtractBlockText)
                    .Where(text => !string.IsNullOrWhiteSpace(text)));
        }

        return string.Empty;
    }

    private static string ExtractInlineText(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        AppendInlineText(container.FirstChild, builder);
        return builder.ToString();
    }

    private static void AppendInlineText(Inline? inline, StringBuilder builder)
    {
        for (var current = inline; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content);
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    builder.AppendLine();
                    break;
                case AutolinkInline link:
                    builder.Append(link.Url);
                    break;
                case ContainerInline nested:
                    AppendInlineText(nested.FirstChild, builder);
                    break;
                case HtmlInline html:
                    builder.Append(html.Tag);
                    break;
            }
        }
    }

}
