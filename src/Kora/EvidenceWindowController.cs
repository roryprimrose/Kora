using Avalonia.Threading;
using Avalonia.Controls;

using Kora.Application.Diagnostics;
using Kora.Application.Diagnostics.EvidenceAnswers;
using Kora.Application.Presentation;
using Kora.Application.ViewModels;
using Kora.Core.Diagnostics;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class EvidenceWindowController(
    MainViewModel main, DurableEvidenceQuery query, IEvidenceQueryAccess access,
    ILogger<EvidenceViewModel> logger, ISessionWorkspaceAccess workspaceAccess,
    EvidenceAnswerWorkflow? answers = null, TimeProvider? time = null,
    GitHubIssueReportWorkflow? problemReports = null, Kora.Core.Hosting.IApplicationInfo? applicationInfo = null,
    EvidencePageExportWindowController? exports = null) : IDisposable
{
    private EvidenceWindow? window;
    private EvidenceView? view;
    private Window? viewOwner;
    private Func<Window>? getWorkbench;
    private Action<EvidenceView>? presentWorkbench;
    private bool disposed;
    private long windowRevision;
    private readonly List<Task> exportOperations = [];

    internal void BindExports(EvidenceViewModel model)
    {
        if (exports is not null)
        {
            model.BindExport(exports, exportOperations.Add, operation => exportOperations.Remove(operation));
        }
    }

    internal async Task PrepareShutdownAsync()
    {
        CloseView();
        await Task.WhenAll(exportOperations);
        exportOperations.RemoveAll(operation => operation.IsCompleted);
    }

    internal Func<bool> CaptureShutdownEligibility()
    {
        var revision = windowRevision;
        return () => view is null && revision == windowRevision && exportOperations.All(operation => operation.IsCompleted);
    }

    internal void BindWorkbench(Func<Window> owner, Action<EvidenceView> present)
    {
        getWorkbench = owner;
        presentWorkbench = present;
    }

    internal void Bind()
    {
        main.PrivacyClosureRequested += OnPrivacyClosure;
        main.EvidenceTracePreparationRequested += PrepareTraceAsync;
    }

    internal void Open()
    {
        if (Open(static () => true) is { } failure) { main.ReportHostInteractionFailure(failure); }
    }

    private string? Open(Func<bool> admitted)
    {
        if (disposed || !access.CanInspect || !admitted())
        {
            return "Evidence inspection denied: live desktop ownership or privacy admission is unavailable.";
        }
        if (view is null)
        {
            EvidenceViewModel? model = null;
            model = new EvidenceViewModel(query, () => !disposed && access.CanInspect && main.CanRevealPrivatePresentation
                && view?.IsNativePresented == true && model is not null && view.IsBoundTo(model), logger);
            BindExports(model);
            if (problemReports is not null && applicationInfo is not null)
            {
                model.BindProblemReporting(problemReports, signature => new(signature, applicationInfo.Version,
                    Environment.OSVersion.Version.ToString(), System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()));
            }
            if (answers is not null)
            {
                model.BindEvidenceAnswers(answers, main.CaptureEvidenceAnswerAdmission,
                    () => workspaceAccess.ControlRevision, time ?? TimeProvider.System);
            }
            try
            {
                if (getWorkbench is not null && presentWorkbench is not null)
                {
                    viewOwner = getWorkbench();
                    view = new EvidenceView(model, viewOwner,
                        () => !disposed && access.CanInspect && main.CanRevealPrivatePresentation, main);
                    view.CloseRequested += OnViewCloseRequested;
                }
                else
                {
                    window = new EvidenceWindow(model, main);
                    view = window.View;
                    viewOwner = window;
                }
            }
            catch (InvalidOperationException)
            {
                model.Close();
                return "Evidence reader unavailable: reader text scale is unconfirmed or preference evidence changed. Inspect saved preference evidence before explicit repair.";
            }
            if (!admitted()) { CloseView(); return "Evidence inspection unavailable: native opening was retired."; }
            viewOwner.Closed += OnOwnerClosed;
            windowRevision++;
            if (presentWorkbench is not null) { presentWorkbench(view); }
            else { window!.Show(); }
        }
        else if (presentWorkbench is not null) { presentWorkbench(view); }
        if (view is not { } target || viewOwner is null || disposed || !admitted())
        {
            return "Evidence inspection unavailable: the original native opening was retired.";
        }
        viewOwner.Activate();
        viewOwner.UpdateLayout();
        if (!target.IsNativePresented)
        { return "Evidence inspection unavailable: the original view is not presented."; }
        return null;
    }

    internal async Task<bool> ReadAuditHistoryAsync(Kora.Core.Hosting.RequestOrigin origin, Func<bool> admitted)
    {
        var failure = Open(admitted);
        if (failure is not null)
        { main.ReportHostInteractionFailure(failure); return false; }
        var target = view ?? throw new InvalidOperationException("The native audit view is unavailable.");
        var model = target.DataContext as EvidenceViewModel
            ?? throw new InvalidOperationException("The native audit query owner is unavailable.");
        var revision = windowRevision;
        var native = target.CapturePresentation();
        bool Current() => !disposed && admitted() && native() && revision == windowRevision
            && ReferenceEquals(view, target) && ReferenceEquals(target.DataContext, model);
        var withdrawn = false;
        var observing = true;
        void OnInputChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (Dispatcher.UIThread.CheckAccess()) { RetireIfChanged(); }
            else { Dispatcher.UIThread.Post(RetireIfChanged); }
        }
        void RetireIfChanged()
        {
            if (!Volatile.Read(ref observing)) { return; }
            if (withdrawn || Current()) { return; }
            withdrawn = true;
            model.RetirePresentation();
        }
        main.PropertyChanged += OnInputChanged;
        try { return await model.ReadAuditHistoryAsync(origin, Current); }
        finally { Volatile.Write(ref observing, false); main.PropertyChanged -= OnInputChanged; }
    }

    internal async Task<bool> PrepareTraceAsync(EvidenceTracePreparation request)
    {
        var revision = windowRevision;
        var existing = view;
        var model = existing?.DataContext as EvidenceViewModel;
        var filters = model?.FilterRevision;
        var control = workspaceAccess.ControlRevision;
        bool Admitted() => !disposed && request.IsCurrent && main.CanRevealPrivatePresentation
            && access.CanInspect && workspaceAccess.CanInspect && control == workspaceAccess.ControlRevision;
        bool OriginalView() => revision == windowRevision && ReferenceEquals(existing, view)
            && (model is null || !model.IsClosed && model.FilterRevision == filters
                && ReferenceEquals(existing!.DataContext, model));
        if (!Admitted() || !OriginalView()) { return false; }
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Presentation);
        var opened = await Dispatcher.UIThread.InvokeAsync(() =>
        {
            using var activity = continuation();
            if (!Admitted() || !OriginalView())
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                return null;
            }
            var failure = Open(Admitted);
            var target = view;
            if (failure is not null || target is null || !Admitted()
                || model is not null && (model.IsClosed || model.FilterRevision != filters
                    || !ReferenceEquals(target.DataContext, model)))
            {
                activity.Complete(HostOperationOutcome.Failed);
                return null;
            }
            activity.Complete(HostOperationOutcome.Completed);
            return target;
        });
        if (opened is null) { return false; }
        var openedRevision = windowRevision;
        var openedModel = opened.DataContext as EvidenceViewModel;
        var expectedFilters = filters ?? 0;
        bool Current() => Admitted() && windowRevision == openedRevision && ReferenceEquals(view, opened)
            && openedModel is not null && !openedModel.IsClosed && ReferenceEquals(opened.DataContext, openedModel);
        async Task<bool> PrepareOpenedTrace()
        {
            using var activity = continuation();
            if (!Current() || openedModel!.FilterRevision != expectedFilters)
            {
                activity.Complete(HostOperationOutcome.Cancelled);
                return false;
            }
            var prepared = await opened.PrepareTraceAsync(request.TraceId, Current);
            activity.Complete(prepared ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            return prepared;
        }
        var preparation = await Dispatcher.UIThread.InvokeAsync<Task<bool>>(PrepareOpenedTrace, DispatcherPriority.Loaded);
        return await preparation;
    }

    private void OnViewCloseRequested(object? sender, EventArgs args) => CloseView();
    private void OnOwnerClosed(object? sender, EventArgs args) => CloseView();
    private void OnPrivacyClosure(object? sender, EventArgs args) => CloseView();

    private void CloseView()
    {
        var oldView = view;
        var oldOwner = viewOwner;
        var oldWindow = window;
        view = null;
        viewOwner = null;
        window = null;
        windowRevision++;
        if (oldOwner is not null) { oldOwner.Closed -= OnOwnerClosed; }
        if (oldView is not null)
        {
            oldView.CloseRequested -= OnViewCloseRequested;
            oldView.Dispose();
            if (oldOwner is ResponseWindow workbench) { workbench.RemoveHostedView(oldView); }
        }
        if (oldWindow is { IsVisible: true }) { oldWindow.Close(); }
    }

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        main.EvidenceTracePreparationRequested -= PrepareTraceAsync;
        CloseView();
    }
}
