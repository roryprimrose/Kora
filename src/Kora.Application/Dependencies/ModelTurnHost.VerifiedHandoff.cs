using System.Security.Cryptography;

using Kora.Core.Conversation;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private sealed record VerifiedHandoffAdmission(ModelContextEnvelope Context, VerifiedHandoffTarget Target);

    internal async Task<VerifiedHandoffTarget?> ResolveHandoffTargetAsync(ModelProviderPolicy policy, CancellationToken token)
    {
        if (providerProfileConfiguration is null || sessionProfiles is null || hostedReviews is null
            || policy.Hosted.Provider != ModelProviderIdentity.Copilot)
        { return null; }
        var control = access.ControlRevision;
        bool Live() => Eligible(control) && IsCurrentPolicy(policy) && policy.Mode == ModelProviderMode.LocalFirst;
        var session = await sessionProfiles.ReadProviderProfile(policy.Session, token).ConfigureAwait(false);
        if (session.Held || !session.Session.IsActive || session.Session.SessionId != policy.Session || !Live())
        { return null; }
        var resolved = await providerProfileConfiguration.ResolveTurnAsync(Live, session.Desired, token,
            ProviderService.Copilot).ConfigureAwait(false);
        if (resolved.Selection is not { } selection || !resolved.Current())
        { return null; }
        // ResolveTurnAsync returns the validated tuple and its selection together, or neither.
        var profile = resolved.Tuple!;
        var registration = registrationSource is null
            ? registrations.SingleOrDefault(item => item.Selection == selection)
            : await registrationSource.ResolveAsync(selection, profile, token).ConfigureAwait(false);
        if (registration is null || registration.Selection != selection || registration.Adapter is not CopilotModelTurnAdapter
            || !registration.IsQualified(time.GetUtcNow()) || !resolved.Current())
        { return null; }
        return new(profile, selection, session, resolved.Current);
    }

    internal async Task<bool> HandoffTargetCurrentAsync(ModelHandoffOffer offer, CancellationToken token)
    {
        if (offer.Target is not { } target || !target.Current()) { return false; }
        var current = await ResolveHandoffTargetAsync(offer.Policy, token).ConfigureAwait(false);
        return current is not null && current.Profile == target.Profile && current.Selection == target.Selection
            && current.Session.Session == target.Session.Session && current.Session.Revision == target.Session.Revision
            && string.Equals(current.Session.Desired.Serialize(), target.Session.Desired.Serialize(), StringComparison.Ordinal)
            && target.Current();
    }

    internal async Task<ModelTurnAdmission> AdmitVerifiedHandoffAsync(ModelHandoffOffer offer,
        ConversationHandoffCommit committed, ModelContextEnvelope context, CancellationToken token)
    {
        var issuer = HostActivity.Current;
        if (!ReferenceEquals(offer.Host, this) || Volatile.Read(ref offer.Retired) != 0
            || workspace is not ISessionConversationStore conversations || issuer is null
            || !ReferenceEquals(issuer.Request, context.Request) || issuer.Activity!.IsStopped
            || issuer.Outcome != HostOperationOutcome.Unknown
            || offer.LocalUnavailable is not { IsCurrent: true } observation || offer.Target is not { } target
            || committed.Source.Message.Outcome != ModelTurnOutcome.Unavailable
            || committed.Source.Task.State != HostTaskState.Unavailable
            || committed.Source.Message.Execution != observation.Result.Provenance
            || !string.Equals(committed.Source.Message.Body, offer.Context.UserRequest, StringComparison.Ordinal)
            || committed.Successor.Message.Request != context.Request
            || !string.Equals(committed.Successor.Message.Body, context.UserRequest, StringComparison.Ordinal)
            || committed.Successor.Message.Handoff is not { } lineage
            || lineage.Observation != observation.Id || lineage.Source != observation.Result.Provenance
            || lineage.Review != offer.Question.Key || lineage.CreatedAt != offer.Context.CreatedAt
            || lineage.ExpiresAt != offer.Context.ExpiresAt
            || context.CreatedAt != lineage.CreatedAt || context.ExpiresAt != lineage.ExpiresAt
            || lineage.Profile != target.Profile || lineage.Destination != target.Selection
            || !string.Equals(lineage.ContextDigest, Convert.ToHexStringLower(SHA256.HashData(context.Serialize())), StringComparison.Ordinal)
            || !await HandoffTargetCurrentAsync(offer, token).ConfigureAwait(false))
        { return new(Finish(ModelTurnOutcome.Denied, ModelTurnReason.HandoffReviewStale)); }
        if (await conversations.ReadConversationMessageAsync(context.Request.SessionId,
                committed.Source.Message.Request.RequestId, token).ConfigureAwait(false) != committed.Source.Message
            || await conversations.ReadConversationMessageAsync(context.Request.SessionId,
                context.Request.RequestId, token).ConfigureAwait(false) != committed.Successor.Message
            || !observation.IsCurrent
            || !observation.TryUseSuccessor())
        { return new(Finish(ModelTurnOutcome.Denied, ModelTurnReason.HandoffReviewStale)); }
        var authorization = hostedReviews!.PrepareTurn(context, target.Profile);
        Interlocked.Exchange(ref authorization.Used, 1);
        return await AdmitConfiguredProfileAsync(ModelTurnChoice.Hosted, context, null, token,
            authorization, new(context, target)).ConfigureAwait(false);
    }
}
