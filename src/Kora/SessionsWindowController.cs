using Avalonia.Threading;

using Kora.Application.Diagnostics;
using Kora.Application.Hosting;
using Kora.Application.Interaction;
using Kora.Application.ViewModels;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class SessionsWindowController(
    MainViewModel main, SessionWorkspaceService service, DurableEvidenceQuery evidence,
    ISessionWorkspaceAccess access, ILogger<SessionsViewModel> logger, LocalEventBroker? localEvents = null,
    DetailWindowController? details = null, Kora.Application.Memory.MemoryManagementService? memories = null,
    SessionAttachmentWindowController? attachments = null,
    Kora.Application.Configuration.SessionProviderProfileConfigurationService? providers = null,
    SessionConversationService? conversation = null) : IDisposable
{
    private SessionsWindow? window;
    private bool disposed;
    internal Action? OpenAllWork { get; set; }

    internal void Bind()
    {
        main.SessionsRequested += OnOpen;
        main.PrivacyClosureRequested += OnPrivacyClosure;
        service.SessionLifecycleChanged += RevokeSession;
        service.SessionRetired += RevokeSession;
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
            var opened = new SessionsWindow(new(service, evidence, access, logger, localEvents,
                content => details?.OpenHistoryDetail(content, window)
                    ?? "History details unavailable: the native viewer is not composed.",
                session => details?.RevokeSession(session), memories, attachments, providers, conversation));
            opened.AllWorkRequested += (_, _) => OpenAllWork?.Invoke();
            opened.Closed += (_, _) => { if (ReferenceEquals(window, opened)) { window = null; } };
            window = opened;
            opened.Show();
        }
        window.Activate();
    }

    internal async Task NavigateAsync(Kora.Core.Hosting.HostId<Kora.Core.Hosting.SessionIdentity> session,
        Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (disposed || !access.CanInspect || !admitted())
        {
            throw new OperationCanceledException("Exact overview navigation is no longer admitted.");
        }
        Open();
        if (window?.DataContext is not SessionsViewModel model)
        {
            throw new InvalidOperationException("The Sessions window is unavailable.");
        }
        if (!model.CanRead) { throw new InvalidOperationException("Sessions is busy; retry passive navigation when idle."); }
        await model.SelectExactAsync(session, admitted, token);
    }

    internal void RevokeSession(Kora.Core.Hosting.HostId<Kora.Core.Hosting.SessionIdentity> session)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            // Lifecycle notifications run after storage I/O; revoke native content before the caller observes completion.
            Dispatcher.UIThread.Invoke(() => RevokeSession(session));
            return;
        }
        if (disposed) { return; }
        if (window?.DataContext is SessionsViewModel state)
        {
            if (state.ReferencesSession(session)) { window.ClosePrivate(); }
            else { state.RevokeSessionList(); }
        }
    }

    private void OnOpen(object? sender, EventArgs args) => Open();
    private void OnPrivacyClosure(object? sender, EventArgs args) => window?.ClosePrivate();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        OpenAllWork = null;
        main.SessionsRequested -= OnOpen;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        service.SessionLifecycleChanged -= RevokeSession;
        service.SessionRetired -= RevokeSession;
        window?.ClosePrivate();
        window = null;
    }
}
