using Avalonia.Threading;

using Kora.Application;
using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora;

internal sealed partial class NewRequestWindowController(
    MainViewModel main, SessionWorkspaceService workspace, ISessionWorkspaceAccess access,
    Func<NewRequestComposer> create, SessionsWindowController sessions, IUiDispatcher dispatcher) : IDisposable
{
    private NewRequestView? window;
    private NewRequestWindow? adapter;
    private Func<Avalonia.Controls.Window>? getWorkbench;
    private Action<NewRequestView>? presentWorkbench;
    internal NewRequestView? View => window;
    internal void BindWorkbench(Func<Avalonia.Controls.Window> owner, Action<NewRequestView> present)
    {
        getWorkbench = owner;
        presentWorkbench = present;
    }
    private NewRequestComposer? composer;
    private long openingEpoch;
    private bool disposed;
    private long windowEpoch;
    internal Func<bool> CaptureShutdownEligibility(bool exitWithoutDisclosure = false)
    {
        var admitted = !disposed && (access.CanInspect && access.CanControl
            || exitWithoutDisclosure && access.CanReviewApplicationExit) && window is null && composer is null;
        var epoch = windowEpoch;
        var revision = access.ControlRevision;
        return () => admitted && !disposed && access.ControlRevision == revision
            && windowEpoch == epoch && window is null && composer is null;
    }

    internal async Task<bool> PrepareShutdown()
    {
        var original = window;
        if (original is null) { return true; }
        try
        {
            var revision = access.ControlRevision;
            return await original.PrepareShutdown() && !disposed && window is null && access.CanInspect
                && access.CanControl && access.ControlRevision == revision;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            main.ReportHostInteractionFailure("New request closing failed; unsent input remains open. Failure type: " + exception.GetType().Name);
            return false;
        }
    }
    private Kora.Application.Voice.ComposerDictationService? dictation;

    internal void BindDictation(Microsoft.Extensions.Logging.ILogger<Kora.Application.Voice.ComposerDictationService> logger) =>
        dictation = main.BindComposerDictation(logger);

    internal void Bind()
    {
        main.NewRequestOpeningRequested += OpenFromCommand;
        main.PrivacyClosureRequested += PrivacyClosed;
        workspace.SessionLifecycleChanged += RevokeSession;
        workspace.SessionRetired += RevokeSession;
        workspace.SessionMetadataChanging += RevokeDictationReview;
    }

    internal void Open()
    {
        CancelOriginalReuse();
        OpenCore();
    }

    private async Task<bool> OpenFromCommand(Func<bool> admitted)
    {
        var revision = access.ControlRevision;
        var epoch = Volatile.Read(ref openingEpoch);
        var opened = false;
        await dispatcher.InvokeAsync(() =>
        {
            bool Current() => !disposed && access.CanInspect && access.ControlRevision == revision
                && Volatile.Read(ref openingEpoch) == epoch && admitted();
            if (Current())
            {
                CancelOriginalReuse();
                opened = OpenCore(admitted: Current);
            }
            return Task.CompletedTask;
        });
        return opened;
    }

    private bool OpenCore(NewRequestComposer? seedTarget = null, Func<bool>? admitted = null)
    {
        bool Current() => !disposed && access.CanInspect && (admitted is null || admitted());
        if (!Current())
        {
            main.ReportHostInteractionFailure("New request unavailable: live private desktop ownership is required.");
            return false;
        }
        if (window is null)
        {
            windowEpoch++;
            var state = seedTarget ?? create();
            if (!Current())
            {
                if (seedTarget is null) { state.Dispose(); }
                return false;
            }
            void Report(Exception exception) => main.ReportHostInteractionFailure(
                "New request native navigation/closing failed; memory edits remain recoverable unless privacy revoked. No implicit Save or selection. Failure type: " + exception.GetType().Name);
            Func<bool> Capture()
                {
                    var revision = access.ControlRevision;
                    return () => !disposed && ReferenceEquals(composer, state) && access.CanInspect
                        && access.CanControl && access.ControlRevision == revision;
                }
            var model = new NewRequestViewModel(state, dictation);
            NewRequestView opened;
            if (getWorkbench is not null && presentWorkbench is not null)
            { opened = new(model, getWorkbench(), () => Navigate(state), Report, Capture); }
            else
            {
                adapter = new(model, () => Navigate(state), Report, Capture);
                opened = adapter.View;
            }
            opened.Closed += (_, _) =>
            {
                if (ReferenceEquals(window, opened))
                {
                    windowEpoch++;
                    Interlocked.Increment(ref openingEpoch);
                    window = null;
                    composer = null;
                    adapter = null;
                }
            };
            opened.CloseRequested += (_, _) =>
            {
                if (opened.Owner is ResponseWindow primary) { primary.RemoveHostedView(opened); }
            };
            composer = state;
            window = opened;
            BindDraftFind(opened, state);
            if (!Current()) { opened.ClosePrivate(); return false; }
            try
            {
                opened.OpenPrivate();
                if (presentWorkbench is not null) { presentWorkbench(opened); }
                else { adapter!.OpenPrivate(); }
            }
            catch
            {
                if (ReferenceEquals(window, opened)) { window = null; composer = null; }
                opened.ClosePrivate();
                throw;
            }
            if (!Current()) { opened.ClosePrivate(); return false; }
        }
        var exactWindow = window;
        if (!Current()) { return false; }
        if (exactWindow is not null && presentWorkbench is not null) { presentWorkbench(exactWindow); }
        exactWindow?.Activate();
        return Current() && exactWindow is { IsPresented: true }
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
        Interlocked.Increment(ref openingEpoch);
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => RevokeSession(session));
            return;
        }
        if (reuseSource == session) { CancelOriginalReuse(); }
        if (composer?.Creation?.Request.SessionId == session)
        {
            window?.ClosePrivate();
        }
    }

    private void PrivacyClosed(object? sender, EventArgs args)
    {
        Interlocked.Increment(ref openingEpoch);
        CancelOriginalReuse();
        window?.ClosePrivate();
    }

    private void RevokeDictationReview(HostId<SessionIdentity> session)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => RevokeDictationReview(session));
            return;
        }
        if (composer?.Creation?.Request.SessionId == session) { composer.Cancel(); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        Interlocked.Increment(ref openingEpoch);
        main.NewRequestOpeningRequested -= OpenFromCommand;
        CancelOriginalReuse();
        main.PrivacyClosureRequested -= PrivacyClosed;
        workspace.SessionLifecycleChanged -= RevokeSession;
        workspace.SessionRetired -= RevokeSession;
        workspace.SessionMetadataChanging -= RevokeDictationReview;
        window?.ClosePrivate();
        window = null;
        composer = null;
    }
}
