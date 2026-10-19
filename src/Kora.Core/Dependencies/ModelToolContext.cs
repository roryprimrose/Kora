using System.Collections.Immutable;

namespace Kora.Core.Dependencies;

public sealed class ModelToolContext
{
    internal ModelToolContext(ModelTurnProvenance provenance, string systemPolicy, string userRequest,
        ImmutableArray<ModelContextEvidence> evidence, ImmutableArray<byte> wire)
    {
        Provenance = provenance;
        SystemPolicy = systemPolicy;
        UserRequest = userRequest;
        Evidence = evidence;
        Wire = wire;
    }

    internal ModelTurnProvenance Provenance { get; }
    public string SystemPolicy { get; }
    public string UserRequest { get; }
    public ImmutableArray<ModelContextEvidence> Evidence { get; }
    public ImmutableArray<byte> Wire { get; }
}
