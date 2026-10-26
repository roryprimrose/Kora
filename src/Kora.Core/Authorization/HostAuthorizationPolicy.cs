using Kora.Core.Hosting;

namespace Kora.Core.Authorization;

// The admitted host resolves these gates inside the storage transaction, not from reply/provider fields.
public sealed record HostAuthorizationPolicy(
    bool IsUnlocked,
    bool OtherMandatoryGatesSatisfied,
    bool IsProtectedCall,
    bool IgnoreReusableGrants,
    bool AllowSpokenApprovalsDuringCalls = false)
{
    // Retain the legacy serialized field, but call entry no longer withdraws exact grants.
    public bool AllowsReusableGrants => true;

    public bool AllowsApprovalChannel(RequestOrigin channel) =>
        IsUnlocked && (channel == RequestOrigin.LocalUi
            || channel == RequestOrigin.ActivatedVoice && (!IsProtectedCall || AllowSpokenApprovalsDuringCalls));

    public bool AllowsVoiceOrCallSettings(RequestOrigin origin) =>
        origin == RequestOrigin.LocalUi || origin == RequestOrigin.ActivatedVoice && !IsProtectedCall;
}
