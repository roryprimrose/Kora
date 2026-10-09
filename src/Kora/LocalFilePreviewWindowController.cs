using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Kora.Application.ViewModels;
using Kora.Core.Context;

namespace Kora;

internal sealed class LocalFilePreviewWindowController : IUserFilePicker, IDisposable
{
    private readonly MainViewModel host;
    private readonly Window owner;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private LocalFilePreviewWindow? window;
    private Guid showing;
    private bool disposed;

    internal LocalFilePreviewWindowController(MainViewModel host, Window owner)
    {
        this.host = host;
        this.owner = owner;
        host.FilePreviewChanged += OnChanged;
        host.FileInspectionRequested += OnInspectionRequested;
        timer.Tick += OnChanged;
        timer.Start();
    }

    public async Task<string?> SelectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (disposed || !Dispatcher.UIThread.CheckAccess() || !owner.StorageProvider.CanOpen)
        {
            throw new InvalidOperationException("Trusted native file selection is unavailable.");
        }
        var selected = await owner.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Select one UTF-8 text or Markdown file for local review",
            AllowMultiple = false,
            FileTypeFilter = [new("Plain text / Markdown") { Patterns = ["*.txt", "*.md", "*.markdown"] }],
        });
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selected.Count == 0) { return null; }
            if (selected.Count != 1) { throw new InvalidOperationException("Exactly one local file must be selected."); }
            return selected[0].TryGetLocalPath()
                ?? throw new InvalidOperationException("Only a native fixed-drive local selection is supported.");
        }
        finally
        {
            foreach (var file in selected) { file.Dispose(); }
        }
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        try { Refresh(); }
        catch (InvalidOperationException)
        {
            CloseView();
            host.ClearFilePreview();
            host.ReportHostInteractionFailure("Native file review unavailable. Nothing was confirmed; make a fresh explicit selection.");
        }
    }

    private void Refresh()
    {
        if (disposed) { return; }
        var revision = host.FileRevision;
        var review = host.FileReview;
        var id = revision?.RevisionId ?? review?.ReviewId ?? Guid.Empty;
        if (id == showing) { return; }
        CloseView();
        if (id == Guid.Empty) { return; }
        showing = id;
        var view = new LocalFilePreviewWindow(host.ReportHostInteractionFailure);
        window = view;
        view.Closed += OnClosed;
        if (revision is not null)
        {
            var exactSource = revision.Reference;
            view.ShowRevision(revision, query => host.SearchFileAsync(exactSource, query),
                () => !disposed && host.FileRevision?.Reference == exactSource);
        }
        else
        {
            var exactId = review!.ReviewId;
            view.ShowReview(review, () => !disposed && host.FileReview?.ReviewId == exactId
                ? host.ConfirmFilePreviewAsync(exactId)
                : Task.FromException(new InvalidOperationException("The exact file review is stale; no read was authorized.")));
        }
        view.Show(owner);
        view.Activate();
        if (disposed || (host.FileRevision?.RevisionId ?? host.FileReview?.ReviewId ?? Guid.Empty) != id) { CloseView(); }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        CloseView();
        host.ClearFilePreview();
    }

    private void OnInspectionRequested(object? sender, EventArgs args)
    {
        Refresh();
        window?.FocusSearch();
    }

    private void CloseView()
    {
        var view = window;
        window = null;
        showing = Guid.Empty;
        if (view is null) { return; }
        view.Closed -= OnClosed;
        view.ClearAndClose();
    }

    public void Dispose()
    {
        disposed = true;
        host.FilePreviewChanged -= OnChanged;
        host.FileInspectionRequested -= OnInspectionRequested;
        timer.Stop();
        timer.Tick -= OnChanged;
        CloseView();
        host.ClearFilePreview();
    }
}
