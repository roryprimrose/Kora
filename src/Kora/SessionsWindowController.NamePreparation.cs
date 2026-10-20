using Kora.Core.Commands;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    public async Task<bool> PrepareNameAsync(SessionNamePreparationCommand command, RequestOrigin origin,
        Func<bool> admitted, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (disposed || origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !command.IsValid || command.IsHelp || !access.CanControl
            || !main.CanRevealPrivatePresentation || !admitted())
        {
            throw new OperationCanceledException("Private native name preparation is unavailable.");
        }
        var originalModel = model;
        var source = model?.CaptureNavigationEligibility() ?? (static () => true);
        Open();
        var owner = window ?? throw new InvalidOperationException("The native Sessions window is unavailable.");
        var state = model ?? throw new InvalidOperationException("The native Sessions presentation is unavailable.");
        var ownerEpoch = Volatile.Read(ref dispositionOwnerEpoch);
        bool NativeAdmission() => !disposed && ReferenceEquals(window, owner)
            && Volatile.Read(ref dispositionOwnerOpen) == 1 && ownerEpoch == Volatile.Read(ref dispositionOwnerEpoch)
            && ReferenceEquals(model, state) && (originalModel is null || ReferenceEquals(originalModel, state))
            && source() && access.CanInspect && access.CanControl && main.CanRevealPrivatePresentation;
        var displayed = await state.PrepareNameAsync(command, origin, admitted, NativeAdmission, token);
        if (displayed && NativeAdmission()) { owner.FocusNamePreparation(); }
        return displayed && NativeAdmission();
    }
}
