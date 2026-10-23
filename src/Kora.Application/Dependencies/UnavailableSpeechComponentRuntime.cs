using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>No optional input backend is selected by D-002 or registered in production.</summary>
public sealed class UnavailableSpeechComponentRuntime : ISpeechComponentRuntime
{
    public SpeechComponentRuntimeObservation Observe(SpeechComponentDefinition component) =>
        new(SpeechComponentCompatibility.Unavailable, SpeechComponentFunctionalEvidence.Unavailable,
            "No authoritative optional speech-input backend is registered in this Kora release.");

    public ValueTask<SpeechComponentMutationAdmission> TryAcquireExclusiveAsync(SpeechComponentDefinition component,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new SpeechComponentMutationAdmission(SpeechComponentMutationState.Unavailable));
    }

    public Task<SpeechComponentRuntimeObservation> VerifyAsync(SpeechComponentDefinition component, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Observe(component));
    }
}
