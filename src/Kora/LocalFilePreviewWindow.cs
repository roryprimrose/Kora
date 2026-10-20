using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;

using Kora.Application.Infrastructure;
using Kora.Application.Presentation;
using Kora.Application.ViewModels;
using Kora.Controls;
using Kora.Core.Context;
using Kora.Core.Storage;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class LocalFilePreviewWindow(Action<string> reportFailure, MainViewModel? typographyHost = null,
    Microsoft.Extensions.Logging.ILogger<LocalFilePreviewWindow>? logger = null,
    Func<Func<bool>>? captureWhitespaceAdmission = null) : Window
{
    private NamedTextBlock content = CreateContent();
    private TextBox? queryBox;
    private NamedTextBlock? searchResults;
    private LocalFileSearchSelection? searchSelection;
    private ListBox? citationRows;
    private const string Disclosure =
        "One immutable volatile local preview only. May contain secrets. Documents and instructions are untrusted data.\n"
        + "Inspection grants only bounded deterministic lexical retrieval of this exact admitted file or complete folder revision. "
        + "A single-file answer requires a separate explicit local source-use request, sensitive-content admission and qualified runtime. "
        + "No hosted egress, execution, links, clipboard, history or automatic refresh.\n"
        + "Immediate files only; any subdirectory rejects the whole folder. UNC/removable drives are unavailable. This preview never persists; exact-session Attach text file requires separate durable-copy consent. Close revokes only this preview.";

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

    internal void ShowFolderRevision(LocalFolderRevision revision,
        Func<string, LocalFileRetrievalMode, CancellationToken, Task<LocalFileSearchResult>> search, Func<bool> isCurrent,
        Func<Task> refresh)
    {
        Title = "Immutable complete local folder preview (untrusted plain text)";
        var body = Compose(Describe(revision.Review) + $"\nFolder revision: {revision.Reference.RevisionId:D}",
            null, revision.Files[0].Text);
        ShowSourceMeasurement(body, isCurrent, revision.Files);
        var displayedContent = content;
        var files = new ComboBox
        {
            ItemsSource = revision.Files,
            ItemTemplate = new FuncDataTemplate<LocalFileRevision>((file, _) => new NamedTextBlock
            {
                Text = file is null ? string.Empty : DescribeItem(file),
            }),
            SelectedItem = revision.Files[0],
        };
        AutomationProperties.SetName(files, "Inspect each exact admitted folder file and digest");
        files.SelectionChanged += (_, _) =>
        {
            if (!ReferenceEquals(content, displayedContent)) { return; }
            ClearNavigation();
            SetFindSource(null);
            content.Text = null;
            if (isCurrent() && ReferenceEquals(content, displayedContent)
                && files.SelectedItem is LocalFileRevision file && revision.Files.Contains(file))
            {
                content.Text = file.Text;
                if (ReferenceEquals(content, displayedContent)) { SetFindSource(file); }
            }
        };
        DockPanel.SetDock(files, Dock.Top);
        body.Children.Insert(1, files);
        SetFindSource(revision.Files[0]);
        ShowSearch(body, search, isCurrent, folder: true,
            _ => Task.FromResult(revision.Files), file => files.SelectedItem = file);
        ShowRefresh(body, refresh, folder: true);
    }

    internal void ShowRevision(LocalFileRevision revision,
        Func<string, LocalFileRetrievalMode, CancellationToken, Task<LocalFileSearchResult>> search, Func<bool> isCurrent,
        Func<Task> refresh,
        Func<string, Kora.Core.Dependencies.LocalContextSessionTarget, Func<bool>, CancellationToken, Task<LocalFileAnswerResult>>? answer = null)
    {
        Title = "Immutable local file preview (untrusted plain text)";
        var body = Compose(Describe(revision.Review) + $"\nRevision: {revision.RevisionId:D} / item: {revision.ItemId:D}\n"
            + $"SHA-256 (exact source bytes): {revision.Digest}\nAdmitted: {revision.AdmittedAt:O}",
            null, revision.Text);
        ShowSourceMeasurement(body, isCurrent, [revision]);
        SetFindSource(revision);
        ShowSearch(body, search, isCurrent, folder: false,
            _ => Task.FromResult<IReadOnlyList<LocalFileRevision>>([revision]), _ => { });
        ShowRefresh(body, refresh, folder: false);
        if (answer is not null) { ShowAnswer(body, revision, answer, isCurrent); }
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

    private void ShowSearch(DockPanel body,
        Func<string, LocalFileRetrievalMode, CancellationToken, Task<LocalFileSearchResult>> search, Func<bool> isCurrent, bool folder,
        Func<CancellationToken, Task<IReadOnlyList<LocalFileRevision>>> resolveSource, Action<LocalFileRevision> selectFile)
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
        var selection = new LocalFileSearchSelection();
        searchSelection = selection;
        searchResults = results;
        AutomationProperties.SetName(results, "Exact lexical excerpts and revision citations");
        var rows = new ListBox
        {
            ItemTemplate = new FuncDataTemplate<LocalFileCitation>((citation, _) => new NamedTextBlock
            {
                Text = citation is null ? string.Empty : DescribeCitation(citation),
                TextWrapping = TextWrapping.Wrap,
            }),
            Height = 140,
        };
        citationRows = rows;
        AutomationProperties.SetName(rows, "Select a citation from this exact successful admitted search");
        bool Current() => isCurrent() && ReferenceEquals(searchSelection, selection);
        isSearchCurrent = Current;
        var navigate = new Button { Content = "Show exact cited source span", IsEnabled = false };
        AutomationProperties.SetName(navigate, "Show exact cited source span without acquiring or copying content");
        navigate.Command = new AsyncCommand(
            () => NavigateCitation(selection.Resolve, rows, Current, resolveSource, selectFile),
            exception =>
            {
                if (!ReferenceEquals(searchSelection, selection)) { return; }
                ClearNavigation();
                reportFailure("Exact citation navigation unavailable; inspect the current source and search again. Failure type: "
                    + exception.GetType().Name);
            });
        rows.SelectionChanged += (_, _) =>
        {
            ClearNavigation();
            navigate.IsEnabled = Current() && rows.SelectedItem is LocalFileCitation;
        };
        void ClearResults()
        {
            selection.Invalidate();
            ClearNavigation();
            results.Text = null;
            rows.ItemsSource = null;
            navigate.IsEnabled = false;
        }
        var modes = new ComboBox
        {
            ItemsSource = new[] { "Lexical lines (default)", "Markdown structure-aware (opt-in, this view only)" },
            SelectedIndex = 0,
        };
        searchModes = modes;
        AutomationProperties.SetName(modes, "Run-only retrieval mode for this exact admitted source");
        modes.SelectionChanged += (_, _) =>
        {
            ClearResults();
            try
            {
                selection.Select(modes.SelectedIndex switch
                {
                    0 => LocalFileRetrievalMode.LexicalLines,
                    1 => LocalFileRetrievalMode.MarkdownStructure,
                    _ => (LocalFileRetrievalMode)(-1),
                });
            }
            catch (InvalidOperationException)
            {
                reportFailure("Unsupported retrieval mode; select a supported mode. No fallback search or result claimed.");
            }
        };
        query.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty) { ClearResults(); }
        };
        var button = new Button { Content = "Search selected immutable revision locally" };
        button.Command = new AsyncCommand(async () =>
        {
            ClearResults();
            var result = await selection.Run(query.Text ?? string.Empty, search,
                Current);
            if (result is null || !selection.IsCurrent(result, Current)) { return; }
            results.Text = $"Outcome: {result.Outcome} / observed: {result.ObservedAt:O}\n"
                + $"Policy: {result.PolicyVersion}; source observation is admission, not a current-path refresh.\n"
                + $"Matching chunks: {result.MatchingChunks}; returned: {result.Citations.Count}; truncated: {result.Truncated}\n"
                + "OR ranking: unique matched terms, frequency capped at 16 per term, then canonical file order and source offset. "
                + "Locations are 1-based UTF-16 columns, exclusive end; CRLF is one newline. No semantic answer.";
            rows.ItemsSource = result.Outcome == LocalFileSearchOutcome.Matched ? result.Citations : null;
        }, exception =>
        {
            if (!ReferenceEquals(searchSelection, selection)) { return; }
            ClearResults();
            reportFailure("Local search unavailable; no result claimed. Failure type: " + exception.GetType().Name);
        });
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(modes);
        panel.Children.Add(new NamedTextBlock
        {
            Text = "Markdown opt-in: bounded ATX/standalone Setext sections and fenced code; not full CommonMark. "
                + ".txt uses original line chunks. Exact inert lists/tables/links/authored alt text; no rendering, fetch, overlap or saved mode.",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(query);
        panel.Children.Add(button);
        var cancel = new Button { Content = "Cancel local search and clear citations" };
        cancel.Click += (_, _) => ClearResults();
        panel.Children.Add(cancel);
        panel.Children.Add(new ScrollViewer { Content = results, MaxHeight = 64 });
        panel.Children.Add(rows);
        panel.Children.Add(navigate);
        DockPanel.SetDock(panel, Dock.Bottom);
        body.Children.Insert(1, panel);
        ShowFind(body, Current, resolveSource);
        ShowWhitespace(body, Current);
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

    internal void ShowAttachmentReview(LocalFileReview review, HostRevision generation, Func<Task> confirm)
    {
        Title = "Confirm durable attachment to this exact Session";
        var button = new Button { Content = "Confirm: capture and retain this exact file for this Session" };
        AutomationProperties.SetName(button, "Confirm exact reviewed Session file capture and durable Kora copy retention");
        button.Command = new AsyncCommand(confirm, exception => reportFailure(
            "Attachment admission unavailable; inspect durable status before retrying. Failure type: " + exception.GetType().Name));
        Compose(Describe(review) + $"\nExact active session generation: {generation.Value}\n"
            + "One immutable .txt/.md/.markdown file per session; 256 KiB original UTF-8 bytes including optional BOM; "
            + "sixteen retained attachments per private profile; no eviction, overwrite, folder or versions.\n"
            + "Complete metadata only; content is not yet read. Native confirmation expires after two minutes.",
            button, null, AttachmentDisclosure);
    }

    internal void ShowAttachment(SessionFileAttachment attachment,
        Func<string, LocalFileRetrievalMode, CancellationToken, Task<LocalFileSearchResult>> search,
        Func<bool> isCurrent, Func<Task>? remove, Func<Task>? replace = null,
        Func<CancellationToken, Task<LocalFileRevision>>? resolveCurrent = null)
    {
        Title = "Exact retained historical Session file (inert source)";
        var file = attachment.File;
        var body = Compose(Describe(file.Review) + $"\nStorage revision: {attachment.StorageRevision.Value}\n"
            + $"Original session generation: {attachment.Generation.Value} / captured: {file.AdmittedAt:O}\n"
            + $"Revision: {file.RevisionId:D} / item: {file.ItemId:D}\nSHA-256 of original bytes: {file.Digest}",
            null, file.Text, AttachmentDisclosure);
        ShowSourceMeasurement(body, isCurrent, [file]);
        SetFindSource(file);
        ShowSearch(body, search, isCurrent, folder: false, async token =>
        {
            if (resolveCurrent is null)
            {
                throw new InvalidOperationException("Authoritative retained source revalidation is unavailable. Inspect again.");
            }
            var current = await resolveCurrent(token);
            if (current.Reference != file.Reference)
            {
                throw new InvalidOperationException("The exact retained source was replaced or removed. Inspect again.");
            }
            return [current];
        }, _ => { });
        if (remove is not null)
        {
            var button = new Button { Content = "Review Remove attachment and Kora copies" };
            AutomationProperties.SetName(button, "Review exact attachment, owning Session, revisions and all owned Kora copies before removal");
            button.Command = new AsyncCommand(remove, exception => reportFailure(
                "Removal held; no full-copy removal claimed. Failure type: " + exception.GetType().Name));
            DockPanel.SetDock(button, Dock.Top);
            body.Children.Insert(1, button);
        }
        if (replace is not null)
        {
            var replacement = new Button { Content = "Replace retained attachment" };
            AutomationProperties.SetName(replacement, "Pick a new source and review exact old and new attachments before replacement");
            replacement.Command = new AsyncCommand(replace, exception => reportFailure(
                "Replacement not completed. Old views retired; inspect exact durable state. Failure type: " + exception.GetType().Name));
            DockPanel.SetDock(replacement, Dock.Top);
            body.Children.Insert(1, replacement);
        }
    }

    internal void ShowAttachmentReplacement(SessionFileReplacement review, Func<Task> confirm)
    {
        Title = "Review replacement of this exact retained Session attachment";
        var old = review.Previous;
        var button = new Button { Content = "Confirm: replace this exact retained attachment with the newly selected file" };
        AutomationProperties.SetName(button, "Confirm exact old copy revocation and separately reviewed new durable Session capture");
        button.Command = new AsyncCommand(confirm, exception => reportFailure(
            "Replacement incomplete or held. Inspect exact durable status; no old body is restored. Failure type: " + exception.GetType().Name));
        Compose($"OLD historical snapshot — not a current filesystem observation:\n"
            + $"Session: {old.Session.Value:D} / current active generation: {old.Generation.Value}\n"
            + $"Historical origin: {review.PreviousMetadata.CanonicalPath} / identity: {review.PreviousMetadata.FileIdentity}\n"
            + $"Captured: {review.PreviousCapturedAt:O} / bytes: {review.PreviousMetadata.ByteLength}\n"
            + $"Storage revision: {old.StorageRevision.Value} / source: {old.File.SourceId:D}\n"
            + $"Revision: {old.File.RevisionId:D} / item: {old.File.ItemId:D} / SHA-256: {old.File.Digest}\n"
            + $"Exact old inventory revision: {old.InventoryRevision}\n\nNEW picker selection — metadata only, content not yet read:\n"
            + Describe(review.Next) + "\nSeparate explicit consent: capture and retain the newly selected source, revoke the old source, "
            + "and verify removal of its inventoried Kora copies. Every fresh picker selection receives a new source identity, even at the same path.\n"
            + "One slot is replaced atomically; sixteen retained attachments still permits replacement of this slot. No eviction or version history.\n"
            + "262,144 original UTF-8 bytes including BOM; .txt/.md/.markdown only. Review expires after two minutes.\n"
            + "Before commit failure/cancel preserves the exact old durable record, not its retired views. After commit uncertainty or failed copy "
            + "verification holds disclosure; explicitly inspect and review removal of the held snapshot. Neither original file is modified.",
            button, null, AttachmentDisclosure);
    }

    internal void ShowAttachmentRemoval(SessionFileRemoval review, Func<Task> confirm)
    {
        Title = "Confirm exact attachment removal and owned-copy deletion";
        var button = new Button { Content = "Confirm: Remove attachment and Kora copies" };
        AutomationProperties.SetName(button, "Confirm exact reviewed attachment revocation and inventoried Kora copy removal, leaving original user files unchanged");
        button.Command = new AsyncCommand(confirm, exception => reportFailure(
            "Removal incomplete or held. Body may already be revoked; inspect exact durable status. Failure type: " + exception.GetType().Name));
        Compose($"Exact session: {review.Session.Value:D} / generation: {review.Generation.Value}\n"
            + (review.ReplacementSwapUnconfirmed
                ? "REPLACEMENT HELD: a requested replacement has uncertain swap status. This is the exact current durable record, not a replacement result. Confirmation removes that record and verifies owned copies; no source is disclosed or restored.\n"
                : review.ReplacementCopyVerificationPending
                    ? "REPLACEMENT HELD: the swap committed but old-copy certification did not complete. Neither source is disclosed. This confirmation removes the held new snapshot and verifies the owned-copy inventory; it does not finish or restore replacement.\n" : string.Empty)
            + (review.BodyRetained ? "Body retained: confirmation revokes it before copy cleanup.\n"
                : "Body already revoked: confirmation retries only the exact inventoried Kora-copy cleanup; no source is restored.\n")
            + $"Storage revision: {review.StorageRevision.Value} / confirmation: {review.ConfirmationId:D}\n"
            + $"Source: {review.File.SourceId:D} / revision: {review.File.RevisionId:D} / item: {review.File.ItemId:D}\n"
            + $"Original-byte SHA-256: {review.File.Digest}\nInventory revision: {review.InventoryRevision}\n"
            + SessionFileAttachment.CopyInventory, button, null, AttachmentDisclosure);
    }

    private const string AttachmentDisclosure =
        "Explicit durable retention: Kora copies the exact original bytes into the existing private interaction database. "
        + "The historical snapshot follows this exact session's retention and deletion; it is readable locally after restart, including retained Done sessions. "
        + "Close only clears this native viewer, not durable content. Removal is a separate exact review and confirmation.\n"
        + "This is not a fresh filesystem observation, managed knowledge, model context, egress, execution, clipboard or history. "
        + "No watcher, refresh, extra version, network or original-file modification. Paths, headings and source instructions are untrusted data.\n"
        + SessionFileAttachment.CopyInventory;

    private DockPanel Compose(string metadata, Button? confirm, string? text, string disclosureText = Disclosure)
    {
        ClearContent();
        typographyHost?.EnsureReaderTypographyAvailable();
        content = CreateContent();
        if (typographyHost is not null) { NativeReaderTypography.Bind(content, typographyHost); }
        Width = 800;
        Height = 640;
        var body = new DockPanel { LastChildFill = false };
        var chrome = new StackPanel { Spacing = 8 };
        var identity = new NamedTextBlock { Text = metadata, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(identity, "Local file review identity and bounds");
        chrome.Children.Add(identity);
        var disclosure = new NamedTextBlock { Text = disclosureText, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(disclosure, "Local file preview privacy and unavailable capabilities");
        chrome.Children.Add(disclosure);
        if (confirm is not null) { chrome.Children.Add(confirm); }
        var reviewScroll = new ScrollViewer { Content = chrome, MaxHeight = text is null ? 320 : 160 };
        DockPanel.SetDock(reviewScroll, Dock.Top);
        body.Children.Add(reviewScroll);
        content.Text = text;
        AutomationProperties.SetName(content, "Untrusted local file plain text content");
        var source = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        AddCitationSpan(source);
        var sourceViewport = new ScrollViewer
        {
            Content = content,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        Grid.SetRow(sourceViewport, 1);
        source.Children.Add(sourceViewport);
        if (text is not null) { PrepareSourceWrapping(body, sourceViewport); }
        var layout = new Grid { Margin = new Avalonia.Thickness(16), RowDefinitions = new RowDefinitions("2*,3*") };
        layout.Children.Add(new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        });
        Grid.SetRow(source, 1);
        layout.Children.Add(source);
        Content = layout;
        return body;
    }

    private static NamedTextBlock CreateContent() => new() { FontFamily = new FontFamily("Consolas") };

    private void ClearContent()
    {
        ClearSourceWrapping();
        wrappingPanel = null;
        wrappingViewport = null;
        ClearSourceMeasurement();
        RemoveWhitespace();
        ClearAnswer();
        ClearNavigation();
        SetFindSource(null);
        findQuery = null;
        findStatus = null;
        findNext = null;
        findPrevious = null;
        searchSelection?.Dispose();
        searchSelection = null;
        isSearchCurrent = null;
        searchModes = null;
        content.Text = null;
        if (queryBox is not null) { queryBox.Text = null; }
        if (searchResults is not null) { searchResults.Text = null; }
        queryBox = null;
        searchResults = null;
        if (citationRows is not null) { citationRows.ItemsSource = null; }
        citationRows = null;
        exactSpanPanel = null;
        citedSpan = null;
        citedLocation = null;
        Content = null;
    }

    internal void ClearAndClose()
    {
        ClearContent();
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        ClearContent();
        base.OnClosed(e);
    }
}