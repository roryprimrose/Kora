using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

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

    internal static IReadOnlyList<Control> CreateControls(NativeDocumentResult result, bool selectable = true)
    {
        if (result.IsFallback)
        {
            return
            [
                CreateText(result.Status, NativeTextStyle.Status, selectable),
                CreateText(result.Source, NativeTextStyle.Code, selectable),
            ];
        }

        return result.Sections.Select(section => (Control)CreateText(section.Text, section.Style, selectable)).ToArray();
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
        TextBlock control = selectable ? new SelectableTextBlock() : new TextBlock();
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
