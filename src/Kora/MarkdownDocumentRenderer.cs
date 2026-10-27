using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Controls;
using Kora.CodeHighlighting;
using Kora.Core.Presentation.CodeHighlighting;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kora;

// The guide and grants share precisely the same admission/parser/rendering profile.
internal static class MarkdownDocumentRenderer
{
    internal static NativeDetailRenderer Renderer { get; set; } =
        new(NullLogger<NativeDetailRenderer>.Instance);

    public static IReadOnlyList<Control> Render(string markdown) =>
        CreateControls(Renderer.Render(markdown));

    internal static IReadOnlyList<Control> CreateControls(NativeDocumentResult result, bool selectable = true,
        NativeDocumentInteraction? interaction = null)
    {
        if (result.IsFallback)
        {
            return
            [
                CreateText(result.Status, NativeTextStyle.Status, selectable),
                CreateText(result.Source, NativeTextStyle.Code, selectable),
            ];
        }

        // Bound the entire view, not just each block; large documents stay ordinary monospace text.
        var codeBytes = result.Sections.Where(section => section.Code is not null)
            .Sum(section => (long)section.Code!.Text.Length);
        var highlight = codeBytes <= CodeHighlightProfile.MaximumUtf8Bytes
            && result.Sections.Count(section => section.Code is not null) <= 16;
        var controls = new List<Control>();
        foreach (var section in result.Sections)
        {
            if (interaction is not null && section.Table is { IsHeader: false }) { continue; }
            var text = CreateText(section.Text, section.Style, selectable);
            NativeStrikethroughText.Attach(text, section);
            if (interaction is not null && section.HeadingId is { } id)
            { interaction.RegisterHeading(id, text); }
            Control control = interaction is not null && section.Table is { IsHeader: true } table
                ? interaction.CreateTable(table.Data)
                : section.Code is { } code ? new NativeCodeBlock(code, text, highlight) : text;
            controls.Add(control);
            if (interaction is not null) { controls.AddRange(interaction.CreateLinks(section.Links)); }
        }
        return controls;
    }

    private static TextBlock CreateText(string text, NativeTextStyle style, bool selectable)
    {
        var fontSize = style switch
        {
            NativeTextStyle.Heading1 => 32,
            NativeTextStyle.Heading2 => 23,
            NativeTextStyle.Heading => 17,
            NativeTextStyle.Code => 13,
            _ => 14,
        };
        TextBlock control = selectable ? new SelectableTextBlock()
            : style is NativeTextStyle.Table or NativeTextStyle.Checklist
                ? new NamedTextBlock { Focusable = true } : new TextBlock();
        control.Text = text;
        control.FontSize = fontSize;
        control.FontWeight = style is NativeTextStyle.Heading1 or NativeTextStyle.Heading2
            or NativeTextStyle.Heading or NativeTextStyle.Status ? FontWeight.SemiBold : FontWeight.Normal;
        control.FontFamily = style == NativeTextStyle.Code ? new FontFamily("Consolas") : FontFamily.Default;
        control.Margin = new Thickness(0, 4, 0, 10);
        control.LineHeight = fontSize * 1.55;
        control.TextWrapping = TextWrapping.Wrap;
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        control.ContextMenu = null;
        return control;
    }
}
