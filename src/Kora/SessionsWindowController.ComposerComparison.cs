using Kora.Application.Presentation;
using Kora.Core.Hosting;

namespace Kora;

internal sealed partial class SessionsWindowController
{
    public ComposerComparisonLaunch? Capture(HostId<SessionIdentity> session, RequestOrigin origin, Func<bool> admitted)
    {
        var owner = window;
        var state = model;
        var source = state?.CaptureComposerComparisonSource(session);
        var epoch = Volatile.Read(ref dispositionOwnerEpoch);
        var control = access.ControlRevision;
        var comparisonEpoch = state?.ComposerComparisonEpoch;
        var invoked = false;
        bool Current() => !disposed && origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
            && admitted() && main.CanRevealPrivatePresentation && access.CanInspect && access.CanControl
            && control == access.ControlRevision && epoch == Volatile.Read(ref dispositionOwnerEpoch)
            && ReferenceEquals(window, owner) && ReferenceEquals(model, state) && owner is { IsVisible: true }
            && source?.Invoke() == true;
        if (!Current()) { return null; }
        Task<bool> OpenComparison()
        {
            if (invoked || !Current() || state!.ComposerComparisonEpoch != comparisonEpoch) { return Task.FromResult(false); }
            invoked = true;
            return owner!.OpenComposerComparison(state, Current);
        }
        return new(Current, OpenComparison);
    }
}
