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

    internal void Bind()
    {
        main.PrivacyClosureRequested += OnPrivacyClosure;
        service.WorkOverviewInvalidated += OnInvalidated;
    }
    internal void Open() => Open(static () => true);
    internal void Open(Func<bool> admitted)
    {
        if (disposed || !access.CanInspect || !admitted())
        {
            window?.Close();
            main.ReportHostInteractionFailure("All work unavailable: live private desktop ownership is required.");
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
            main.ReportHostInteractionFailure("All work unavailable: private ownership changed while opening.");
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
