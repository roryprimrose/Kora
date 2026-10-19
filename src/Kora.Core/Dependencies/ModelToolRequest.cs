using System.Collections.Immutable;
using Kora.Core.Providers;

namespace Kora.Core.Dependencies;

/// <summary>Host-only provenance/profile are not part of the model-visible serialized conversation.</summary>
public sealed class ModelToolRequest
{
    internal ModelToolRequest(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ModelToolContext context, ImmutableArray<ModelToolMessage> messages, int round, ModelToolBudget budget)
    {
        Provenance = provenance;
        Profile = profile;
        Context = context;
        Messages = messages;
        Round = round;
        Budget = budget;
    }

    public ModelTurnProvenance Provenance { get; }
    public ProviderProfileTuple? Profile { get; }
    public ModelToolContext Context { get; }
    public ImmutableArray<ModelToolMessage> Messages { get; }
    public int Round { get; }
    public ModelToolBudget Budget { get; }
}
