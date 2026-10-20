using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class LocalFilePreviewWindow
{
    private LocalFileRevision? findSource;
    private TextBox? findQuery;
    private CheckBox? findMatchCase;
    private NamedTextBlock? findStatus;
    private Button? findNext;
    private Button? findPrevious;
    private LocalFileLiteralMatch? findMatch;
    private CancellationTokenSource? finding;
    private long findGeneration;
    private bool literalSpanVisible;

    private void SetFindSource(LocalFileRevision? file)
    {
        ClearFind(clearQuery: true);
        findSource = file;
    }

    private void ShowFind(DockPanel body, Func<bool> current,
        Func<CancellationToken, Task<IReadOnlyList<LocalFileRevision>>> resolveSource)
    {
        var query = new TextBox
        {
            AcceptsReturn = false,
            PlaceholderText = $"Literal Find: {LocalFileRetrievalPolicy.MaximumQueryCharacters} UTF-16 characters / "
                + $"{LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes} UTF-8 bytes maximum",
        };
        findQuery = query;
        AutomationProperties.SetName(query, "Literal Find in the currently displayed exact original file only");
        query.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty && ReferenceEquals(findQuery, query)) { ClearFind(); }
        };
        var matchCase = new CheckBox { Content = "Match case", IsChecked = true, IsThreeState = false };
        findMatchCase = matchCase;
        AutomationProperties.SetName(matchCase, "Match case");
        ToolTip.SetTip(matchCase, "Checked: ordinal case-sensitive. Unchecked: ordinal ignore case, only in this original-file literal Find. Requires a fresh Find first.");
        matchCase.PropertyChanged += (_, args) =>
        {
            if (args.Property == CheckBox.IsCheckedProperty && ReferenceEquals(findMatchCase, matchCase)) { ClearFind(); }
        };
        var status = new NamedTextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        findStatus = status;
        AutomationProperties.SetName(status, "Literal Find outcome, not ranked retrieval");
        Button Action(string caption, LocalFileFindDirection direction)
        {
            var button = new Button { Content = caption };
            AutomationProperties.SetName(button, caption + " literal in current original file using Match case; no wrap");
            button.Command = new AsyncCommand(() => FindLiteral(direction, query, current, resolveSource), exception =>
            {
                if (!ReferenceEquals(findQuery, query)) { return; }
                ClearFind();
                status.Text = "Literal Find unavailable; no match or focus claimed.";
                reportFailure("Literal Find unavailable; inspect the exact current source and enter a bounded one-line literal. Failure type: "
                    + exception.GetType().Name);
            });
            return button;
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(Action("Find first", LocalFileFindDirection.First));
        findNext = Action("Find next", LocalFileFindDirection.Next);
        findPrevious = Action("Find previous", LocalFileFindDirection.Previous);
        findNext.IsEnabled = false;
        findPrevious.IsEnabled = false;
        actions.Children.Add(findNext);
        actions.Children.Add(findPrevious);
        var clear = new Button { Content = "Clear literal Find" };
        clear.Click += (_, _) => ClearFind(clearQuery: true);
        actions.Children.Add(clear);
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new NamedTextBlock
        {
            Text = "Literal Find: Match case defaults on (ordinal); off uses ordinal ignore case only here. "
                + "Unchanged spaces, nonoverlapping, no wrap. Changing Match case requires a fresh Find first. First starts at zero; "
                + "Next starts after the match end; Previous reverses that same forward sequence. "
                + "Total and selected ordinal cover this exact immutable original file only, not other files, "
                + "ranked retrieval or live disk. Native only; ranked search below is unchanged.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        panel.Children.Add(query);
        panel.Children.Add(matchCase);
        panel.Children.Add(actions);
        panel.Children.Add(status);
        DockPanel.SetDock(panel, Dock.Top);
        body.Children.Insert(1, panel);
    }

    private async Task FindLiteral(LocalFileFindDirection direction, TextBox queryBox, Func<bool> current,
        Func<CancellationToken, Task<IReadOnlyList<LocalFileRevision>>> resolveSource)
    {
        var file = findSource ?? throw new InvalidOperationException("No exact displayed file is available.");
        var query = queryBox.Text ?? string.Empty;
        var matchCase = findMatchCase ?? throw new InvalidOperationException("Literal Find comparison is unavailable.");
        var comparison = matchCase.IsChecked switch
        {
            true => StringComparison.Ordinal,
            false => StringComparison.OrdinalIgnoreCase,
            null => throw new InvalidOperationException("Literal Find comparison is unavailable."),
        };
        var selected = findMatch;
        ClearNavigation();
        var generation = findGeneration;
        bool Current() => generation == findGeneration && ReferenceEquals(findQuery, queryBox)
            && ReferenceEquals(findMatchCase, matchCase) && matchCase.IsChecked == (comparison == StringComparison.Ordinal)
            && ReferenceEquals(findSource, file) && string.Equals(queryBox.Text, query, StringComparison.Ordinal)
            && string.Equals(content.Text, file.Text, StringComparison.Ordinal) && current();
        void MarkStale()
        {
            if (generation != findGeneration || !ReferenceEquals(findQuery, queryBox)) { return; }
            ClearFind();
            findStatus!.Text = "Literal Find stale or unavailable; inspect the exact source again. No match or focus claimed.";
        }
        if (!Current()) { throw new InvalidOperationException("The displayed exact source is stale."); }
        // Validate and count the displayed immutable source before any retained read; never retain an occurrence index.
        var result = LocalFileLiteralFind.FindCounted(file, query, direction, selected, comparison);
        var match = result.Match;
        using var cancellation = new CancellationTokenSource();
        finding = cancellation;
        try
        {
            var files = await resolveSource(cancellation.Token);
            if (cancellation.IsCancellationRequested) { return; }
            if (!Current()) { MarkStale(); return; }
            var resolved = files.SingleOrDefault(candidate => candidate.Reference == file.Reference)
                ?? throw new InvalidOperationException("The exact displayed source is unavailable.");
            if (!string.Equals(resolved.Text, file.Text, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The validated original source differs from the displayed immutable source.");
            }
            if (!Current()) { MarkStale(); return; }
            findMatch = match ?? (direction == LocalFileFindDirection.First ? null : selected);
            findNext!.IsEnabled = findMatch is not null;
            findPrevious!.IsEnabled = findMatch is not null;
            findStatus!.Text = match is not null
                ? $"Literal match {result.Ordinal} of {result.Count} in this exact immutable original file only; "
                    + (comparison == StringComparison.Ordinal ? "case-sensitive" : "ordinal ignore case")
                    + "; nonoverlapping; no wrap."
                : direction == LocalFileFindDirection.First
                    ? "No literal match in this exact immutable original file only; total 0; selected ordinal 0. No match or focus claimed."
                    : (direction == LocalFileFindDirection.Next ? "End of forward direction" : "End of backward direction")
                        + $"; no wrap. Total {result.Count} in this exact immutable original file only; "
                        + $"anchor ordinal {result.Ordinal}; no current match selected or focus claimed.";
            if (match is null) { return; }
            citedLocation!.Text = DescribeItem(file) + "\n"
                + $"Literal match: UTF-16 [{match.Start}, {match.Start + match.Length}) of {file.Text.Length}; end exclusive. "
                + "Historical admission, not a current filesystem observation or ranked citation. Complete original source remains below.";
            citedSpan!.Text = file.Text.Substring(match.Start, match.Length);
            AutomationProperties.SetName(citedSpan, $"Exact literal original source match; item {file.ItemId:D}; "
                + $"UTF-16 [{match.Start}, {match.Start + match.Length})");
            literalSpanVisible = true;
            exactSpanPanel!.IsVisible = true;
            UpdateLayout();
            if (!Current())
            {
                MarkStale();
                return;
            }
            citedSpan.BringIntoView();
            if (!Current()) { MarkStale(); return; }
            if (!citedSpan.Focus()) { throw new InvalidOperationException("Literal source match focus is unavailable."); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception) when (cancellation.IsCancellationRequested || !Current()) { MarkStale(); }
        finally
        {
            if (ReferenceEquals(finding, cancellation)) { finding = null; }
        }
    }

    private void ClearFind(bool clearQuery = false)
    {
        findGeneration++;
        finding?.Cancel();
        findMatch = null;
        if (findNext is not null) { findNext.IsEnabled = false; }
        if (findPrevious is not null) { findPrevious.IsEnabled = false; }
        if (findStatus is not null) { findStatus.Text = null; }
        if (literalSpanVisible)
        {
            literalSpanVisible = false;
            if (citedSpan is not null) { citedSpan.Text = null; AutomationProperties.SetName(citedSpan, null); }
            if (citedLocation is not null) { citedLocation.Text = null; }
            if (exactSpanPanel is not null) { exactSpanPanel.IsVisible = false; }
        }
        if (clearQuery && findQuery is not null) { findQuery.Text = null; }
    }
}