namespace Kora.Core.Dependencies;

public sealed class SpeechComponentMutationAdmission
{
    public SpeechComponentMutationAdmission(SpeechComponentMutationState state, IAsyncDisposable? lease = null)
    {
        if (!Enum.IsDefined(state)) { throw new ArgumentOutOfRangeException(nameof(state)); }
        if ((state == SpeechComponentMutationState.Acquired) != (lease is not null))
        {
            throw new ArgumentException("Only acquired exact ownership can carry an exclusive lease.", nameof(lease));
        }
        State = state;
        Lease = lease;
    }

    public SpeechComponentMutationState State { get; }
    public IAsyncDisposable? Lease { get; }
    public string ReasonCode => State switch
    {
        SpeechComponentMutationState.Acquired => "exclusive-ownership-confirmed",
        SpeechComponentMutationState.Unavailable => "runtime-unavailable",
        SpeechComponentMutationState.Busy => "component-in-use",
        SpeechComponentMutationState.UnknownOwnership => "ownership-unknown",
        SpeechComponentMutationState.SharedOrUserOwned => "shared-or-user-owned",
        SpeechComponentMutationState.Denied => "mutation-denied",
        _ => throw new InvalidOperationException("Unknown mutation admission."),
    };
    public string Detail => State switch
    {
        SpeechComponentMutationState.Acquired => "Exclusive ownership and quiescence confirmed.",
        SpeechComponentMutationState.Unavailable => "No authoritative component mutation adapter is available. No assets changed.",
        SpeechComponentMutationState.Busy => "A known consumer is using these speech assets. Stop that use before a fresh review. No assets changed.",
        SpeechComponentMutationState.UnknownOwnership => "Exclusive ownership or quiescence is unknown. Inspect and resolve ownership before retrying. No assets changed.",
        SpeechComponentMutationState.SharedOrUserOwned => "Shared or user-owned speech assets are preserved. This operation cannot remove or replace them.",
        SpeechComponentMutationState.Denied => "Host policy denies this exact speech-data mutation. No assets changed.",
        _ => throw new InvalidOperationException("Unknown mutation admission."),
    };
}
