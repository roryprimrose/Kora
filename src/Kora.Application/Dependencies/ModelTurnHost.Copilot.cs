using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
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

    private static bool HostedAuthorized(ModelProviderRegistration registration, CopilotTurnAuthorization? authorization,
        ModelContextEnvelope context, HostActivity issuer) =>
        registration.Selection.Provider == ModelProviderIdentity.Copilot
        && registration.Adapter is CopilotModelTurnAdapter copilot && authorization is not null
        && Volatile.Read(ref authorization.Used) == 1 && ReferenceEquals(authorization.Context, context)
        && ReferenceEquals(authorization.Issuer.Request, issuer.Request)
        && copilot.Owns(authorization);

    private async ValueTask<bool> HostedCurrentAsync(ModelTurn turn, CancellationToken token)
    {
        if (turn.Profile is not { Service: ProviderService.Copilot } profile || !turn.IsRunning
            || await RevalidateAsync(turn, token).ConfigureAwait(false) != ModelTurnReason.None
            || sessionProfiles is null || providerProfileConfiguration is null)
        { return false; }
        var saved = await sessionProfiles.ReadProviderProfile(turn.Provenance.Request.SessionId, token).ConfigureAwait(false);
        return !saved.Held && saved.Session.IsActive && saved.Session.SessionId == turn.Provenance.Request.SessionId
            && saved.Session.Generation == turn.SessionGeneration && saved.Revision == turn.SessionPolicyRevision
            && await providerProfileConfiguration.ValidateTurnAsync(profile, token).ConfigureAwait(false)
            && await RevalidateAsync(turn, token).ConfigureAwait(false) == ModelTurnReason.None;
    }
}
