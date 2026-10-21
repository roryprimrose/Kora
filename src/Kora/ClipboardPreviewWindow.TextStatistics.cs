using System.Globalization;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class ClipboardPreviewWindow
{
    private NamedTextBlock? textStatisticsStatus;
    private ClipboardSnapshot? textStatisticsSource;
    private Func<bool>? textStatisticsViewCurrent;
    private Func<bool>? textStatisticsCurrent;
    private Func<ClipboardSnapshot, Func<bool>, CancellationToken, ClipboardTextStatisticsResult>? textStatisticsCount;
    private long textStatisticsGeneration;
    private long textStatisticsOperation;

    private void AddTextStatistics(StackPanel content, ClipboardSnapshot snapshot, Func<bool> viewCurrent, Func<bool> isCurrent,
        Func<ClipboardSnapshot, Func<bool>, CancellationToken, ClipboardTextStatisticsResult>? countText)
    {
        textStatisticsSource = snapshot;
        textStatisticsViewCurrent = viewCurrent;
        textStatisticsCurrent = isCurrent;
        textStatisticsCount = countText;
        var generation = textStatisticsGeneration;
        var status = new NamedTextBlock { TextWrapping = TextWrapping.Wrap, Focusable = true };
        textStatisticsStatus = status;
        AutomationProperties.SetName(status, "Text counts for this exact clipboard snapshot");
        var button = new Button { Content = "Count this snapshot text", IsEnabled = countText is not null };
        AutomationProperties.SetName(button, "Count this exact clipboard snapshot text");
        button.Command = new AsyncCommand(() =>
        {
            var operation = ++textStatisticsOperation;
            var captured = textStatisticsSource;
            bool Current()
            {
                if (generation != textStatisticsGeneration || operation != textStatisticsOperation) { return false; }
                if (textStatisticsViewCurrent?.Invoke() != true) { ClearAndClose(); return false; }
                if (captured is null || textStatisticsCurrent?.Invoke() != true) { ClearTextStatistics(); return false; }
                return generation == textStatisticsGeneration && operation == textStatisticsOperation
                    && ReferenceEquals(textStatisticsSource, captured);
            }
            if (!Current()) { return Task.CompletedTask; }
            status.Text = null;
            if (!Current()) { return Task.CompletedTask; }
            var count = textStatisticsCount ?? throw new InvalidOperationException("Native clipboard text counting is unavailable.");
            var result = count(captured!, Current, CancellationToken.None);
            if (!Current()) { return Task.CompletedTask; }
            if (!ReferenceEquals(result.Source, captured))
            {
                throw new InvalidOperationException("Text counts do not belong to this exact clipboard snapshot.");
            }
            status.Text = result.Statistics is { } counts
                ? string.Create(CultureInfo.InvariantCulture,
                    $"UTF-8 bytes: {counts.Utf8Bytes}; UTF-16 code units: {counts.Utf16CodeUnits}; Unicode scalars: {counts.UnicodeScalars}; whitespace-delimited words: {counts.WhitespaceDelimitedWords}; logical lines: {counts.LogicalLines}.")
                : "Text counting unavailable for this exact source. No counts are claimed.";
            _ = Current();
            return Task.CompletedTask;
        }, exception =>
        {
            if (generation != textStatisticsGeneration) { return; }
            if (textStatisticsViewCurrent?.Invoke() != true) { ClearAndClose(); return; }
            if (textStatisticsCurrent?.Invoke() != true) { ClearTextStatistics(); return; }
            if (generation != textStatisticsGeneration) { return; }
            status.Text = "Text counting unavailable. No counts are claimed.";
            if (textStatisticsCurrent?.Invoke() != true) { ClearTextStatistics(); return; }
            if (generation != textStatisticsGeneration) { return; }
            reportFailure(exception);
        });
        content.Children.Add(button);
        content.Children.Add(new NamedTextBlock
        {
            Text = "Counts use the full unchanged snapshot, not the Find query or span. Words are maximal runs of non-whitespace Unicode scalars, "
                + "not linguistic words or model tokens. CRLF is one line break; CR, LF, NEL, line separator and paragraph separator also break lines. "
                + "A trailing break includes an empty final line. Scalar counts are not displayed-character counts. Nothing is copied, saved, spoken or sent to a model.",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(status);
    }

    public void ClearTextStatistics()
    {
        textStatisticsGeneration++;
        textStatisticsOperation++;
        if (textStatisticsStatus is not null) { textStatisticsStatus.Text = null; }
        textStatisticsStatus = null;
        textStatisticsSource = null;
        textStatisticsViewCurrent = null;
        textStatisticsCurrent = null;
        textStatisticsCount = null;
    }
}
