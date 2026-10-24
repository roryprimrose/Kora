using Kora.Core.Providers;

namespace Kora.Core.Dependencies;

public interface IClaudeTurnRuntime
{
    bool IsQuiescent { get; }
    Task<ClaudeTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ModelContextEnvelope context, Func<ClaudeTransmission, CancellationToken, ValueTask<bool>> admit,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token);
}
