using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Providers;

namespace Kora.Core.Conversation;

/// <summary>Immutable canonical lineage, not permission to dispatch or disclose retained content.</summary>
public sealed record ConversationHandoffLineage(Guid Observation, ModelTurnProvenance Source,
    HostRevision SourceRevision, HostQuestionKey Review, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    string ContextDigest, ProviderProfileTuple Profile, ModelProviderSelection Destination)
{
    public void Validate(HostRequest successor)
    {
        if (Observation == Guid.Empty || Source is null || Source.Request is null || Source.Selection is null
            || Review is null || Source.Request.SessionId != successor.SessionId
            || Source.Request.Origin != successor.Origin || Source.Request.RequestId == successor.RequestId
            || Source.Request.TaskId == successor.TaskId || !Source.Selection.IsValid
            || Source.Selection.Provider != ModelProviderIdentity.Ollama
            || SourceRevision.Value != 2 || Review.Request != Source.Request
            || Profile is null || Profile.Service != ProviderService.Copilot || Destination is null
            || !Destination.IsValid || Destination.Provider != ModelProviderIdentity.Copilot
            || Destination.Revision != Profile.CatalogueRevision
            || ExpiresAt <= CreatedAt || ContextDigest is null || ContextDigest.Length != 64
            || !ContextDigest.All(char.IsAsciiHexDigit))
        { throw new InvalidDataException("A handoff requires exact original local execution, review, expiry and fresh successor lineage."); }
        Source.Turn.Validate();
        Review.QuestionId.Validate();
        ProviderProfileRules.ValidateTuple(Profile);
    }
}
