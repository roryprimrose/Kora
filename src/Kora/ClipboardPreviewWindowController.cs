using Avalonia.Threading;
using Kora.Application.ViewModels;
using Kora.Core.Context;

namespace Kora;

internal sealed class ClipboardPreviewWindowController : IDisposable
{
    private readonly MainViewModel? host;
    private readonly Func<ClipboardSnapshot?> current;
    private readonly Func<Guid, Task> reuse;
    private readonly Action clear;
    private readonly Func<Task> revoke;
    private readonly Func<IClipboardPreviewView> create;
    private readonly Action<string> reportFailure;
    private readonly DispatcherTimer? privacyTimer;
    private IClipboardPreviewView? window;
    private Guid showing;
    private bool disposed;

    public ClipboardPreviewWindowController(MainViewModel host)
        : this(() => host.ClipboardPreview, host.ReuseClipboardAsync, host.ClearClipboardPreview, host.RevokeClipboardAsync,
            () => new ClipboardPreviewWindow(exception => host.ReportHostInteractionFailure(
                "Native clipboard preview failed. No success is claimed. Failure type: " + exception.GetType().Name)),
            host.ReportHostInteractionFailure)
    {
        this.host = host;
        host.ClipboardPreviewChanged += OnChanged;
        // Metadata-only revalidation also closes a preview if host ownership is lost.
        privacyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        privacyTimer.Tick += OnChanged;
        privacyTimer.Start();
    }

    internal ClipboardPreviewWindowController(Func<ClipboardSnapshot?> current, Func<Guid, Task> reuse,
        Action clear, Func<Task> revoke, Func<IClipboardPreviewView> create, Action<string> reportFailure)
    {
        this.current = current;
        this.reuse = reuse;
        this.clear = clear;
        this.revoke = revoke;
        this.create = create;
        this.reportFailure = reportFailure;
    }

    private void OnChanged(object? sender, EventArgs args)
    {
        try { Refresh(); }
        catch (InvalidOperationException)
        {
            CloseView();
            clear();
            reportFailure("Native clipboard preview unavailable. No review or reuse is claimed; start a new explicit request.");
        }
    }

    internal void Refresh()
    {
        if (disposed) { return; }
        var snapshot = current();
        if (snapshot is null || snapshot.SnapshotId != showing) { CloseView(); }
        if (snapshot is null || window is not null) { return; }
        var id = snapshot.SnapshotId;
        var view = create();
        window = view;
        showing = id;
        view.Closed += OnClosed;
        // A queued callback cannot reuse or retarget another generation.
        view.Show(snapshot, () => !disposed && current()?.SnapshotId == id
            ? reuse(id) : Task.FromException(new InvalidOperationException("Clipboard preview generation is stale; start a new explicit request.")),
            () => !disposed && current()?.SnapshotId == id ? revoke()
                : Task.FromException(new InvalidOperationException("Clipboard preview generation is stale; nothing was revoked. Start a new explicit request.")));
        if (current()?.SnapshotId != id) { CloseView(); }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        CloseView();
        clear();
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
        if (host is not null) { host.ClipboardPreviewChanged -= OnChanged; }
        if (privacyTimer is not null)
        {
            privacyTimer.Stop();
            privacyTimer.Tick -= OnChanged;
        }
        CloseView();
        clear();
    }
}
