using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Kora.Application.Hosting;
using Kora.Application.Infrastructure;
using Kora.Application.KnowledgeSources;
using Kora.Application.ViewModels;
using Kora.Core.Context;
using Kora.Core.Hosting;
using Kora.Core.KnowledgeSources;

namespace Kora.KnowledgeSources;

internal sealed class KnowledgeSourcesWindow : Window, IDisposable
{
    private readonly MainViewModel host;
    private readonly KnowledgeSourceManager manager;
    private readonly IUserFolderPicker picker;
    private readonly CancellationTokenSource lifetime = new();
    private readonly StackPanel body = new() { Spacing = 10, Margin = new Thickness(20) };
    private readonly ListBox sources = new();
    private readonly TextBox sourceName = new() { PlaceholderText = "Name for this device-local folder", MaxLength = 80 };
    private readonly CheckBox descendants = new() { Content = "Explicitly include bounded subfolders (depth 4; 32 directories including root)" };
    private readonly TextBlock status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel review = new() { Spacing = 8 };
    private readonly TextBox preview = new() { IsReadOnly = true, AcceptsReturn = true, MinHeight = 120, MaxHeight = 320 };
    private readonly TextBox query = new() { PlaceholderText = "Explicit lexical query in selected source only" };
    private readonly TextBlock results = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly StackPanel citationLinks = new() { Spacing = 4 };
    private KnowledgeSourceRevision? opened;
    private KnowledgeSource? openedSource;
    private bool running;
    private bool closing;
    private long epoch;
    private TaskCompletionSource? joined;
    private bool disposed;

    internal KnowledgeSourcesWindow(MainViewModel host, KnowledgeSourceManager manager, IUserFolderPicker? picker = null)
    {
        this.host = host;
        this.manager = manager;
        this.picker = picker ?? new KnowledgeSourceFolderPicker(this);
        Title = "Knowledge sources - device-local copies";
        Width = 840;
        Height = 780;
        sources.ItemTemplate = new FuncDataTemplate<KnowledgeSource>((item, _) => new TextBlock
        {
            Text = item is null ? string.Empty : $"{item.Name} - {item.State}\n{item.Folder.CanonicalPath}\n"
                + $"{item.Folder.Files.Count} files / {item.Folder.CombinedBytes} original bytes / {item.StoredBytes} stored bytes; "
                + $"last state change {item.ChangedAt:O}",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        sources.SelectionChanged += (_, _) => RetirePresentation();
        AutomationProperties.SetName(sources, "Explicit named knowledge source selection");
        AutomationProperties.SetName(sourceName, "Knowledge source name");
        AutomationProperties.SetName(preview, "Exact admitted untrusted text, never executed");
        AutomationProperties.SetName(query, "Search only the explicitly selected registered source");
        body.Children.Add(new TextBlock
        {
            Text = "Device-local, current Windows profile only. No automatic session or model context. "
                + "Adding and refreshing never grant model or hosted consent. Originals are never changed.\n"
                + "UTF-8 .txt/.md/.markdown only. Immediate files remain the default. Explicit subfolder opt-in is required before discovery. "
                + "32 files / 1 MiB total / 256 KiB each; 32 directories including root and empty nodes; root depth 0, maximum 4. "
                + "Each discovery/read/save/inspection has a 30-second work bound. Any forbidden item, link, exceeded bound or unstable inventory rejects the whole source.\n"
                + "Copies persist until explicit removal, separately from session history. Disable retains copies but blocks use. "
                + "Refresh immediately retires old use, even if cancelled. No watcher, embeddings, OCR or network.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        body.Children.Add(sourceName);
        AutomationProperties.SetName(descendants, "Explicit bounded descendant capture before discovery");
        body.Children.Add(descendants);
        body.Children.Add(Button("Choose folder and review metadata", AddAsync));
        body.Children.Add(Button("Reload source inventory", LoadAsync));
        body.Children.Add(sources);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Button("Refresh selected source (new review)", RefreshAsync));
        actions.Children.Add(Button("Disable selected source", DisableAsync));
        actions.Children.Add(Button("Review removal of Kora copies", ReviewRemovalAsync));
        actions.Children.Add(Button("Inspect selected exact stored snapshot", InspectAsync));
        body.Children.Add(actions);
        body.Children.Add(status);
        body.Children.Add(review);
        body.Children.Add(preview);
        body.Children.Add(query);
        body.Children.Add(Button("Search this exact inspected source locally", SearchAsync));
        body.Children.Add(results);
        body.Children.Add(citationLinks);
        Content = new ScrollViewer { Content = body };
        host.PropertyChanged += OnHostChanged;
        Closing += OnClosing;
        host.BindKnowledgeSourcesShutdown(StopAsync);
        Closed += (_, _) => Dispose();
    }

    private Button Button(string text, Func<Task> operation)
    {
        var button = new Button { Content = text, Margin = new Thickness(2) };
        AutomationProperties.SetName(button, text);
        button.Command = new AsyncCommand(() => RunAsync(operation), exception =>
            host.ReportHostInteractionFailure("Knowledge source operation failed; no completion is claimed. Failure type: "
                + exception.GetType().Name));
        return button;
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (running || closing) { throw new InvalidOperationException("The previous source operation must finish first."); }
        running = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        joined = completion;
        body.IsEnabled = false;
        try { await HostRequestRunner.RunAsync(RequestOrigin.LocalUi, operation); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException
            or System.Text.DecoderFallbackException
            or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or OperationCanceledException)
        {
            RetirePresentation();
            status.Text = "Not completed. Refresh/removal may already have revoked the source. Reload inventory and retry "
                + "a fresh review; never infer rollback. Storage/cleanup uncertainty is unavailable, not empty success. Failure type: "
                + exception.GetType().Name;
            host.ReportHostInteractionFailure(status.Text);
        }
        finally
        {
            running = false;
            body.IsEnabled = true;
            if (closing) { Close(); }
            completion.SetResult();
        }
    }

    private bool Eligible() => !closing && !lifetime.IsCancellationRequested
        && host.CanRevealPrivatePresentation && !host.IsProtectedCall;

    private async Task LoadAsync()
    {
        RetirePresentation();
        sources.ItemsSource = await manager.ListAsync(Eligible, lifetime.Token);
        status.Text = "Current private inventory loaded. Select one source explicitly; nothing was read from original files.";
    }

    private KnowledgeSource Selected() => sources.SelectedItem as KnowledgeSource
        ?? throw new InvalidOperationException("Select exactly one current named source.");

    private async Task AddAsync()
    {
        RetirePresentation();
        if (!Eligible())
        { throw new InvalidOperationException("Private native folder selection is unavailable."); }
        var generation = epoch;
        var scope = descendants.IsChecked == true ? KnowledgeSourceScope.BoundedDescendants : KnowledgeSourceScope.ImmediateFiles;
        var name = sourceName.Text ?? string.Empty;
        var path = await picker.SelectFolderAsync(lifetime.Token);
        if (!Eligible() || generation != epoch) { throw new InvalidOperationException("Native folder selection was revoked."); }
        if (path is null) { status.Text = "Folder selection cancelled; no content read or source added."; return; }
        ShowReview(await manager.ReviewAsync(name, path, null, scope,
            () => Eligible() && epoch == generation, lifetime.Token));
    }

    private async Task RefreshAsync()
    {
        var source = Selected();
        RetirePresentation();
        var generation = epoch;
        ShowReview(await manager.ReviewAsync(source.Name, source.Folder.CanonicalPath, source,
            () => Eligible() && epoch == generation, lifetime.Token));
    }

    private void ShowReview(KnowledgeSourceReview exact)
    {
        var generation = epoch;
        review.Children.Clear();
        review.Children.Add(new TextBlock
        {
            Text = "READ REVIEW (two minutes, one use)\n" + exact.Metadata.CanonicalPath
                + $"\nScope: {exact.Metadata.Scope}; named device-local, current profile; retained until removal, no model selection. "
                + "Limits: 32 files / 256 KiB each / 1 MiB total; depth 4; 32 directories including root/empty nodes; 30-second operations. "
                + "Any failure rejects the entire folder. May contain secrets. Review all members:\n"
                + string.Join("\n", exact.Metadata.Directories.Select(item => "Directory: "
                    + (string.Equals(item.CanonicalPath, exact.Metadata.CanonicalPath, StringComparison.Ordinal) ? "."
                        : KnowledgeSourcePolicy.RelativePath(exact.Metadata.CanonicalPath, item.CanonicalPath))
                    + " - native identity retained; exact membership revalidated"))
                + "\n" + string.Join("\n", exact.Metadata.Files.Select(item =>
                    $"{KnowledgeSourcePolicy.RelativePath(exact.Metadata.CanonicalPath, item.CanonicalPath)} - {item.ByteLength} bytes; {item.LastWrite:O}; native identity retained"))
                + $"\nComplete membership: {exact.Metadata.Directories.Count} directories / {exact.Metadata.Files.Count} files / {exact.Metadata.CombinedBytes} bytes. "
                + "Reading is separate from saving; review the resulting exact snapshot before publication.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        review.Children.Add(Button("Confirm read of this exact reviewed membership", async () =>
        {
            var candidate = await manager.CaptureAsync(exact.ReviewId,
                () => Eligible() && epoch == generation, lifetime.Token);
            review.Children.Clear();
            ShowFiles(candidate);
            review.Children.Add(new TextBlock
            {
                Text = "Review every captured item above. This exact complete immutable snapshot will be retained privately "
                    + "until removal, independently of sessions. It is historical, not a current-filesystem observation. "
                    + "Saving does not select it for a model. Confirmation expires in two minutes.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            });
            review.Children.Add(Button("Confirm save of this exact complete device-local snapshot", async () =>
            {
                await manager.PublishAsync(candidate, () => Eligible() && epoch == generation, lifetime.Token);
                await LoadAsync();
                status.Text = "Exact complete source saved. No model or hosted consent was granted.";
            }));
        }));
    }

    private void ShowFiles(KnowledgeSourceRevision snapshot)
    {
        var files = new ComboBox
        {
            ItemsSource = snapshot.Files,
            ItemTemplate = new FuncDataTemplate<LocalFileRevision>((item, _) => new TextBlock
            {
                Text = item is null ? string.Empty
                    : KnowledgeSourcePolicy.RelativePath(snapshot.Review.Metadata.CanonicalPath, item.Review.Metadata.CanonicalPath),
            }),
        };
        var generation = epoch;
        files.SelectionChanged += (_, _) =>
        {
            preview.Text = null;
            if (Eligible() && epoch == generation && files.SelectedItem is LocalFileRevision file)
            { preview.Text = file.Text; }
        };
        AutomationProperties.SetName(files, "Inspect every exact captured source item");
        review.Children.Add(files);
        files.SelectedIndex = 0;
    }

    private async Task InspectAsync()
    {
        var source = Selected();
        RetirePresentation();
        var generation = epoch;
        opened = await manager.OpenAsync(source, () => Eligible() && epoch == generation, lifetime.Token);
        openedSource = source;
        ShowFiles(opened);
        status.Text = "Explicit inspection of this historical registered snapshot only. It is not model context or current source access.";
    }

    private async Task SearchAsync()
    {
        citationLinks.Children.Clear();
        var source = openedSource ?? throw new InvalidOperationException("Explicitly inspect an enabled source first.");
        var snapshot = opened ?? throw new InvalidOperationException("No exact inspected snapshot is current.");
        var generation = epoch;
        _ = await manager.OpenAsync(source, Eligible, lifetime.Token);
        var found = new LocalFileLexicalRetrieval().Search(snapshot, snapshot.Reference,
            query.Text ?? string.Empty, DateTimeOffset.UtcNow, lifetime.Token);
        _ = await manager.OpenAsync(source, Eligible, lifetime.Token);
        if (!Eligible() || epoch != generation) { throw new InvalidOperationException("The inspected source was retired."); }
        results.Text = $"{found.Outcome}; {found.Citations.Count} excerpts; truncated: {found.Truncated}\n"
            + string.Join("\n\n", found.Citations.Select(item =>
                $"{item.DisplayIdentity}, lines {item.StartLine}-{item.EndLine}\n{item.Excerpt}"));
        foreach (var citation in found.Citations)
        {
            citationLinks.Children.Add(Button($"Inspect cited saved item: {citation.DisplayIdentity}, line {citation.StartLine}", async () =>
            {
                _ = await manager.OpenAsync(source, Eligible, lifetime.Token);
                if (!Eligible() || epoch != generation || !ReferenceEquals(opened, snapshot))
                { throw new InvalidOperationException("The exact cited snapshot was retired."); }
                var span = snapshot.ResolveCitation(citation);
                preview.Text = span.File.Text;
                preview.SelectionStart = citation.Start;
                preview.SelectionEnd = citation.Start + citation.Length;
                preview.CaretIndex = citation.Start;
                preview.Focus();
            }));
        }
    }

    private async Task DisableAsync()
    {
        var source = Selected();
        RetirePresentation();
        await manager.DisableAsync(source, Eligible, lifetime.Token);
        await LoadAsync();
        status.Text = "Use disabled; Kora copies retained. A fresh reviewed refresh is required to enable use.";
    }

    private Task ReviewRemovalAsync()
    {
        var source = Selected();
        RetirePresentation();
        var reviewedAt = DateTimeOffset.UtcNow;
        var generation = epoch;
        review.Children.Add(new TextBlock
        {
            Text = $"Remove '{source.Name}' and Kora copies only?\n{source.Folder.CanonicalPath}\n"
                + $"Inventory: one registry row, one encrypted complete snapshot ({source.StoredBytes} bytes), "
                + "and its private database rollback journal. No embeddings, indexes, backups or original-file deletion. "
                + "Use is revoked before cleanup. Failure leaves unavailable/retryable cleanup, not a claim of erasure. "
                + "This is logical deletion, not forensic erasure of hardware. Review expires in two minutes.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        review.Children.Add(Button("Confirm remove this inventoried source and Kora copies", async () =>
        {
            if (epoch != generation) { throw new InvalidOperationException("Removal review changed."); }
            RetirePresentation();
            await manager.RemoveAsync(source, reviewedAt, Eligible, lifetime.Token);
            await LoadAsync();
            status.Text = "Inventoried Kora copies removed. Original files unchanged; no hardware erasure claim.";
        }));
        return Task.CompletedTask;
    }

    private void RetirePresentation()
    {
        epoch++;
        opened = null;
        openedSource = null;
        preview.Text = null;
        query.Text = null;
        results.Text = null;
        citationLinks.Children.Clear();
        review.Children.Clear();
    }

    private void OnHostChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!Eligible()) { Close(); }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs args)
    {
        closing = true;
        lifetime.Cancel();
        RetirePresentation();
        args.Cancel = running;
    }

    internal async Task StopAsync()
    {
        if (disposed) { return; }
        Close();
#pragma warning disable VSTHRD003 // This completion signal has no dispatcher work; it joins the already owned operation.
        if (joined is { } completion) { await completion.Task; }
#pragma warning restore VSTHRD003
    }

    public void Dispose()
    {
        if (disposed) { return; }
        if (running) { throw new InvalidOperationException("Source work must terminate before native disposal."); }
        disposed = true;
        host.BindKnowledgeSourcesShutdown(null);
        host.PropertyChanged -= OnHostChanged;
        manager.Dispose();
        lifetime.Dispose();
        RetirePresentation();
    }
}
