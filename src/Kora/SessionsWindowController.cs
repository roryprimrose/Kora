using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.ViewModels;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class SessionsWindowController(
    MainViewModel main, SessionWorkspaceService service, DurableEvidenceQuery evidence,
    ISessionWorkspaceAccess access, ILogger<SessionsViewModel> logger, LocalEventBroker? localEvents = null) : IDisposable
{
    private SessionsWindow? window;
    private bool disposed;

    internal void Bind()
    {
        main.SessionsRequested += OnOpen;
        main.PrivacyClosureRequested += OnPrivacyClosure;
    }

    internal void Open()
    {
        if (disposed || !access.CanInspect)
        {
            main.ReportHostInteractionFailure("Sessions unavailable: live private desktop ownership is required.");
            return;
        }

        if (window is null)
        {
            var opened = new SessionsWindow(new(service, evidence, access, logger, localEvents));
            opened.Closed += (_, _) => { if (ReferenceEquals(window, opened)) { window = null; } };
            window = opened;
            opened.Show();
        }
        window.Activate();
    }

    internal void RevokeSession(Kora.Core.Hosting.HostId<Kora.Core.Hosting.SessionIdentity> session)
    {
        if (window?.DataContext is SessionsViewModel state)
        {
            if (state.ReferencesSession(session)) { window.Close(); }
            else { state.RevokeSessionList(); }
        }
    }

    private void OnOpen(object? sender, EventArgs args) => Open();
    private void OnPrivacyClosure(object? sender, EventArgs args) => window?.Close();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.SessionsRequested -= OnOpen;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        window?.Close();
        window = null;
    }
}
