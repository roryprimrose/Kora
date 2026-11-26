using System.Collections.Immutable;
using Kora.Core.Providers;
using Microsoft.Extensions.AI;

namespace Kora.Core.Dependencies;

/// <summary>Host-only provenance/profile are not part of the model-visible serialized conversation.</summary>
public sealed class ModelToolRequest
{
    internal ModelToolRequest(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ModelToolContext context, ImmutableArray<ModelToolMessage> messages, int round, ModelToolBudget budget,
        bool clarificationAllowed = false, ImmutableArray<ChatMessage> chatMessages = default)
    {
        Provenance = provenance;
        Profile = profile;
        Context = context;
        Messages = messages;
        Round = round;
        Budget = budget;
        ClarificationAllowed = clarificationAllowed;
        ChatMessages = chatMessages.IsDefault ? [] : chatMessages;
    }

    public ModelTurnProvenance Provenance { get; }
    public ProviderProfileTuple? Profile { get; }
    public ModelToolContext Context { get; }
    public ImmutableArray<ModelToolMessage> Messages { get; }
    /// <summary>Volatile package USER/history companion, not policy or authority. Revalidate before disclosure.</summary>
    public ImmutableArray<ChatMessage> ChatMessages { get; }
    public int Round { get; }
    public ModelToolBudget Budget { get; }
    public bool ClarificationAllowed { get; }
    public bool FormsAllowed => ClarificationAllowed
        && ModelToolProtocol.AllowsForms(Context.ToolMode, Provenance.Selection.Provider);
}
