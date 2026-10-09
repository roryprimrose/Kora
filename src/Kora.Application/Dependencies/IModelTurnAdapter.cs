using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Application.Dependencies;

internal interface IModelTurnAdapter
{
    Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ReadOnlyMemory<byte> context,
        Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        CancellationToken cancellationToken);
}
