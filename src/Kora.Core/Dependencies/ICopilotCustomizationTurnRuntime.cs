using Kora.Core.Providers;

namespace Kora.Core.Dependencies;

/// <summary>Optional native single-selection runtime capability. Absence is unavailable, not text fallback.</summary>
public interface ICopilotCustomizationTurnRuntime : ICopilotTurnRuntime
{
    /// <summary>Runs a tool-free selected turn, revalidating its exact source before callbacks and transmissions.</summary>
    Task<CopilotTurnReply> RunSelectedAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ReadOnlyMemory<byte> context, CopilotCustomizationSelection selection,
        Func<CancellationToken, ValueTask<bool>> sourceCurrent,
        Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission,
        CancellationToken cancellationToken);
}
