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
    private CompactSessionWindow? compact;
    private SessionsViewModel? model;
    private SessionsPresentationOwner? presentation;
    private bool disposed;
    internal Action? OpenAllWork { get; set; }
    internal Action? OpenNewRequest { get; set; }

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
            EnsurePresentation();
            var opened = new SessionsWindow(model!, presentation);
            opened.AllWorkRequested += (_, _) => OpenAllWork?.Invoke();
            opened.NewRequestRequested += (_, _) => OpenNewRequest?.Invoke();
            opened.CompactConversationRequested += (_, _) => OpenCompact();
            opened.Closed += (_, _) =>
            {
                if (ReferenceEquals(window, opened)) { window = null; }
                ReleaseUnusedPresentation();
            };
            window = opened;
            opened.Show();
        }
        window.Activate();
    }

    internal void OpenCompact()
    {
        if (disposed || !access.CanInspect)
        {
            main.ReportHostInteractionFailure("Compact conversation unavailable: live private desktop ownership is required.");
            return;
        }
        if (compact is null)
        {
            EnsurePresentation();
            var opened = new CompactSessionWindow(model!, presentation!);
            opened.SessionsRequested += (_, _) => Open();
            opened.NewRequestRequested += (_, _) => OpenNewRequest?.Invoke();
            opened.Closed += (_, _) =>
            {
                if (ReferenceEquals(compact, opened)) { compact = null; }
                ReleaseUnusedPresentation();
            };
            compact = opened;
            opened.Show();
        }
        compact.Activate();
    }

    private void EnsurePresentation()
    {
        if (presentation is not null) { return; }
        model = new(service, evidence, access, logger, localEvents,
            content => details?.OpenHistoryDetail(content, window)
                ?? "History details unavailable: the native viewer is not composed.",
            session => details?.RevokeSession(session), memories, attachments, providers, conversation,
            (content, admitted, resolve, token) => details?.OpenConversationDetailAsync(content,
                window ?? (Avalonia.Controls.Window?)compact, admitted, resolve, token)
                ?? Task.FromResult("Conversation details unavailable: the native viewer is not composed."));
        presentation = new(model);
    }

    private void ReleaseUnusedPresentation()
    {
        if (window is not null || compact is not null) { return; }
        model = null;
        presentation = null;
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
        details?.RevokeSession(session);
        if (model is { } state)
        {
            if (state.ReferencesSession(session)) { presentation?.Revoke(); }
            else { state.RevokeSessionList(); }
        }
    }

    internal async Task<bool> NavigateSavedDraftAsync(SavedDraftSummary exact, Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (disposed || !access.CanInspect || !main.CanRevealPrivatePresentation || !admitted())
        { throw new OperationCanceledException("Original saved draft navigation was revoked."); }
        Open();
        if (window?.DataContext is not SessionsViewModel model)
        { throw new InvalidOperationException("The native Sessions window is unavailable."); }
        return await model.SelectSavedDraftAsync(exact, admitted, token);
    }

    private void OnOpen(object? sender, EventArgs args) => Open();
    private void OnPrivacyClosure(object? sender, EventArgs args) => presentation?.Revoke();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        OpenAllWork = null;
        OpenNewRequest = null;
        main.SessionsRequested -= OnOpen;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        service.SessionLifecycleChanged -= RevokeSession;
        service.SessionRetired -= RevokeSession;
        presentation?.Revoke();
        window = null;
        compact = null;
        model = null;
        presentation = null;
    }
}
