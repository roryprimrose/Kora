using System.Runtime.CompilerServices;

using Kora.Application.Hosting;
using Kora.Application.Tools;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;
using Kora.Core.Tools;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private readonly ConditionalWeakTable<ModelTurn, SessionDiscoveryInvocation> sessionDiscoveries = new();

    /// <summary>Explicit exact-source review and execution, not inferred routing or a provider-enablement route.</summary>
    public async Task<ModelTurnResult> RunSessionDiscoveryAsync(ModelContextEnvelope context,
        SessionWorkspaceService metadata, SessionDiscoveryPresenter present, CancellationToken token,
        SessionListSearchCursor? sourceCursor = null, int sourceLimit = SessionDiscoveryTool.MaximumRecords)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(present);
        var issuer = HostActivity.Current;
        if (issuer is null || issuer.Activity!.IsStopped || issuer.Outcome != HostOperationOutcome.Unknown
            || issuer.Request != context.Request || issuer.Request.InvocationId is not null
            || issuer.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || context.ToolMode != ModelToolMode.SessionDiscovery)
        { return Finish(ModelTurnOutcome.Denied, ModelTurnReason.OriginalUserRequired); }
        var control = access.ControlRevision;
        bool Live() => Eligible(control) && access.CanInspect
            && !issuer.Activity!.IsStopped && issuer.Outcome == HostOperationOutcome.Unknown
            && ContextReason(context, issuer.Request) == ModelTurnReason.None;
        if (!Live()) { return Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed); }
        // Admit the original task before even reading or presenting another session's metadata.
        var admitted = providerProfileConfiguration is null
            ? await AdmitAsync(ModelProviderSelection.OllamaCandidate, context, token).ConfigureAwait(false)
            : await AdmitAsync(ModelTurnChoice.Local, context, null, token).ConfigureAwait(false);
        if (admitted.Turn is not { } turn) { return admitted.Result; }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Tool);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            SessionDiscoveryTool.Id, SecurityAuditOutcome.Requested,
            issuer.Request.Origin == RequestOrigin.LocalUi ? SecurityAuditInitiator.TypedCommand : SecurityAuditInitiator.VoiceCommand,
            issuer.Request.SessionId.Value.ToString("D"));
        audit.Write(requested);
        using var invocation = new SessionDiscoveryInvocation(metadata, Live);
        try
        {
            var review = await invocation.PrepareAsync(context.UserRequest, sourceCursor, sourceLimit, token).ConfigureAwait(false);
            sessionDiscoveries.Add(turn, invocation);
            var reason = await RevalidateContinuationAsync(turn, token).ConfigureAwait(false);
            ModelTurnResult result;
            if (reason != ModelTurnReason.None)
            { result = Finish(ModelTurnOutcome.Denied, reason, turn.Provenance); }
            else
            {
                var decision = await present(review, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (decision != SessionDiscoveryDecision.Confirm)
                { result = Finish(ModelTurnOutcome.Denied, ModelTurnReason.ToolUseNotPermitted, turn.Provenance); }
                else if (!Live() || !await invocation.ConfirmAsync(token).ConfigureAwait(false))
                { result = Finish(ModelTurnOutcome.Denied, ModelTurnReason.SessionNotAdmitted, turn.Provenance); }
                else { result = await RunAsync(turn, token).ConfigureAwait(false); }
            }
            CompleteAudit(requested, result);
            activity.Complete(ToActivityOutcome(result.Outcome));
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            var result = Finish(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled, turn.Provenance);
            CompleteAudit(requested, result);
            activity.Complete(HostOperationOutcome.Cancelled);
            return result;
        }
        catch (Exception exception)
        {
            Fault(logger, exception.GetType().Name);
            CompleteAudit(requested, new(ModelTurnOutcome.Unknown, ModelTurnReason.ProviderOutcome, turn.Provenance));
            activity.Complete(HostOperationOutcome.Unknown);
            throw;
        }
    }

    private async ValueTask<bool> SessionDiscoveryCurrentAsync(ModelTurn turn, CancellationToken token) =>
        turn.Context.ToolMode != ModelToolMode.SessionDiscovery
        || sessionDiscoveries.TryGetValue(turn, out var invocation)
            && await invocation.ValidateAsync(token).ConfigureAwait(false);
}
