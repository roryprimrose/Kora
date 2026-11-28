using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    internal Task<ModelTurnAdmission> AdmitClaudeHistoryAsync(ModelContextEnvelope context,
        Func<CancellationToken, ValueTask<bool>> sourceCurrent, CancellationToken token) =>
        AdmitClaudeCoreAsync(context, false, null, sourceCurrent, token);

    public Task<ModelTurnAdmission> AdmitClaudeAsync(ModelContextEnvelope context, CancellationToken token,
        bool streamAnswer = false) => AdmitClaudeCoreAsync(context, streamAnswer, null, null, token);

    public Task<ModelTurnAdmission> AdmitClaudeAsync(ModelContextEnvelope context, SelectedHostedDefinition selection,
        Func<CancellationToken, ValueTask<bool>> sourceCurrent, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(selection);
        return AdmitClaudeCoreAsync(context, false, selection, sourceCurrent, token);
    }

    private async Task<ModelTurnAdmission> AdmitClaudeCoreAsync(ModelContextEnvelope context,
        bool streamAnswer, SelectedHostedDefinition? selection, Func<CancellationToken, ValueTask<bool>>? sourceCurrent,
        CancellationToken token)
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
        if (selection is not null && (context.ToolMode != ModelToolMode.None || sourceCurrent is null
            || !await sourceCurrent(token).ConfigureAwait(false) || !Live()))
        { return new(Finish(ModelTurnOutcome.DeniedEgress, ModelTurnReason.EgressNotAdmitted)); }
        var session = await sessionProfiles.ReadProviderProfile(issuer.Request.SessionId, token).ConfigureAwait(false);
        if (session.Held || !session.Session.IsActive || session.Session.SessionId != issuer.Request.SessionId || !Live())
        { return new(Finish(ModelTurnOutcome.Denied, ModelTurnReason.SessionNotAdmitted)); }
        var resolved = await providerProfileConfiguration.ResolveTurnAsync(Live, session.Desired, token).ConfigureAwait(false);
        if (!Live() || !resolved.Current() || resolved.Tuple is not { Service: ProviderService.Claude } profile)
        { return new(Finish(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel)); }
        if (selection is not null && (!await sourceCurrent!(token).ConfigureAwait(false) || !Live()))
        { return new(Finish(ModelTurnOutcome.DeniedEgress, ModelTurnReason.EgressNotAdmitted)); }
        var authorization = hostedReviews.PrepareClaudeTurn(context, profile, streamAnswer, selection, sourceCurrent);
        Volatile.Write(ref authorization.Used, 1);
        var admission = await AdmitConfiguredProfileAsync(ModelTurnChoice.Hosted, context, null, token, authorization).ConfigureAwait(false);
        if (admission.Turn is { } turn) { BindHostedAnswerCommit(turn, sourceCurrent); }
        return admission;
    }

    /// <summary>Revalidates host continuation and any selected source immediately before a hosted answer commits.</summary>
    private void BindHostedAnswerCommit(ModelTurn turn, Func<CancellationToken, ValueTask<bool>>? sourceCurrent) =>
        turn.ValidateAnswerCommit = async ct =>
        {
            var reason = await RevalidateContinuationAsync(turn, ct).ConfigureAwait(false);
            if (reason != ModelTurnReason.None) { return reason; }
            if (sourceCurrent is not null && !await sourceCurrent(ct).ConfigureAwait(false))
            { return ModelTurnReason.EgressNotAdmitted; }
            return await RevalidateContinuationAsync(turn, ct).ConfigureAwait(false);
        };
}
