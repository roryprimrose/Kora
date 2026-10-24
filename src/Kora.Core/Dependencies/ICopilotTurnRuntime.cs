using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Core.Dependencies;

/// <summary>A host-owned runtime boundary; neither profiles nor SDK callbacks confer authority.</summary>
public interface ICopilotTurnRuntime
{
    bool IsQuiescent { get; }

    Task<CopilotTurnReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ReadOnlyMemory<byte> context,
        Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission,
        CancellationToken cancellationToken);
}
