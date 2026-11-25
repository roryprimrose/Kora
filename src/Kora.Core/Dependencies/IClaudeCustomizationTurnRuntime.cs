using Kora.Core.Providers;

namespace Kora.Core.Dependencies;

/// <summary>Explicit one-request selected USER-copy capability; ordinary runtimes do not imply support.</summary>
public interface IClaudeCustomizationTurnRuntime : IClaudeTurnRuntime
{
    /// <summary>Runs the exact selected copy with independent physical transmission admission and current authority.</summary>
    Task<ClaudeTurnReply> RunSelectedAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ModelContextEnvelope context, SelectedHostedDefinition selection,
        Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token);
}
