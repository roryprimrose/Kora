using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class AllWorkWindowController(
    MainViewModel main, SessionWorkspaceService service, ISessionWorkspaceAccess access,
    ILogger<AllWorkViewModel> logger, SessionsWindowController sessions) : IDisposable
{
    private AllWorkWindow? window;
    private long openedRevision;
    private bool disposed;
    private bool bound;
    private long sourceRevision;

    internal Func<bool> CapturePreparationEligibility()
    {
        var original = window;
        var source = sourceRevision;
        var current = (original?.DataContext as AllWorkViewModel)?.CapturePreparationEligibility();
        return () => !disposed && bound && sourceRevision == source && ReferenceEquals(window, original)
            && (current is null || current());
    }

    internal bool PrepareTaskQuery(AllWorkTaskQueryPreparation preparation, Func<bool> admitted)
    {
        if (disposed || !bound || !access.CanInspect || !admitted()
            || window is not null && openedRevision != access.ControlRevision)
        {
            return false;
        }
        var revision = access.ControlRevision;
        var source = sourceRevision;
        var originalModel = (window?.DataContext as AllWorkViewModel)?.CapturePreparationEligibility();
        Open(admitted, reportFailure: false);
        var target = window;
        bool Current() => !disposed && access.CanInspect && access.ControlRevision == revision
            && sourceRevision == source && ReferenceEquals(window, target) && admitted();
        if (target?.DataContext is not AllWorkViewModel model || !Current()
            || originalModel is not null && !originalModel() || !target.FocusTaskQuery())
        {
            return false;
        }
        if (!Current() || originalModel is not null && !originalModel()
            || !model.PrepareTaskQuery(preparation.TaskId) || !Current())
        {
            model.Revoke();
            return false;
        }
        var preparedSource = model.CapturePreparationEligibility();
        preparation.Acknowledge(() => Current() && preparedSource() && target.IsVisible && target.IsTaskQueryFocused);
        return true;
    }

    internal void Bind()
    {
        if (bound || disposed) { return; }
        bound = true;
        main.PrivacyClosureRequested += OnPrivacyClosure;
        service.WorkOverviewInvalidated += OnInvalidated;
    }
    internal void Open() => Open(static () => true);
    internal void Open(Func<bool> admitted) => Open(admitted, reportFailure: true);
    private void Open(Func<bool> admitted, bool reportFailure)
    {
        if (disposed || !access.CanInspect || !admitted())
        {
            window?.Close();
            if (reportFailure) { main.ReportHostInteractionFailure("All work unavailable: live private desktop ownership is required."); }
            return;
        }
        var revision = access.ControlRevision;
        if (window is not null && openedRevision != revision) { window.Close(); }
        if (window is null)
        {
            var opened = new AllWorkWindow(new(service, access, logger, sessions.NavigateAsync));
            opened.Closed += (_, _) => { if (ReferenceEquals(window, opened)) { window = null; } };
            window = opened;
            openedRevision = revision;
            opened.Show();
        }
        if (!access.CanInspect || access.ControlRevision != revision || !admitted())
        {
            window?.Close();
            if (reportFailure) { main.ReportHostInteractionFailure("All work unavailable: private ownership changed while opening."); }
            return;
        }
        window?.Activate();
    }
    private void OnInvalidated(HostId<SessionIdentity> session)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => OnInvalidated(session));
            return;
        }
        sourceRevision++;
        if (window?.DataContext is AllWorkViewModel model) { model.Revoke(); }
    }
    private void OnPrivacyClosure(object? sender, EventArgs args) => window?.Close();
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        service.WorkOverviewInvalidated -= OnInvalidated;
        window?.Close();
        window = null;
    }
}
