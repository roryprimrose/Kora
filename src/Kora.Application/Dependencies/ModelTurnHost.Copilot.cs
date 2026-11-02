using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost : ICopilotTurnAdmission
{
    public async Task<ModelTurnAdmission> AdmitCopilotAsync(ModelContextEnvelope context, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(context);
        var issuer = HostActivity.Current;
        if (hostedReviews is null || providerProfileConfiguration is null || sessionProfiles is null || issuer is null
            || issuer.Activity!.IsStopped || issuer.Outcome != HostOperationOutcome.Unknown
            || !ReferenceEquals(issuer.Request, context.Request))
        { return new(Finish(ModelTurnOutcome.DeniedEgress, ModelTurnReason.EgressNotAdmitted)); }
        var control = access.ControlRevision;
        bool Live() => Eligible(control) && !issuer.Activity!.IsStopped && issuer.Outcome == HostOperationOutcome.Unknown;
        var session = await sessionProfiles.ReadProviderProfile(issuer.Request.SessionId, token).ConfigureAwait(false);
        if (session.Held || !session.Session.IsActive || session.Session.SessionId != issuer.Request.SessionId || !Live())
        { return new(Finish(ModelTurnOutcome.Denied, ModelTurnReason.SessionNotAdmitted)); }
        var resolved = await providerProfileConfiguration.ResolveTurnAsync(Live, session.Desired, token).ConfigureAwait(false);
        if (!Live() || !resolved.Current() || resolved.Tuple is not { Service: ProviderService.Copilot } profile)
        { return new(Finish(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel)); }
        return await AdmitCopilotAsync(hostedReviews.PrepareTurn(context, profile), token).ConfigureAwait(false);
    }

    /// <summary>Prepares an explicitly requested Copilot turn. Each final physical transmission still needs its own exact review.</summary>
    public Task<ModelTurnAdmission> AdmitCopilotAsync(CopilotTurnAuthorization authorization, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        var current = HostActivity.Current;
        if (providerProfileConfiguration is null || current is null
            || !ReferenceEquals(current.Request, authorization.Context.Request)
            || !authorization.Owner.Current(authorization)
            || Interlocked.Exchange(ref authorization.Used, 1) != 0)
        { return Task.FromResult(new ModelTurnAdmission(Finish(ModelTurnOutcome.DeniedEgress, ModelTurnReason.EgressNotAdmitted))); }
        return AdmitConfiguredProfileAsync(ModelTurnChoice.Hosted, authorization.Context, null, token, authorization);
    }

    private async ValueTask<bool> HostedCurrentAsync(ModelTurn turn, CancellationToken token)
    {
        if (turn.Profile is not { Service: ProviderService.Copilot or ProviderService.Claude } profile || !turn.IsRunning
            || await RevalidateContinuationAsync(turn, token).ConfigureAwait(false) != ModelTurnReason.None)
        { return false; }
        // This callback is bound only after configured-profile admission requires both stores.
        var saved = await sessionProfiles!.ReadProviderProfile(turn.Provenance.Request.SessionId, token).ConfigureAwait(false);
        return !saved.Held && saved.Session.IsActive && saved.Session.SessionId == turn.Provenance.Request.SessionId
            && saved.Session.Generation == turn.SessionGeneration && saved.Revision == turn.SessionPolicyRevision
            && await providerProfileConfiguration!.ValidateTurnAsync(profile, token).ConfigureAwait(false)
            && await RevalidateContinuationAsync(turn, token).ConfigureAwait(false) == ModelTurnReason.None;
    }
}
