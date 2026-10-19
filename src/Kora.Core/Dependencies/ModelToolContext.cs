using System.Collections.Immutable;

namespace Kora.Core.Dependencies;

public sealed class ModelToolContext
{
    internal ModelToolContext(string systemPolicy, string userRequest,
        ImmutableArray<ModelContextEvidence> evidence, ImmutableArray<byte> wire)
    {
        SystemPolicy = systemPolicy;
        UserRequest = userRequest;
        Evidence = evidence;
        Wire = wire;
    }

    public string SystemPolicy { get; }
    public string UserRequest { get; }
    public ImmutableArray<ModelContextEvidence> Evidence { get; }
    public ImmutableArray<byte> Wire { get; }
}
