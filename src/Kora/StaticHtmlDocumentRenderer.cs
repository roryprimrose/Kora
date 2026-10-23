using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Controls;
using Kora.Core.Presentation;

namespace Kora;

internal static class StaticHtmlDocumentRenderer
{
    internal static IReadOnlyList<Control> CreateControls(NativeDocumentResult result)
    {
        if (result.IsFallback)
        {
            return
            [
                CreateText(result.Status, StaticHtmlSectionKind.Paragraph, "Static HTML unavailable; exact source follows"),
                CreateText(result.Source, StaticHtmlSectionKind.Code, "Exact static HTML source"),
            ];
        }
        var document = result.StaticHtml;
        if (document is null || document.IsFallback
            || !string.Equals(result.Source, document.Source, StringComparison.Ordinal)
            || !string.Equals(result.SemanticText, document.SemanticText, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Native static HTML controls require the complete validated immutable projection.");
        }
        return document.Sections.Select(section => (Control)CreateText(section.Text, section.Kind, Label(section.Kind))).ToArray();
    }

    private static string Label(StaticHtmlSectionKind kind) => kind switch
    {
        StaticHtmlSectionKind.Heading1 => "Heading level 1",
        StaticHtmlSectionKind.Heading2 => "Heading level 2",
        StaticHtmlSectionKind.Heading3 => "Heading level 3",
        StaticHtmlSectionKind.Heading4 => "Heading level 4",
        StaticHtmlSectionKind.Heading5 => "Heading level 5",
        StaticHtmlSectionKind.Heading6 => "Heading level 6",
        StaticHtmlSectionKind.Code => "Preformatted inert code",
        StaticHtmlSectionKind.ListItem => "List item",
        StaticHtmlSectionKind.Quote => "Quotation",
        StaticHtmlSectionKind.Divider => "Thematic separator",
        _ => "Paragraph",
    };

    private static NamedTextBlock CreateText(string text, StaticHtmlSectionKind kind, string label)
    {
        var heading = kind is >= StaticHtmlSectionKind.Heading1 and <= StaticHtmlSectionKind.Heading6;
        var fontSize = kind switch
        {
            StaticHtmlSectionKind.Heading1 => 32,
            StaticHtmlSectionKind.Heading2 => 23,
            StaticHtmlSectionKind.Heading3 => 20,
            StaticHtmlSectionKind.Heading4 => 18,
            StaticHtmlSectionKind.Heading5 or StaticHtmlSectionKind.Heading6 => 17,
            StaticHtmlSectionKind.Code => 13,
            _ => 14,
        };
        var control = new NamedTextBlock
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = heading ? FontWeight.SemiBold : FontWeight.Normal,
            FontFamily = kind == StaticHtmlSectionKind.Code ? new FontFamily("Consolas") : FontFamily.Default,
            Margin = new Thickness(0, 4, 0, 10),
            LineHeight = fontSize * 1.55,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ContextMenu = null,
            ContextFlyout = null,
        };
        AutomationProperties.SetName(control, label);
        return control;
    }
}
