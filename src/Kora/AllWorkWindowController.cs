using Avalonia.Threading;

using Kora.Application.Hosting;
using Kora.Application.ViewModels;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class AllWorkWindowController(
    MainViewModel main, SessionWorkspaceService service, ISessionWorkspaceAccess access,
    ILogger<AllWorkViewModel> logger, SessionsWindowController sessions) : IDisposable, IAsyncDisposable
{
    private AllWorkWindow? window;
    private long openedRevision;
    private bool disposed;
    private AllWorkViewModel? stopAllOwner;
    private Task retiredStopAll = Task.CompletedTask;
    private Task<WorkspaceSurfaceNavigationOutcome> waitingQuestionOperation =
        Task.FromResult(WorkspaceSurfaceNavigationOutcome.Unavailable);
#pragma warning disable VSTHRD003 // Exposes only this controller's exact owned read for lifecycle observation.
    internal Task<WorkspaceSurfaceNavigationOutcome> WaitingQuestionOperation => waitingQuestionOperation;
#pragma warning restore VSTHRD003

    internal void Bind()
    {
        main.PrivacyClosureRequested += OnPrivacyClosure;
        service.WorkOverviewInvalidated += OnInvalidated;
    }
    internal void Open() => Open(static () => true);
    internal Task<WorkspaceSurfaceNavigationOutcome> ShowWaitingQuestionsAsync(WorkspaceSurfaceNavigation navigation)
    {
        if (disposed || !waitingQuestionOperation.IsCompleted)
        {
            return Task.FromResult(WorkspaceSurfaceNavigationOutcome.Unavailable);
        }
        Open(navigation.IsAdmitted);
        if (window is not { DataContext: AllWorkViewModel model } opened)
        {
            return Task.FromResult(WorkspaceSurfaceNavigationOutcome.Unavailable);
        }
        var revision = openedRevision;
        bool Admitted() => !disposed && ReferenceEquals(window, opened) && access.CanInspect
            && openedRevision == revision && access.ControlRevision == revision && navigation.IsAdmitted();
        waitingQuestionOperation = model.ReadWaitingQuestionsAsync(navigation with { IsAdmitted = Admitted });
        return waitingQuestionOperation;
    }

    internal void RetireWaitingQuestionsIfStale()
    {
        if (window?.DataContext is AllWorkViewModel model) { model.RetireWaitingQuestionsIfStale(); }
    }

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
            var model = new AllWorkViewModel(service, access, logger, sessions.NavigateAsync);
            model.BindStopAll(main.StopAllWorkAsync);
            stopAllOwner = model;
            var opened = new AllWorkWindow(model);
            opened.Closed += (_, _) =>
            {
                retiredStopAll = Task.WhenAll(retiredStopAll, model.StopAllOperation);
                if (ReferenceEquals(window, opened)) { window = null; }
            };
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

    public async ValueTask DisposeAsync()
    {
        Dispose();
#pragma warning disable VSTHRD003 // Join the original owned operation, including cancellation-ignoring storage.
        await waitingQuestionOperation;
        await retiredStopAll;
        if (stopAllOwner is not null) { await stopAllOwner.StopAllOperation; }
#pragma warning restore VSTHRD003
    }
}
