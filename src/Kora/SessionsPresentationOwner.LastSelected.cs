using Kora.Application.Hosting;

namespace Kora;

internal sealed partial class SessionsPresentationOwner
{
    private ForegroundConversationTarget? lastSelectedTarget;

    private void UpdateLastSelectedTarget()
    {
        var entry = model.SelectedSessionRecord;
        if (lastSelectedTarget is { IsResourceCurrent: true } current && current.Entry == entry) { return; }
        lastSelectedTarget?.Retire();
        lastSelectedTarget = null;
        if (!revoked && model.CanCompose && entry is not null)
        {
            var source = model.CaptureDraftPresentationEligibility();
            lastSelectedTarget = new(entry, () => !revoked && source() && model.CanCompose);
        }
        requestCommands?.PublishLastSelectedConversationTarget(lastSelectedTarget);
    }
}
