using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed class NewRequestWindowController(
    MainViewModel main, SessionWorkspaceService workspace, ISessionWorkspaceAccess access,
    Func<NewRequestComposer> create, SessionsWindowController sessions) : IDisposable
{
    private NewRequestWindow? window;
    private NewRequestComposer? composer;
    private bool disposed;

    internal void Bind()
    {
        main.PrivacyClosureRequested += PrivacyClosed;
        workspace.SessionLifecycleChanged += RevokeSession;
        workspace.SessionRetired += RevokeSession;
    }

    internal void Open()
    {
        if (disposed || !access.CanInspect)
        {
            main.ReportHostInteractionFailure("New request unavailable: live private desktop ownership is required.");
            return;
        }
        if (window is null)
        {
            var state = create();
            var opened = new NewRequestWindow(new(state), () => Navigate(state),
                exception => main.ReportHostInteractionFailure("New request native navigation/closing failed; memory edits remain recoverable unless privacy revoked. No implicit Save or selection. Failure type: " + exception.GetType().Name));
            opened.Closed += (_, _) =>
            {
                if (ReferenceEquals(window, opened)) { window = null; composer = null; }
            };
            composer = state;
            window = opened;
            opened.Show();
        }
        window.Activate();
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
        if (composer?.Creation?.Request.SessionId == session) { window?.ClosePrivate(); }
    }

    private void PrivacyClosed(object? sender, EventArgs args) => window?.ClosePrivate();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PrivacyClosureRequested -= PrivacyClosed;
        workspace.SessionLifecycleChanged -= RevokeSession;
        workspace.SessionRetired -= RevokeSession;
        window?.ClosePrivate();
        window = null;
        composer = null;
    }
}
