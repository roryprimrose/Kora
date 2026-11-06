namespace Kora.Core.Dependencies;

public sealed record SpeechComponentRuntimeObservation
{
    public SpeechComponentRuntimeObservation(SpeechComponentCompatibility compatibility,
        SpeechComponentFunctionalEvidence functional, string detail)
    {
        if (!Enum.IsDefined(compatibility)) { throw new ArgumentOutOfRangeException(nameof(compatibility)); }
        if (!Enum.IsDefined(functional)) { throw new ArgumentOutOfRangeException(nameof(functional)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        if (detail.Length > 512 || detail.Any(char.IsControl))
        {
            throw new ArgumentException("A bounded explicit host readiness reason is required.", nameof(detail));
        }
        Compatibility = compatibility;
        Functional = functional;
        Detail = detail;
    }

    public SpeechComponentCompatibility Compatibility { get; }
    public SpeechComponentFunctionalEvidence Functional { get; }
    public string Detail { get; }
    public long ConsumerRevision { get; init; }
    public int ActiveConsumers { get; init; }
    public string? NativeIdentity { get; init; }
    public bool ConsumersKnown { get; init; }
    public string? Dependents { get; init; }
}