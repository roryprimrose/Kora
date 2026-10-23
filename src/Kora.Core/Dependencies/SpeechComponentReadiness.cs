namespace Kora.Core.Dependencies;

public sealed record SpeechComponentReadiness(SpeechComponentIdentity Identity, SpeechComponentSnapshot? Storage,
    SpeechComponentRuntimeObservation Runtime, bool Qualified, string? Recovery = null)
{
    public bool CanOffer => Storage is not null && Runtime.Compatibility == SpeechComponentCompatibility.Compatible && Qualified;
    public string Description => (Storage is { } observed
        ? $"Installed: {observed.Installed}; verified bytes: {observed.Verified}; ownership: {observed.Ownership}."
        : "Local data state: Unknown. " + Recovery)
        + $" Compatibility: {Runtime.Compatibility}; qualified: {Qualified}; functional evidence: {Runtime.Functional}. {Runtime.Detail} "
        + "Runtime selection and current usability are separate; no microphone or learning consent was granted.";
}
