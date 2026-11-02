using Kora.Core.Communication;

namespace Kora.Application.Communication;

internal sealed class CallVoiceActivationReview(
    CallCommunicationPolicy owner, CallPolicyObservation observation,
    CallAwareSettings? savedSettings, Func<bool> eligible)
{
    private int consumed;

    internal CallPolicyObservation Observation { get; } = observation;
    internal CallAwareSettings? SavedSettings { get; } = savedSettings;
    internal bool IsEligible => eligible();

    internal bool TryConsume(CallCommunicationPolicy policy, CallAwareSettings settings) =>
        Interlocked.Exchange(ref consumed, 1) == 0
        && ReferenceEquals(owner, policy)
        && settings == Observation.Settings with { AllowVoiceActivationDuringCalls = true };
}
