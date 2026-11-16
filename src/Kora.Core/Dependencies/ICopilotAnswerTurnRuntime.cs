using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Core.Dependencies;

public interface ICopilotAnswerTurnRuntime : ICopilotTurnRuntime
{
    Task<CopilotTurnReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple profile,
        ReadOnlyMemory<byte> context,
        Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CopilotTransmission, CancellationToken, ValueTask<bool>> admitTransmission,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish,
        CancellationToken cancellationToken);
}
