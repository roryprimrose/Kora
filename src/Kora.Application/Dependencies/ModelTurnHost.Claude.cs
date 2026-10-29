using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    public async Task<ModelTurnAdmission> AdmitClaudeAsync(ModelContextEnvelope context, CancellationToken token,
        bool streamAnswer = false)
    {
        ArgumentNullException.ThrowIfNull(context);
        var issuer = HostActivity.Current;
        if (hostedReviews is null || providerProfileConfiguration is null || sessionProfiles is null || issuer is null
            || issuer.Activity!.IsStopped || issuer.Outcome != HostOperationOutcome.Unknown
            || !ReferenceEquals(issuer.Request, context.Request) || !ClaudeObservationTools.Supports(context)
            || streamAnswer && (context.ToolMode != ModelToolMode.None
                || issuer.Request.Origin != Kora.Core.Hosting.RequestOrigin.LocalUi))
        { return new(Finish(ModelTurnOutcome.DeniedEgress, ModelTurnReason.EgressNotAdmitted)); }
        var control = access.ControlRevision;
        bool Live() => Eligible(control) && !issuer.Activity!.IsStopped && issuer.Outcome == HostOperationOutcome.Unknown;
        var session = await sessionProfiles.ReadProviderProfile(issuer.Request.SessionId, token).ConfigureAwait(false);
        if (session.Held || !session.Session.IsActive || session.Session.SessionId != issuer.Request.SessionId || !Live())
        { return new(Finish(ModelTurnOutcome.Denied, ModelTurnReason.SessionNotAdmitted)); }
        var resolved = await providerProfileConfiguration.ResolveTurnAsync(Live, session.Desired, token).ConfigureAwait(false);
        if (!Live() || !resolved.Current() || resolved.Tuple is not { Service: ProviderService.Claude } profile)
        { return new(Finish(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel)); }
        var authorization = hostedReviews.PrepareClaudeTurn(context, profile, streamAnswer);
        Volatile.Write(ref authorization.Used, 1);
        var admission = await AdmitConfiguredProfileAsync(ModelTurnChoice.Hosted, context, null, token, authorization).ConfigureAwait(false);
        if ((streamAnswer || context.ToolMode == ModelToolMode.HostedClarification) && admission.Turn is { } turn)
        { turn.ValidateAnswerCommit = ct => RevalidateContinuationAsync(turn, ct); }
        return admission;
    }
}
