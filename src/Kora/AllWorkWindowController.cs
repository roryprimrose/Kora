using Avalonia.Threading;
using Avalonia.Controls;

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
    private AllWorkView? view;
    private Window? viewOwner;
    private Func<Window>? getWorkbench;
    private Action<AllWorkView>? presentWorkbench;
    private AllWorkViewModel? ActiveModel => view?.Model ?? window?.DataContext as AllWorkViewModel;
    internal AllWorkViewModel? CurrentModel => ActiveModel;
    internal void BindWorkbench(Func<Window> owner, Action<AllWorkView> present)
    {
        getWorkbench = owner;
        presentWorkbench = present;
    }
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
        if (ActiveModel is not { } model)
        {
            return Task.FromResult(WorkspaceSurfaceNavigationOutcome.Unavailable);
        }
        var revision = openedRevision;
        var native = view?.CapturePresentation() ?? (() => window is { IsVisible: true, WindowState: not WindowState.Minimized });
        bool Admitted() => !disposed && ReferenceEquals(ActiveModel, model) && native() && access.CanInspect
            && openedRevision == revision && access.ControlRevision == revision && navigation.IsAdmitted();
        waitingQuestionOperation = model.ReadWaitingQuestionsAsync(navigation with { IsAdmitted = Admitted });
        return waitingQuestionOperation;
    }

    internal void RetireWaitingQuestionsIfStale()
    {
        ActiveModel?.RetireWaitingQuestionsIfStale();
    }

    internal void Open(Func<bool> admitted)
    {
        if (disposed || !access.CanInspect || !admitted())
        {
            CloseHostedView();
            window?.Close();
            main.ReportHostInteractionFailure("All work unavailable: live private desktop ownership is required.");
            return;
        }
        var revision = access.ControlRevision;
        if (getWorkbench is not null && presentWorkbench is not null)
        {
            if (view is not null && openedRevision != revision) { CloseHostedView(); }
            if (view is null)
            {
                var model = CreateModel();
                viewOwner = getWorkbench();
                view = new(model, viewOwner);
                view.CloseRequested += OnHostedCloseRequested;
                viewOwner.Closed += OnHostedOwnerClosed;
                openedRevision = revision;
            }
            presentWorkbench(view);
            if (!access.CanInspect || access.ControlRevision != revision || !admitted())
            {
                CloseHostedView();
                main.ReportHostInteractionFailure("All work unavailable: private ownership changed while opening.");
            }
            return;
        }
        if (window is not null && openedRevision != revision) { window.Close(); }
        if (window is null)
        {
            var model = CreateModel();
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
        ActiveModel?.Revoke();
    }
    private AllWorkViewModel CreateModel()
    {
        var model = new AllWorkViewModel(service, access, logger, sessions.NavigateAsync);
        model.BindStopAll(main.StopAllWorkAsync);
        stopAllOwner = model;
        return model;
    }
    private void OnHostedCloseRequested(object? sender, EventArgs args) => CloseHostedView();
    private void OnHostedOwnerClosed(object? sender, EventArgs args) => CloseHostedView();
    private void CloseHostedView()
    {
        if (view is null) { return; }
        var retired = view;
        view = null;
        var owner = viewOwner;
        if (viewOwner is not null) { viewOwner.Closed -= OnHostedOwnerClosed; }
        viewOwner = null;
        retired.CloseRequested -= OnHostedCloseRequested;
        retiredStopAll = Task.WhenAll(retiredStopAll, retired.Model.StopAllOperation);
        if (owner is ResponseWindow workbench) { workbench.RemoveHostedView(retired); }
        retired.Dispose();
    }
    private void OnPrivacyClosure(object? sender, EventArgs args)
    {
        CloseHostedView();
        window?.Close();
    }
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        service.WorkOverviewInvalidated -= OnInvalidated;
        CloseHostedView();
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
