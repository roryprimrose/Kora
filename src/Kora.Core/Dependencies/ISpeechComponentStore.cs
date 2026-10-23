namespace Kora.Core.Dependencies;

/// <summary>Platform-owned atomic storage for exact host-catalogued speech data.</summary>
public interface ISpeechComponentStore
{
    /// <summary>Inspects exact local content and ownership without changes.</summary>
    Task<SpeechComponentSnapshot> InspectAsync(SpeechComponentDefinition component, CancellationToken cancellationToken);
    /// <summary>Stages and verifies data, then publishes only if the reviewed snapshot remains current.</summary>
    Task PrepareAsync(SpeechComponentDefinition component, SpeechComponentSnapshot expected,
        Func<bool> admitted, IProgress<string> progress, CancellationToken cancellationToken);
    /// <summary>Removes only the exact managed component after snapshot and admission checks.</summary>
    Task RemoveAsync(SpeechComponentDefinition component, SpeechComponentSnapshot expected,
        Func<bool> admitted, CancellationToken cancellationToken);
}
