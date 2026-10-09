using Kora.Core.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private readonly Dictionary<HostId<SessionIdentity>, ModelProviderPolicy> policies = [];
    private readonly Lock policyGate = new();

    /// <summary>Seeds a session once from confirmed device state; subsequent preference edits do not replace its policy.</summary>
    public async Task<(ModelTurnResult Result, ModelProviderPolicy? Policy)> InitializePolicyAsync(CancellationToken token)
    {
        var current = HostActivity.Current;
        if (current is null || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown)
        { return (Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired), null); }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "model.policy", SecurityAuditOutcome.Requested, SecurityAuditInitiator.System, current.Request.SessionId.Value.ToString("D"));
        audit.Write(requested);
        try
        {
            var control = access.ControlRevision;
            bool Live() => Eligible(control) && !current.Activity!.IsStopped && current.Outcome == HostOperationOutcome.Unknown;
            var session = await workspace.ReadMetadataAsync(current.Request.SessionId, token).ConfigureAwait(false);
            var task = await workspace.ReadTaskAsync(current.Request.SessionId, current.Request.TaskId, token).ConfigureAwait(false);
            lock (policyGate)
            {
                var reason = current.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                    ? ModelTurnReason.OriginalUserRequired
                    : !Live() || session.Authority.SessionId != current.Request.SessionId || !session.Authority.IsActive
                        || session.Authority.Generation.Value <= 0 || !OwnsTask(task, current.Request, session.Authority.Generation)
                        ? ModelTurnReason.HostAdmissionClosed : ModelTurnReason.None;
                if (token.IsCancellationRequested || reason != ModelTurnReason.None)
                {
                    var denied = token.IsCancellationRequested ? Finish(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled)
                        : Finish(ModelTurnOutcome.Denied, reason);
                    CompleteAudit(requested, denied);
                    activity.Complete(ToActivityOutcome(denied.Outcome));
                    return (denied, null);
                }
                var previous = policies.GetValueOrDefault(current.Request.SessionId);
                if (previous is not null)
                {
                    var result = Finish(ModelTurnOutcome.Succeeded, ModelTurnReason.None);
                    CompleteAudit(requested, result);
                    if (token.IsCancellationRequested || !Live() || !IsCurrentPolicy(previous))
                    {
                        result = token.IsCancellationRequested
                            ? Finish(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled)
                            : Finish(ModelTurnOutcome.Denied, ModelTurnReason.PolicyChanged);
                        CompleteAudit(requested, result);
                        activity.Complete(ToActivityOutcome(result.Outcome));
                        return (result, null);
                    }
                    activity.Complete(HostOperationOutcome.Completed);
                    return (result, previous);
                }
                var configuration = providerModeConfiguration
                    ?? throw new InvalidOperationException("Durable provider-mode configuration is not composed.");
                return configuration.WithInitialMode<(ModelTurnResult, ModelProviderPolicy?)>((mode, confirmed) =>
                {
                    var policy = new ModelProviderPolicy(current.Request.SessionId, new(1), mode,
                        ModelProviderSelection.OllamaCandidate, ModelProviderSelection.CopilotCandidate);
                    var result = Finish(ModelTurnOutcome.Succeeded, ModelTurnReason.None);
                    CompleteAudit(requested, result);
                    if (token.IsCancellationRequested || !Live() || !confirmed()
                        || policies.ContainsKey(policy.Session))
                    {
                        result = token.IsCancellationRequested ? Finish(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled)
                            : Finish(ModelTurnOutcome.Denied, ModelTurnReason.PolicyChanged);
                        CompleteAudit(requested, result);
                        activity.Complete(ToActivityOutcome(result.Outcome));
                        return (result, null);
                    }
                    policies[policy.Session] = policy;
                    activity.Complete(HostOperationOutcome.Completed);
                    return (result, policy);
                });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            var result = Finish(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled);
            CompleteAudit(requested, result);
            activity.Complete(HostOperationOutcome.Cancelled);
            return (result, null);
        }
        catch (Exception exception)
        {
            Fault(logger, exception.GetType().Name);
            CompleteAudit(requested, new(ModelTurnOutcome.Unknown, ModelTurnReason.ProviderOutcome));
            activity.Complete(HostOperationOutcome.Unknown);
            throw;
        }
    }

    /// <summary>Starts a policy-bound turn using the session's durably seeded mode and unchanged per-turn semantics.</summary>
    public async Task<ModelTurnAdmission> AdmitAsync(ModelTurnChoice choice, ModelContextEnvelope context,
        ModelHandoffReview? review, CancellationToken token)
    {
        var initialized = await InitializePolicyAsync(token).ConfigureAwait(false);
        return initialized.Policy is { } policy
            ? await AdmitAsync(policy, choice, context, review, token).ConfigureAwait(false)
            : new(initialized.Result);
    }

    /// <summary>Publishes a new volatile policy only after original-user admission and required audit.</summary>
    public async Task<ModelTurnResult> SetPolicyAsync(ModelProviderPolicy policy, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var current = HostActivity.Current;
        if (current is null || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown)
        {
            return Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired);
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "model.policy", SecurityAuditOutcome.Requested, SecurityAuditInitiator.System, current.Request.SessionId.Value.ToString("D"));
        audit.Write(requested);
        ModelTurnResult result;
        try
        {
            var control = access.ControlRevision;
            var session = await workspace.ReadMetadataAsync(current.Request.SessionId, token).ConfigureAwait(false);
            var task = await workspace.ReadTaskAsync(current.Request.SessionId, current.Request.TaskId, token).ConfigureAwait(false);
            lock (policyGate)
            {
                var previous = policies.GetValueOrDefault(current.Request.SessionId);
                var reason = !policy.IsValid || policy.Session != current.Request.SessionId ? ModelTurnReason.InvalidPolicy
                    : current.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                        ? ModelTurnReason.OriginalUserRequired
                    : !Eligible(control) || session.Authority.SessionId != policy.Session || !session.Authority.IsActive
                        || session.Authority.Generation.Value <= 0
                        || !OwnsTask(task, current.Request, session.Authority.Generation) ? ModelTurnReason.HostAdmissionClosed
                    : policy.Revision.Value != (previous?.Revision.Value ?? 0) + 1 ? ModelTurnReason.PolicyChanged
                    : ModelTurnReason.None;
                result = token.IsCancellationRequested
                    ? Finish(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled)
                    : Finish(reason == ModelTurnReason.None ? ModelTurnOutcome.Succeeded : ModelTurnOutcome.Denied, reason);
                CompleteAudit(requested, result);
                if (result.Outcome == ModelTurnOutcome.Succeeded)
                {
                    // Audit callbacks can close authority or re-enter policy publication.
                    if (token.IsCancellationRequested || !Eligible(control)
                        || !ReferenceEquals(previous, policies.GetValueOrDefault(policy.Session)))
                    {
                        result = token.IsCancellationRequested
                            ? Finish(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled)
                            : Finish(ModelTurnOutcome.Denied, ModelTurnReason.PolicyChanged);
                        CompleteAudit(requested, result);
                    }
                    else { policies[policy.Session] = policy; }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            result = Finish(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled);
            CompleteAudit(requested, result);
        }
        catch (Exception exception)
        {
            Fault(logger, exception.GetType().Name);
            CompleteAudit(requested, new(ModelTurnOutcome.Unknown, ModelTurnReason.ProviderOutcome));
            activity.Complete(HostOperationOutcome.Unknown);
            throw;
        }
        activity.Complete(ToActivityOutcome(result.Outcome));
        return result;
    }

    /// <summary>Resolves exactly one provider. A reviewed handoff is still not permission for egress.</summary>
    public async Task<ModelTurnAdmission> AdmitAsync(ModelProviderPolicy policy, ModelTurnChoice choice,
        ModelContextEnvelope context, ModelHandoffReview? review, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var selection = policy.Select(choice);
        var reason = selection is null || policy.Session != context.Request.SessionId ? ModelTurnReason.InvalidPolicy
            : !IsCurrentPolicy(policy) ? ModelTurnReason.PolicyChanged : ModelTurnReason.None;
        if (reason == ModelTurnReason.None && selection!.Provider == ModelProviderIdentity.Copilot
            && policy.Mode == ModelProviderMode.LocalFirst)
        {
            reason = review is null ? ModelTurnReason.HandoffReviewRequired
                : !ReferenceEquals(review.Offer.Policy, policy) || !ReferenceEquals(review.Offer.Context, context)
                    || Interlocked.Exchange(ref review.Used, 1) != 0 ? ModelTurnReason.HandoffReviewStale
                : (await CheckHandoffAsync(review.Offer, token).ConfigureAwait(false)).Reason;
        }
        if (reason != ModelTurnReason.None)
        {
            var current = HostActivity.Current;
            if (current is null || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown)
            {
                return new(Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired));
            }
            using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
            var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
                "model.selection", SecurityAuditOutcome.Requested, SecurityAuditInitiator.System, current.Request.TaskId.Value.ToString("D"));
            audit.Write(requested);
            var result = Finish(ModelTurnOutcome.Denied, reason);
            CompleteAudit(requested, result);
            activity.Complete(HostOperationOutcome.Failed);
            return new(result);
        }
        var admission = await AdmitAsync(selection!, context, token).ConfigureAwait(false);
        if (admission.Turn is { } turn) { turn.Policy = policy; }
        return admission;
    }

    private bool IsCurrentPolicy(ModelProviderPolicy policy)
    {
        lock (policyGate) { return ReferenceEquals(policies.GetValueOrDefault(policy.Session), policy); }
    }

    internal bool CanOfferAfterLocal(ModelTurn turn) =>
        turn.Owner == owner && turn.Policy is { Mode: ModelProviderMode.LocalFirst } policy
        && IsCurrentPolicy(policy) && turn.Provenance.Selection == policy.Local
        && turn.TerminalResult is { Outcome: ModelTurnOutcome.Unavailable };

    internal async Task<(ModelTurnReason Reason, HostRevision Generation, HostRevision TaskRevision, long Control)>
        ObserveHandoffAsync(ModelProviderPolicy policy, ModelContextEnvelope context, CancellationToken token)
    {
        var current = HostActivity.Current;
        var control = access.ControlRevision;
        ModelTurnReason reason;
        if (current is null || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown
            || !ReferenceEquals(current.Request, context.Request)) { reason = ModelTurnReason.HostContextRequired; }
        else if (current.Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)) { reason = ModelTurnReason.OriginalUserRequired; }
        else if (!IsCurrentPolicy(policy)) { reason = ModelTurnReason.PolicyChanged; }
        else if (policy.Mode != ModelProviderMode.LocalFirst) { reason = ModelTurnReason.HandoffNotPermitted; }
        else if (ContextReason(context, current.Request) is var invalid && invalid != ModelTurnReason.None) { reason = invalid; }
        else if (context.Serialize().Length > ModelContextEnvelope.MaximumInputUtf8Bytes) { reason = ModelTurnReason.EnvelopeLimitExceeded; }
        else if (context.Evidence.Any(item => item.Disclosure != ModelEvidenceDisclosure.HostedEligible)) { reason = ModelTurnReason.LocalEvidenceNotDisclosable; }
        else { reason = ModelTurnReason.None; }
        if (reason != ModelTurnReason.None) { return (reason, default, default, control); }
        var session = await workspace.ReadMetadataAsync(context.Request.SessionId, token).ConfigureAwait(false);
        var task = await workspace.ReadTaskAsync(context.Request.SessionId, context.Request.TaskId, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        reason = !Eligible(control) || !IsCurrentPolicy(policy) ? ModelTurnReason.HostAdmissionClosed
            : session.Authority.SessionId != policy.Session || !session.Authority.IsActive
                || session.Authority.Generation.Value <= 0
                || !OwnsTask(task, context.Request, session.Authority.Generation) ? ModelTurnReason.SessionNotAdmitted
            : ContextReason(context, context.Request);
        if (reason == ModelTurnReason.None)
        {
            var registration = registrations.SingleOrDefault(item => item.Selection == policy.Hosted);
            reason = registration is null ? ModelTurnReason.UnregisteredModel
                : !registration.IsQualified(time.GetUtcNow()) ? ModelTurnReason.QualificationPending : ModelTurnReason.None;
        }
        return (reason, session.Authority.Generation, task?.Task.Revision ?? default, control);
    }

    internal async Task<(ModelTurnReason Reason, HostRevision Generation, HostRevision TaskRevision, long Control)>
        CheckHandoffAsync(ModelHandoffOffer offer, CancellationToken token)
    {
        if (!ReferenceEquals(offer.Host, this))
        {
            return (ModelTurnReason.HandoffReviewStale, default, default, 0);
        }
        var observed = await ObserveHandoffAsync(offer.Policy, offer.Context, token).ConfigureAwait(false);
        return observed.Reason == ModelTurnReason.None && (observed.Generation != offer.Generation
            || observed.TaskRevision != offer.TaskRevision || observed.Control != offer.ControlRevision)
            ? (ModelTurnReason.HandoffReviewStale, observed.Generation, observed.TaskRevision, observed.Control) : observed;
    }
}
