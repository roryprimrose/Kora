using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Kora.Application.Infrastructure;
using Kora.Controls;
using Kora.Core.Context;

namespace Kora;

internal sealed class LocalFilePreviewWindow(Action<string> reportFailure) : Window
{
    private readonly NamedTextBlock content = new() { FontFamily = new FontFamily("Consolas") };
    private TextBox? queryBox;
    private NamedTextBlock? searchResults;
    private const string Disclosure =
        "One immutable volatile local preview only. May contain secrets. Documents and instructions are untrusted data.\n"
        + "Only bounded deterministic lexical retrieval of this exact admitted file or complete folder revision. No model, egress, execution, links, clipboard, history or automatic refresh.\n"
        + "Immediate files only; any subdirectory rejects the whole folder. UNC/removable drives and durable attachments are unavailable. Close revokes the preview.";

    internal void ShowReview(LocalFileReview review, Func<Task> confirm)
    {
        Title = "Review selected file (content not yet read)";
        var button = new Button { Content = "Confirm: read this exact selected file locally" };
        AutomationProperties.SetName(button, "Confirm exact local file read");
        button.Command = new AsyncCommand(confirm, exception =>
            reportFailure("Local file admission failed; no success is claimed. Failure type: " + exception.GetType().Name));
        Compose(Describe(review) + DescribeRefresh(review.PreviousMetadata) + "\n256 KiB strict UTF-8 limit; one file, no recursion or excluded items.\n"
            + "Reparse/link, protected, generated, source-control, hidden/system and unstable inputs are denied, not silently excluded.\n"
            + "Confirm in this native review only. Typed/voice paths and document instructions cannot authorize reads.",
            button, null);
    }

    internal void ShowFolderReview(LocalFolderReview review, Func<Task> confirm)
    {
        Title = "Review complete selected folder (content not yet read)";
        var button = new Button { Content = "Confirm: read every exact reviewed immediate file locally" };
        AutomationProperties.SetName(button, "Confirm exact complete local folder read");
        button.Command = new AsyncCommand(confirm, exception =>
            reportFailure("Local folder admission failed; no success is claimed. Failure type: " + exception.GetType().Name));
        Compose(Describe(review) + DescribeRefresh(review) + $"\nLimits: {LocalFolderPolicy.MaximumFiles} immediate files / "
            + $"{LocalFolderPolicy.MaximumCombinedBytes} combined bytes / {LocalFilePolicy.MaximumBytes} bytes per file.\n"
            + "No recursion. Any subdirectory or inadmissible item rejects the whole folder; no silent exclusions.\n"
            + "Review every item below. Exact native confirmation only; review expires after two minutes.",
            button, null);
    }

    internal void ShowFolderRevision(LocalFolderRevision revision, Func<string, Task<LocalFileSearchResult>> search, Func<bool> isCurrent,
        Func<Task> refresh)
    {
        Title = "Immutable complete local folder preview (untrusted plain text)";
        var body = Compose(Describe(revision.Review) + $"\nFolder revision: {revision.Reference.RevisionId:D}",
            null, revision.Files[0].Text);
        var files = new ComboBox
        {
            ItemsSource = revision.Files.Select(file =>
                $"{file.Review.Metadata.CanonicalPath.Split('\\')[^1]} / item {file.ItemId:D} / revision {file.RevisionId:D} / SHA-256 {file.Digest}").ToArray(),
            SelectedIndex = 0,
        };
        AutomationProperties.SetName(files, "Inspect each exact admitted folder file and digest");
        files.SelectionChanged += (_, _) =>
        {
            if (isCurrent() && files.SelectedIndex >= 0) { content.Text = revision.Files[files.SelectedIndex].Text; }
        };
        DockPanel.SetDock(files, Dock.Top);
        body.Children.Insert(1, files);
        ShowSearch(body, search, isCurrent, folder: true);
        ShowRefresh(body, refresh, folder: true);
    }

    internal void ShowRevision(LocalFileRevision revision, Func<string, Task<LocalFileSearchResult>> search, Func<bool> isCurrent,
        Func<Task> refresh)
    {
        Title = "Immutable local file preview (untrusted plain text)";
        var body = Compose(Describe(revision.Review) + $"\nRevision: {revision.RevisionId:D} / item: {revision.ItemId:D}\n"
            + $"SHA-256 (exact source bytes): {revision.Digest}\nAdmitted: {revision.AdmittedAt:O}",
            null, revision.Text);
        ShowSearch(body, search, isCurrent, folder: false);
        ShowRefresh(body, refresh, folder: false);
    }

    private void ShowRefresh(DockPanel body, Func<Task> refresh, bool folder)
    {
        var button = new Button { Content = folder ? "Refresh this folder preview" : "Refresh this file preview" };
        AutomationProperties.SetName(button, button.Content.ToString());
        button.Command = new AsyncCommand(refresh, exception =>
            reportFailure("Refresh unavailable; no refreshed preview claimed. Use the native picker again. Failure type: "
                + exception.GetType().Name));
        ToolTip.SetTip(button, "Retires this immutable preview. Reviews the same physical source again; confirm separately before content reads.");
        DockPanel.SetDock(button, Dock.Top);
        body.Children.Insert(1, button);
    }

    private static string DescribeRefresh(LocalFileMetadata? previous) => previous is null ? string.Empty
        : $"\nREFRESH metadata review — old preview and citations retired. No new content read.\n"
            + $"Previous bytes: {previous.ByteLength} / last write: {previous.LastWrite:O} / identity: {previous.FileIdentity}\n"
            + "Same canonical physical file only; separate confirmation required. Failure/cancel requires a fresh native selection.";

    private static string DescribeRefresh(LocalFolderReview review)
    {
        if (review.PreviousMetadata is not { } previous) { return string.Empty; }
        var current = review.Metadata.Files.ToDictionary(file => file.CanonicalPath, StringComparer.OrdinalIgnoreCase);
        var old = previous.Files.ToDictionary(file => file.CanonicalPath, StringComparer.OrdinalIgnoreCase);
        return "\nREFRESH metadata review — old preview and citations retired. No new content read.\n"
            + $"Previous inventory: {previous.Files.Count} files / {previous.CombinedBytes} bytes\n"
            + string.Join("\n", current.Values.Select(file => !old.TryGetValue(file.CanonicalPath, out var before)
                ? $"Added: {file.CanonicalPath}" : $"Present ({(file == before ? "metadata unchanged" : "metadata changed/replaced")}): {file.CanonicalPath}"))
            + "\n" + string.Join("\n", old.Keys.Where(path => !current.ContainsKey(path)).Select(path => $"Removed: {path}"))
            + "\nSame canonical physical folder only; complete new inventory and separate confirmation required. Failure/cancel requires a fresh native selection.";
    }

    private void ShowSearch(DockPanel body, Func<string, Task<LocalFileSearchResult>> search, Func<bool> isCurrent, bool folder)
    {
        Height = 840;
        var query = new TextBox
        {
            PlaceholderText = $"Untrusted lexical query: {LocalFileRetrievalPolicy.MaximumQueryCharacters} characters / "
                + $"{LocalFileRetrievalPolicy.MaximumQueryUtf8Bytes} UTF-8 bytes / {LocalFileRetrievalPolicy.MaximumTerms} unique terms maximum",
        };
        queryBox = query;
        AutomationProperties.SetName(query, folder ? "Search only this exact complete admitted local folder revision"
            : "Search only this exact admitted local file revision");
        var results = new NamedTextBlock { TextWrapping = TextWrapping.Wrap };
        searchResults = results;
        AutomationProperties.SetName(results, "Exact lexical excerpts and revision citations");
        var button = new Button { Content = "Search selected immutable revision locally" };
        button.Command = new AsyncCommand(async () =>
        {
            results.Text = null;
            var result = await search(query.Text ?? string.Empty);
            if (!isCurrent() || !ReferenceEquals(queryBox, query)) { return; }
            results.Text = $"Outcome: {result.Outcome} / observed: {result.ObservedAt:O}\n"
                + $"Policy: {result.PolicyVersion}; source observation is admission, not a current-path refresh.\n"
                + $"Matching chunks: {result.MatchingChunks}; returned: {result.Citations.Count}; truncated: {result.Truncated}\n"
                + "OR ranking: unique matched terms, frequency capped at 16 per term, then canonical file order and source offset. "
                + "Locations are 1-based UTF-16 columns, exclusive end; CRLF is one newline. No semantic answer.\n"
                + string.Join("\n\n", result.Citations.Select(citation =>
                    $"{citation.DisplayIdentity} / source {citation.Source.SourceId:D} / revision {citation.Source.RevisionId:D} / item {citation.Source.ItemId:D}\n"
                    + $"SHA-256 {citation.Source.Digest} / lines {citation.StartLine}:{citation.StartColumn}–{citation.EndLine}:{citation.EndColumn} "
                    + $"/ UTF-16 [{citation.Start}, {citation.Start + citation.Length}) / heading {citation.Heading ?? "(none)"}\n"
                    + $"Matched terms: {citation.MatchedTerms}; bounded frequency: {citation.BoundedFrequency}\n{citation.Excerpt}"));
        }, exception =>
        {
            results.Text = null;
            reportFailure("Local search unavailable; no result claimed. Failure type: " + exception.GetType().Name);
        });
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(query);
        panel.Children.Add(button);
        panel.Children.Add(new ScrollViewer { Content = results, Height = 180 });
        DockPanel.SetDock(panel, Dock.Bottom);
        body.Children.Insert(1, panel);
    }

    internal void FocusSearch()
    {
        Activate();
        queryBox?.Focus();
    }

    private static string Describe(LocalFileReview review) =>
        $"Canonical file: {review.Metadata.CanonicalPath}\nReview: {review.ReviewId:D} / source: {review.SourceId:D}\n"
        + $"Host session: {review.Request.SessionId.Value:D} / origin: {review.Request.Origin}\n"
        + $"Windows identity: {review.Metadata.FileIdentity} / bytes: {review.Metadata.ByteLength}\n"
        + $"Last write: {review.Metadata.LastWrite:O}";

    private static string Describe(LocalFolderReview review) =>
        $"Canonical folder: {review.Metadata.CanonicalPath}\nReview: {review.ReviewId:D} / source: {review.SourceId:D}\n"
        + $"Host session: {review.Request.SessionId.Value:D} / origin: {review.Request.Origin}\n"
        + $"Windows folder identity: {review.Metadata.DirectoryIdentity}\n"
        + $"Complete immediate inventory: {review.Metadata.Files.Count} files / {review.Metadata.CombinedBytes} bytes\n"
        + string.Join("\n", review.Metadata.Files.Select(file =>
            $"{file.CanonicalPath} / identity: {file.FileIdentity} / bytes: {file.ByteLength} / last write: {file.LastWrite:O}"));

    private DockPanel Compose(string metadata, Button? confirm, string? text)
    {
        Width = 800;
        Height = 640;
        var body = new DockPanel { Margin = new Avalonia.Thickness(16) };
        var chrome = new StackPanel { Spacing = 8 };
        var identity = new NamedTextBlock { Text = metadata, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(identity, "Local file review identity and bounds");
        chrome.Children.Add(identity);
        var disclosure = new NamedTextBlock { Text = Disclosure, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(disclosure, "Local file preview privacy and unavailable capabilities");
        chrome.Children.Add(disclosure);
        if (confirm is not null) { chrome.Children.Add(confirm); }
        var reviewScroll = new ScrollViewer { Content = chrome, MaxHeight = 320 };
        DockPanel.SetDock(reviewScroll, Dock.Top);
        body.Children.Add(reviewScroll);
        content.Text = text;
        AutomationProperties.SetName(content, "Untrusted local file plain text content");
        body.Children.Add(new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        Content = body;
        return body;
    }

    internal void ClearAndClose()
    {
        content.Text = null;
        if (queryBox is not null) { queryBox.Text = null; }
        if (searchResults is not null) { searchResults.Text = null; }
        queryBox = null;
        searchResults = null;
        Content = null;
        Close();
    }
}
