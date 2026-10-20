using Avalonia.Threading;

using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    public Func<bool> CaptureNavigationEligibility()
    {
        var originalModel = model;
        var originalPresentation = presentation;
        var source = model?.CaptureQueueMoveNavigationEligibility() ?? (static () => true);
        return () => !disposed && ReferenceEquals(model, originalModel)
            && ReferenceEquals(presentation, originalPresentation) && source();
    }

    public async Task<bool> ReviewQueueMoveAsync(SessionQueueMoveReviewCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Dispatcher.UIThread.CheckAccess())
        { throw new InvalidOperationException("Queue move review must enter through the native dispatcher."); }
        if (disposed || !access.CanInspect || !access.CanControl || !main.CanRevealPrivatePresentation || !admitted())
        { throw new OperationCanceledException("Private native queue move preparation is unavailable."); }
        var hadPresentation = model is not null;
        var source = CaptureCompactNavigationEligibility();
        Open();
        var owner = window ?? throw new InvalidOperationException("The native Sessions window is unavailable.");
        var state = model ?? throw new InvalidOperationException("The native Sessions presentation is unavailable.");
        var ownerEpoch = Volatile.Read(ref dispositionOwnerEpoch);
        bool NativeAdmission() => !disposed && ReferenceEquals(window, owner)
            && Volatile.Read(ref dispositionOwnerOpen) == 1 && ownerEpoch == Volatile.Read(ref dispositionOwnerEpoch)
            && ReferenceEquals(model, state) && access.CanInspect && access.CanControl && main.CanRevealPrivatePresentation;
        if (hadPresentation && !source())
        { throw new OperationCanceledException("The original Sessions navigation source changed."); }
        return await state.PrepareQueueMoveReviewAsync(command, origin, admitted, NativeAdmission, token);
    }
}
