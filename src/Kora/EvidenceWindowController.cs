using Avalonia.Threading;

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
    EvidenceAnswerWorkflow? answers = null, TimeProvider? time = null) : IDisposable
{
    private EvidenceWindow? window;
    private bool disposed;
    private long windowRevision;

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
        if (window is null)
        {
            EvidenceWindow opened;
            var model = new EvidenceViewModel(query, () => !disposed && access.CanInspect, logger);
            if (answers is not null)
            {
                model.BindEvidenceAnswers(answers, main.CaptureEvidenceAnswerAdmission,
                    () => workspaceAccess.ControlRevision, time ?? TimeProvider.System);
            }
            try { opened = new(model, main); }
            catch (InvalidOperationException)
            {
                model.Close();
                return "Evidence reader unavailable: reader text scale is unconfirmed or preference evidence changed. Inspect saved preference evidence before explicit repair.";
            }
            if (!admitted()) { model.Close(); opened.Close(); return "Evidence inspection unavailable: native opening was retired."; }
            opened.Closed += (_, _) => { if (ReferenceEquals(window, opened)) { window = null; windowRevision++; } };
            window = opened;
            windowRevision++;
            opened.Show();
        }
        if (window is not { } target || disposed || !admitted())
        {
            return "Evidence inspection unavailable: the original native opening was retired.";
        }
        target.Activate();
        return null;
    }

    internal async Task<bool> PrepareTraceAsync(EvidenceTracePreparation request)
    {
        var revision = windowRevision;
        var existing = window;
        var model = existing?.DataContext as EvidenceViewModel;
        var filters = model?.FilterRevision;
        var control = workspaceAccess.ControlRevision;
        bool Admitted() => !disposed && request.IsCurrent && main.CanRevealPrivatePresentation
            && access.CanInspect && workspaceAccess.CanInspect && control == workspaceAccess.ControlRevision;
        bool OriginalView() => revision == windowRevision && ReferenceEquals(existing, window)
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
            var target = window;
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
        bool Current() => Admitted() && windowRevision == openedRevision && ReferenceEquals(window, opened)
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

    private void OnPrivacyClosure(object? sender, EventArgs args) => window?.Close();

    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        main.PrivacyClosureRequested -= OnPrivacyClosure;
        main.EvidenceTracePreparationRequested -= PrepareTraceAsync;
        window?.Close();
        window = null;
    }
}
