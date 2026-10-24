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
    private readonly Func<Guid, Task>? explain;
    private readonly Func<string?> explanation;
    private readonly DispatcherTimer? privacyTimer;
    private IClipboardPreviewView? window;
    private ClipboardSnapshot? showing;
    private bool disposed;

    public ClipboardPreviewWindowController(MainViewModel host)
        : this(() => host.ClipboardPreview, host.ReuseClipboardAsync, host.ClearClipboardPreview, host.RevokeClipboardAsync,
            () => new ClipboardPreviewWindow(exception => host.ReportHostInteractionFailure(
                "Native clipboard preview failed. No success is claimed. Failure type: " + exception.GetType().Name), host),
            host.ReportHostInteractionFailure, host.ExplainClipboardAsync, () => host.ClipboardExplanation)
    {
        this.host = host;
        host.ClipboardPreviewChanged += OnChanged;
        // Metadata-only revalidation also closes a preview if host ownership is lost.
        privacyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        privacyTimer.Tick += OnChanged;
        privacyTimer.Start();
    }

    internal ClipboardPreviewWindowController(Func<ClipboardSnapshot?> current, Func<Guid, Task> reuse,
        Action clear, Func<Task> revoke, Func<IClipboardPreviewView> create, Action<string> reportFailure,
        Func<Guid, Task>? explain = null, Func<string?>? explanation = null)
    {
        this.current = current;
        this.reuse = reuse;
        this.clear = clear;
        this.revoke = revoke;
        this.create = create;
        this.reportFailure = reportFailure;
        this.explain = explain;
        this.explanation = explanation ?? (static () => null);
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
        if (!ReferenceEquals(snapshot, showing)) { CloseView(); }
        if (snapshot is null) { return; }
        if (window is not null)
        {
            window.SetExplanation(explanation());
            return;
        }
        var id = snapshot.SnapshotId;
        var view = create();
        window = view;
        showing = snapshot;
        view.Closed += OnClosed;
        bool IsCurrent() => !disposed && ReferenceEquals(window, view) && ReferenceEquals(current(), snapshot)
            && !disposed && ReferenceEquals(window, view) && ReferenceEquals(showing, snapshot);
        // A queued callback cannot reuse or retarget another generation.
        view.Show(snapshot, () => IsCurrent()
            ? reuse(id) : Task.FromException(new InvalidOperationException("Clipboard preview generation is stale; start a new explicit request.")),
            () => IsCurrent() ? revoke()
                : Task.FromException(new InvalidOperationException("Clipboard preview generation is stale; nothing was revoked. Start a new explicit request.")),
            IsCurrent, explain is null ? null : () => IsCurrent() ? explain(id)
                : Task.FromException(new InvalidOperationException("Clipboard explanation source is stale; nothing was sent.")));
        if (!IsCurrent()) { CloseView(); }
        else { view.SetExplanation(explanation()); }
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, window)) { return; }
        var clearCurrent = ReferenceEquals(current(), showing);
        CloseView();
        if (clearCurrent) { clear(); }
    }

    private void CloseView()
    {
        var view = window;
        window = null;
        showing = null;
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
