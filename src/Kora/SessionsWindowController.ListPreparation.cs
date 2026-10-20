using Avalonia.Threading;

using Kora.Application.Presentation;
using Kora.Core.Diagnostics;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    private async Task<bool> PrepareSessionList(SessionListPreparation request)
    {
        var control = access.ControlRevision;
        var originalModel = model;
        var originalOwner = presentation;
        var source = model?.CaptureListPreparationEligibility() ?? (static () => true);
        bool Admitted() => !disposed && request.IsAdmitted() && access.CanInspect
            && access.ControlRevision == control && main.CanRevealPrivatePresentation;
        bool Original() => ReferenceEquals(model, originalModel) && ReferenceEquals(presentation, originalOwner) && source();
        if (!Admitted() || !Original()) { return false; }
        var begin = HostActivity.CaptureContinuation(HostActivityLayer.Desktop, HostOperation.Presentation);
        return await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            using var activity = begin();
            if (!Admitted() || !Original()) { activity.Complete(HostOperationOutcome.Cancelled); return false; }
            Open();
            var target = window;
            var state = model;
            var owner = presentation;
            bool Native() => Admitted() && target is { IsVisible: true } && ReferenceEquals(window, target)
                && ReferenceEquals(model, state) && ReferenceEquals(presentation, owner);
            if (target is null || state is null || !Native()
                || originalModel is not null && (!ReferenceEquals(state, originalModel) || !source()))
            { activity.Complete(HostOperationOutcome.Cancelled); return false; }
            var result = await state.PrepareListQueryAsync(request.SessionId, Native, target.FocusPreparedListQuery);
            activity.Complete(result ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            return result;
        });
    }
}
