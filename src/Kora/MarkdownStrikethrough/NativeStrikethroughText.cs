using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

using Kora.Core.Presentation.MarkdownStrikethrough;

namespace Kora;

internal static class NativeStrikethroughText
{
    internal static void Attach(TextBlock control, NativeTextSection section)
    {
        if (section.Strikethrough.Count == 0) { return; }
        var boundaries = new SortedDictionary<int, int>();
        foreach (var span in section.Strikethrough)
        {
            boundaries[span.TextStart] = boundaries.GetValueOrDefault(span.TextStart) + 1;
            boundaries[span.TextEnd] = boundaries.GetValueOrDefault(span.TextEnd) - 1;
        }
        var inlines = new InlineCollection();
        var position = 0;
        var depth = 0;
        void Append(int end)
        {
            if (end == position) { return; }
            var run = new Run(section.Text[position..end]);
            if (depth > 0) { run.TextDecorations = TextDecorations.Strikethrough; }
            inlines.Add(run);
            position = end;
        }
        foreach (var boundary in boundaries)
        {
            Append(boundary.Key);
            depth += boundary.Value;
        }
        Append(section.Text.Length);
        control.Inlines = inlines;
        control.Focusable = true;
        AutomationProperties.SetName(control, section.Text);
        AutomationProperties.SetHelpText(control,
            "Display-only strikethrough wording, not a diff, task status, action or permission. Exact source is unchanged.");
    }

    internal static void Retire(IEnumerable<Control> controls)
    {
        foreach (var text in controls.OfType<TextBlock>())
        {
            if (text.Inlines?.OfType<Run>().Any(run => run.TextDecorations == TextDecorations.Strikethrough) != true)
            { continue; }
            text.Inlines.Clear();
            text.Text = string.Empty;
            AutomationProperties.SetName(text, string.Empty);
            AutomationProperties.SetHelpText(text, string.Empty);
        }
    }
}
