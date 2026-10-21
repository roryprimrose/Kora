using Avalonia.Threading;

using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    public async Task<bool> PrepareQueueCancellationAsync(SessionQueueCancellationReviewCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        var source = CaptureCompactNavigationEligibility();
        var input = model?.CaptureQueueCancellationInputEligibility() ?? (static () => true);
        var ownerEpoch = Volatile.Read(ref dispositionOwnerEpoch);
        var revision = access.ControlRevision;
        bool Eligible() => !disposed && access.CanInspect && access.CanControl && main.CanRevealPrivatePresentation
            && revision == access.ControlRevision && admitted() && !token.IsCancellationRequested;
        bool Original() => Eligible() && source() && input() && ownerEpoch == Volatile.Read(ref dispositionOwnerEpoch);
        if (!Eligible()) { throw new OperationCanceledException("Private queue cancellation preparation is unavailable."); }
        var preparation = await service.PrepareQueueCancellationAsync(command, origin, Original, token);
        var prepared = false;
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (!Original())
            { throw new OperationCanceledException("Original queue preparation navigation or input changed."); }
            EnsurePresentation();
            var state = model ?? throw new InvalidOperationException("The native Sessions presentation is unavailable.");
            var openingSource = state.CaptureQueueCancellationInputEligibility();
            Open();
            var owner = window ?? throw new InvalidOperationException("The native Sessions window is unavailable.");
            if (!openingSource() || !ReferenceEquals(model, state))
            { throw new OperationCanceledException("Original native source changed while opening Sessions."); }
            var openedEpoch = Volatile.Read(ref dispositionOwnerEpoch);
            // Storage callbacks consume native-event observations, not off-thread Avalonia properties.
            bool NativeAdmission() => Eligible() && ReferenceEquals(window, owner) && ReferenceEquals(model, state)
                && Volatile.Read(ref dispositionOwnerOpen) == 1 && openedEpoch == Volatile.Read(ref dispositionOwnerEpoch);
            prepared = await state.PrepareQueueCancellationAsync(preparation, NativeAdmission, token);
        });
        return prepared;
    }
}
