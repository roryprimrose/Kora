using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    public Func<bool> CaptureLifetime()
    {
        var source = CaptureCompactNavigationEligibility();
        var preparation = model?.CaptureReplacementNavigationEligibility() ?? (static () => true);
        var epoch = Volatile.Read(ref dispositionOwnerEpoch);
        return () => !disposed && source() && preparation() && epoch == Volatile.Read(ref dispositionOwnerEpoch);
    }

    public async Task<bool> ReviewReplacementAsync(SessionQueueReplacementReviewCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (disposed || !access.CanControl || !main.CanRevealPrivatePresentation || !admitted())
        { throw new OperationCanceledException("Private exact replacement navigation is unavailable."); }
        Open();
        var owner = window ?? throw new InvalidOperationException("Native Sessions is unavailable.");
        var state = model ?? throw new InvalidOperationException("Native Sessions presentation is unavailable.");
        var epoch = Volatile.Read(ref dispositionOwnerEpoch);
        bool NativeAdmission() => !disposed && ReferenceEquals(window, owner) && ReferenceEquals(model, state)
            && Volatile.Read(ref dispositionOwnerOpen) == 1 && epoch == Volatile.Read(ref dispositionOwnerEpoch)
            && access.CanControl && access.CanInspect && main.CanRevealPrivatePresentation;
        return await state.PrepareExactReplacementReviewAsync(command, origin, admitted, NativeAdmission, token);
    }
}
