using Kora.Application.Hosting;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Storage;
using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    internal async Task<LocalRequestProfileReview> ReviewLocalRequestProfileAsync(SessionWorkspaceEntry target,
        string body, RequestOrigin origin, ProviderProfileTuple profile, Func<bool> admitted, CancellationToken token)
    {
        SessionConversationService.RequireOriginalSource(origin);
        ConversationText.Validate(body, submission: true);
        ProviderProfileRules.ValidateTuple(profile);
        if (profile.Service != ProviderService.Local || providerProfileConfiguration is null || sessionProfiles is null)
        { throw new InvalidOperationException("A composed local request-profile consumer is required; no hosted route or fallback."); }
        var control = access.ControlRevision;
        var expires = time.GetUtcNow().AddSeconds(60);
        bool Live() => Eligible(control) && access.CanInspect && admitted() && time.GetUtcNow() < expires;
        var session = await sessionProfiles.ReadProviderProfile(target.Authority.SessionId, token).ConfigureAwait(false);
        if (!Live() || session.Held || !target.Authority.IsActive || session.Session != target.Authority
            || await workspace.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false) != target)
        { throw new InvalidOperationException("Select the exact current Active conversation and private original request."); }
        var resolved = await providerProfileConfiguration.ResolveTurnAsync(Live, session.Desired, token,
            localRequest: profile).ConfigureAwait(false);
        if (resolved.Tuple != profile || resolved.Selection is null || !resolved.Current())
        { throw new InvalidOperationException("The complete local tuple is not currently advertised. No substitution or model acquisition."); }
        async Task Validate(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!resolved.Current())
            { throw new InvalidOperationException("Request-only profile, preferences or original input changed; review again."); }
            var actual = await sessionProfiles.ReadProviderProfile(target.Authority.SessionId, cancellation).ConfigureAwait(false);
            var metadata = await workspace.ReadMetadataAsync(target.Authority.SessionId, cancellation).ConfigureAwait(false);
            var fresh = await providerProfileConfiguration.ResolveTurnAsync(Live, actual.Desired, cancellation,
                localRequest: profile).ConfigureAwait(false);
            if (actual.Session != session.Session || actual.Revision != session.Revision || actual.Held
                || !string.Equals(actual.Desired.Serialize(), session.Desired.Serialize(), StringComparison.Ordinal)
                || metadata != target || fresh.Tuple != profile || fresh.Selection != resolved.Selection
                || !resolved.Current() || !fresh.Current())
            { throw new InvalidOperationException("The exact session, source or local catalogue changed; nothing may be submitted."); }
        }
        await Validate(token).ConfigureAwait(false);
        return new(owner, target, body, origin, profile, resolved.Current, Validate);
    }

    internal async Task<ModelTurnAdmission> AdmitLocalRequestProfileAsync(LocalRequestProfileReview review,
        ModelContextEnvelope context, CancellationToken token)
    {
        if (review.Owner != owner || !review.TryConsume(context.Request) || !review.IsCurrent
            || context.Request.SessionId != review.Target.Authority.SessionId || context.Request.Origin != review.Origin
            || !string.Equals(context.UserRequest, review.Body, StringComparison.Ordinal))
        { return new(new(ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed)); }
        await review.ValidateAsync(token).ConfigureAwait(false);
        return await AdmitConfiguredProfileAsync(ModelTurnChoice.Local, context, null, token,
            localRequest: review.Profile).ConfigureAwait(false);
    }
}
