using Kora.Core.Authorization;
using Kora.Core.Communication;

namespace Kora.Application.Communication;

public sealed record CallPolicyObservation(
    long Revision, CallState AutomaticState, bool ManualActive, CallAwareSettings Settings,
    bool ManualControlEvidenceUnavailable = false)
{
    public CallState EffectiveState => ManualActive ? CallState.Active : AutomaticState;
    public bool IsProtected => ManualControlEvidenceUnavailable || ManualActive || AutomaticState is not (CallState.Unavailable or CallState.Clear);
    public bool SuppressSpeech => ManualControlEvidenceUnavailable || IsProtected && Settings.ShowVisualTextDuringCalls;
    public bool AllowActivation => !ManualControlEvidenceUnavailable && (!IsProtected || Settings.AllowVoiceActivationDuringCalls);
    public HostAuthorizationPolicy Authorization(bool unlocked, bool mandatoryGatesSatisfied) =>
        new(unlocked, mandatoryGatesSatisfied, IsProtected, IgnoreReusableGrants: true);
}
