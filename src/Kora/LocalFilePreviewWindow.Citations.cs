using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Application.Presentation;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed partial class LocalFilePreviewWindow
{
    private Grid? exactSpanPanel;
    private NamedTextBlock? citedSpan;
    private NamedTextBlock? citedLocation;
    private CancellationTokenSource? navigation;
    private long navigationGeneration;

    private void AddCitationSpan(Grid source)
    {
        citedLocation = new NamedTextBlock { TextWrapping = TextWrapping.Wrap };
        citedSpan = new NamedTextBlock { FontFamily = new FontFamily("Consolas"), Focusable = true };
        if (typographyHost is not null) { NativeReaderTypography.Bind(citedSpan, typographyHost); }
        AutomationProperties.SetName(citedLocation, "Exact original source identity and end-exclusive location");
        exactSpanPanel = new Grid { IsVisible = false, RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 4 };
        exactSpanPanel.Children.Add(new ScrollViewer
        {
            Content = citedLocation,
            MaxHeight = 64,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        var spanViewport = new ScrollViewer
        {
            Content = citedSpan,
            MaxHeight = 140,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        Grid.SetRow(spanViewport, 1);
        exactSpanPanel.Children.Add(spanViewport);
        // Exact-span navigation cannot consume the complete original source viewport.
        exactSpanPanel.Bind(MaxHeightProperty, new Binding("Bounds.Height")
        {
            Source = source,
            Converter = new FuncValueConverter<double, double>(height => height * 0.4),
        });
        source.Children.Add(exactSpanPanel);
    }

    private async Task NavigateCitation(
        Func<LocalFileCitation, IReadOnlyList<LocalFileRevision>, Func<bool>, LocalFileCitationSpan> resolve,
        ListBox rows, Func<bool> current,
        Func<CancellationToken, Task<IReadOnlyList<LocalFileRevision>>> resolveSource, Action<LocalFileRevision> selectFile)
    {
        ClearNavigation();
        var generation = navigationGeneration;
        var citation = rows.SelectedItem as LocalFileCitation
            ?? throw new InvalidOperationException("Select a current successful citation first.");
        if (!current()) { throw new InvalidOperationException("The exact source admission is no longer current."); }
        using var cancellation = new CancellationTokenSource();
        navigation = cancellation;
        try
        {
            var files = await resolveSource(cancellation.Token);
            if (cancellation.IsCancellationRequested || generation != navigationGeneration
                || !ReferenceEquals(rows.SelectedItem, citation) || !current()) { return; }
            var span = resolve(citation, files, current);
            navigation = null;
            selectFile(span.File);
            generation = navigationGeneration;
            // Folder selection clears the previous target; this explicit action alone reveals and focuses the new span.
            content.Text = span.File.Text;
            SetFindSource(span.File);
            citedLocation!.Text = DescribeItem(span.File) + "\n"
                + $"Original source span: lines {citation.StartLine}:{citation.StartColumn}–{citation.EndLine}:{citation.EndColumn}; "
                + $"UTF-16 [{citation.Start}, {citation.Start + citation.Length}) of {span.File.Text.Length}. "
                + "End exclusive; historical admission, not a current filesystem observation. Complete original source remains below.";
            citedSpan!.Text = span.Text;
            AutomationProperties.SetName(citedSpan,
                $"Exact cited original source span; item {span.File.ItemId:D}; lines {citation.StartLine}:{citation.StartColumn}–{citation.EndLine}:{citation.EndColumn}");
            exactSpanPanel!.IsVisible = true;
            var target = citedSpan;
            var layoutGeneration = sourceLayoutGeneration;
            UpdateLayout();
            var admitted = current();
            if (generation != navigationGeneration || !ReferenceEquals(citedSpan, target)) { return; }
            if (!admitted)
            {
                ClearNavigation();
                throw new InvalidOperationException("Exact source disclosure was revoked before focus.");
            }
            if (!IsSourceLayoutCurrent(layoutGeneration, target, span.File, current)
                || generation != navigationGeneration) { return; }
            target.BringIntoView();
            if (!IsSourceLayoutCurrent(layoutGeneration, target, span.File, current)
                || generation != navigationGeneration) { return; }
            if (!target.Focus()) { throw new InvalidOperationException("Exact source span focus is unavailable."); }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception) when (cancellation.IsCancellationRequested || generation != navigationGeneration || !current()) { }
        finally
        {
            if (ReferenceEquals(navigation, cancellation)) { navigation = null; }
        }
    }

    private void ClearNavigation()
    {
        ClearFind();
        navigationGeneration++;
        navigation?.Cancel();
        if (citedSpan is not null) { citedSpan.Text = null; AutomationProperties.SetName(citedSpan, null); }
        if (citedLocation is not null) { citedLocation.Text = null; }
        if (exactSpanPanel is not null) { exactSpanPanel.IsVisible = false; }
    }

    private static string DescribeItem(LocalFileRevision file) =>
        $"{file.Review.Metadata.CanonicalPath.Split('\\')[^1]} / source {file.Reference.SourceId:D} / item {file.ItemId:D} "
        + $"/ revision {file.RevisionId:D} / SHA-256 {file.Digest}";

    private static string DescribeCitation(LocalFileCitation citation) =>
        $"{citation.DisplayIdentity} / source {citation.Source.SourceId:D} / revision {citation.Source.RevisionId:D} / item {citation.Source.ItemId:D}\n"
        + $"SHA-256 {citation.Source.Digest} / lines {citation.StartLine}:{citation.StartColumn}–{citation.EndLine}:{citation.EndColumn} "
        + $"/ UTF-16 [{citation.Start}, {(long)citation.Start + citation.Length}) / heading {citation.Heading ?? "(none)"}\n"
        + (citation.HeadingContext is { } heading
            ? $"Authored heading: {heading.Line}:{heading.Column} / UTF-16 [{heading.Start}, {(long)heading.Start + heading.Length})\n" : string.Empty)
        + (citation.IsStructuralFragment ? "Bounded structural fragment; not an intact section/block.\n" : string.Empty)
        + $"Matched terms: {citation.MatchedTerms}; bounded frequency: {citation.BoundedFrequency}\n{citation.Excerpt}";
}