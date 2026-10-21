using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class NewRequestWindowController(
    MainViewModel main, SessionWorkspaceService workspace, ISessionWorkspaceAccess access,
    Func<NewRequestComposer> create, SessionsWindowController sessions) : IDisposable
{
    private NewRequestWindow? window;
    private NewRequestComposer? composer;
    private bool disposed;
    internal async Task<bool> PrepareShutdown()
    {
        var original = window;
        if (original is null) { return true; }
        try { return await original.PrepareShutdown(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            main.ReportHostInteractionFailure("New request closing failed; unsent input remains open. Failure type: " + exception.GetType().Name);
            return false;
        }
    }

    internal void Bind()
    {
        main.PrivacyClosureRequested += PrivacyClosed;
        workspace.SessionLifecycleChanged += RevokeSession;
        workspace.SessionRetired += RevokeSession;
    }

    internal void Open()
    {
        CancelOriginalReuse();
        OpenCore();
    }

    private bool OpenCore(NewRequestComposer? seedTarget = null)
    {
        if (disposed || !access.CanInspect)
        {
            main.ReportHostInteractionFailure("New request unavailable: live private desktop ownership is required.");
            return false;
        }
        if (window is null)
        {
            var state = seedTarget ?? create();
            var opened = new NewRequestWindow(new(state), () => Navigate(state),
                exception => main.ReportHostInteractionFailure("New request native navigation/closing failed; memory edits remain recoverable unless privacy revoked. No implicit Save or selection. Failure type: " + exception.GetType().Name),
                () =>
                {
                    var revision = access.ControlRevision;
                    return () => !disposed && ReferenceEquals(composer, state) && access.CanInspect
                        && access.CanControl && access.ControlRevision == revision;
                });
            opened.Closed += (_, _) =>
            {
                if (ReferenceEquals(window, opened)) { window = null; composer = null; }
            };
            composer = state;
            window = opened;
            BindDraftFind(opened, state);
            try { opened.OpenPrivate(); }
            catch
            {
                if (ReferenceEquals(window, opened)) { window = null; composer = null; }
                opened.ClosePrivate();
                throw;
            }
        }
        var exactWindow = window;
        exactWindow?.Activate();
        return !disposed && access.CanInspect && exactWindow is { IsVisible: true }
            && ReferenceEquals(window, exactWindow) && (seedTarget is null || ReferenceEquals(composer, seedTarget));
    }

    private async Task Navigate(NewRequestComposer state)
    {
        var exact = state.Creation?.Request.SessionId
            ?? throw new InvalidOperationException("No exact creation attempt exists.");
        var revision = access.ControlRevision;
        await sessions.NavigateAsync(exact, () => !disposed && ReferenceEquals(composer, state)
            && access.CanInspect && access.ControlRevision == revision, CancellationToken.None);
    }

    internal void RevokeSession(HostId<SessionIdentity> session)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => RevokeSession(session));
            return;
        }
        if (reuseSource == session) { CancelOriginalReuse(); }
        if (composer?.Creation?.Request.SessionId == session) { window?.ClosePrivate(); }
    }

    private void PrivacyClosed(object? sender, EventArgs args)
    {
        CancelOriginalReuse();
        window?.ClosePrivate();
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        CancelOriginalReuse();
        main.PrivacyClosureRequested -= PrivacyClosed;
        workspace.SessionLifecycleChanged -= RevokeSession;
        workspace.SessionRetired -= RevokeSession;
        window?.ClosePrivate();
        window = null;
        composer = null;
    }
}
