using Kora.Core.Dependencies;
using Kora.Core.Tools;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

internal interface IModelTurnAdapter
{
    Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile, ReadOnlyMemory<byte> context,
        Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        CancellationToken cancellationToken);
}
