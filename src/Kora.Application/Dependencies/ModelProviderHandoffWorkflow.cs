using Kora.Application.Interaction;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

/// <summary>Bounded original-user workflow. Confirmation never grants runtime, tool, or egress authority.</summary>
public sealed partial class ModelProviderHandoffWorkflow(
    ModelTurnHost host, HostQuestionService questions, ISecurityAuditLog audit,
    ILogger<ModelProviderHandoffWorkflow> logger, HostQuestionReviewService reviews)
{
    /// <summary>Offers, never dispatches, after an audited host-issued local unavailable outcome.</summary>
    public Task<ModelHandoffResult> OfferAfterLocalAsync(ModelTurn turn, ModelContextEnvelope context, CancellationToken token) =>
        ExecuteAsync(() => host.CanOfferAfterLocal(turn)
            ? OfferCoreAsync(turn.Policy!, context, ModelHandoffReason.LocalUnavailable, token)
            : Task.FromResult(new ModelHandoffResult(ModelHandoffOutcome.Denied, ModelTurnReason.HandoffNotPermitted)), token);

    public Task<ModelHandoffResult> OfferAsync(ModelProviderPolicy policy, ModelContextEnvelope context,
        ModelHandoffReason reason, CancellationToken token) =>
        ExecuteAsync(() => OfferCoreAsync(policy, context, reason, token), token);

    private async Task<ModelHandoffResult> OfferCoreAsync(ModelProviderPolicy policy, ModelContextEnvelope context,
        ModelHandoffReason reason, CancellationToken token)
    {
        if (!Enum.IsDefined(reason) || reason == ModelHandoffReason.Unknown)
        {
            return new(ModelHandoffOutcome.Denied, ModelTurnReason.InvalidHandoffReason);
        }
        var observed = await host.ObserveHandoffAsync(policy, context, token).ConfigureAwait(false);
        if (observed.Reason != ModelTurnReason.None) { return Denied(observed.Reason); }
        var decision = await questions.CreateAsync(context.Request,
            new("Review the exact context envelope and Copilot destination before confirming this new hosted turn. "
                + "Confirmation does not qualify the runtime or permit network transmission.",
                QuestionKind.SingleChoice, [new("approve", "Approve exact envelope"), new("decline", "Decline")],
                purpose: "provider-handoff", sourceId: "host"), context.ExpiresAt, token).ConfigureAwait(false);
        if (decision.Outcome != HostInteractionOutcome.Presented) { return FromQuestion(decision); }
        var offer = new ModelHandoffOffer(host, policy, context, reason, decision.Question!,
            observed.Generation, observed.TaskRevision, observed.Control);
        var after = await host.CheckHandoffAsync(offer, token).ConfigureAwait(false);
        return after.Reason == ModelTurnReason.None
            ? new(ModelHandoffOutcome.Offered, ModelTurnReason.None, offer) : Denied(after.Reason);
    }

    public Task<ModelHandoffResult> ReviewAsync(ModelHandoffOffer offer, ModelProviderPolicy policy,
        ModelContextEnvelope context, ModelHandoffDecision decision, RequestOrigin channel, CancellationToken token) =>
        ExecuteAsync(() => ReviewCoreAsync(offer, policy, context, decision, channel, token), token);

    private async Task<ModelHandoffResult> ReviewCoreAsync(ModelHandoffOffer offer, ModelProviderPolicy policy,
        ModelContextEnvelope context, ModelHandoffDecision decision, RequestOrigin channel, CancellationToken token)
    {
        if (!ReferenceEquals(offer.Policy, policy) || !ReferenceEquals(offer.Context, context))
        {
            return new(ModelHandoffOutcome.Stale, ModelTurnReason.HandoffReviewStale);
        }
        if (channel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !Enum.IsDefined(decision) || decision == ModelHandoffDecision.Unknown)
        {
            return new(ModelHandoffOutcome.Denied, ModelTurnReason.OriginalUserRequired);
        }
        var observed = await host.CheckHandoffAsync(offer, token).ConfigureAwait(false);
        if (observed.Reason != ModelTurnReason.None) { return Denied(observed.Reason); }
        if (Interlocked.Exchange(ref offer.Used, 1) != 0)
        {
            return new(ModelHandoffOutcome.Stale, ModelTurnReason.HandoffReviewStale);
        }
        var answered = decision == ModelHandoffDecision.Cancel
            ? await questions.CancelAsync(offer.Question.Key, token).ConfigureAwait(false)
            : await questions.SubmitAsync(offer.Question.Key,
                new([decision == ModelHandoffDecision.Approve ? "approve" : "decline"]), channel, token).ConfigureAwait(false);
        if (answered.Outcome is not (HostInteractionOutcome.Answered or HostInteractionOutcome.Cancelled))
        {
            return FromQuestion(answered);
        }
        observed = await host.CheckHandoffAsync(offer, token).ConfigureAwait(false);
        if (observed.Reason != ModelTurnReason.None) { return Denied(observed.Reason); }
        return decision switch
        {
            ModelHandoffDecision.Approve => new(ModelHandoffOutcome.Approved, ModelTurnReason.None, offer, new(offer)),
            ModelHandoffDecision.Decline => new(ModelHandoffOutcome.Declined, ModelTurnReason.None),
            _ => new(ModelHandoffOutcome.Cancelled, ModelTurnReason.CallerCancelled),
        };
    }

    /// <summary>Removal retires the old offer and creates a new exact envelope requiring fresh review.</summary>
    public Task<ModelHandoffResult> RemoveAsync(ModelHandoffOffer offer,
        IReadOnlyCollection<HostId<EvidenceIdentity>> remove, CancellationToken token) =>
        ExecuteAsync(async () =>
        {
            if (remove.Count == 0 || remove.Distinct().Count() != remove.Count
                || remove.Any(id => !offer.Context.Evidence.Any(item => item.Id == id)))
            {
                return new(ModelHandoffOutcome.Denied, ModelTurnReason.ContextMismatch);
            }
            var observed = await host.CheckHandoffAsync(offer, token).ConfigureAwait(false);
            if (observed.Reason != ModelTurnReason.None) { return Denied(observed.Reason); }
            if (Interlocked.Exchange(ref offer.Used, 1) != 0)
            {
                return new(ModelHandoffOutcome.Stale, ModelTurnReason.HandoffReviewStale);
            }
            var cancelled = await questions.CancelAsync(offer.Question.Key, token).ConfigureAwait(false);
            if (cancelled.Outcome != HostInteractionOutcome.Cancelled) { return FromQuestion(cancelled); }
            var original = offer.Context;
            var reduced = new ModelContextEnvelope(original.Request, original.SystemPolicy, original.UserRequest,
                original.Evidence.Where(item => !remove.Contains(item.Id)), original.CreatedAt, original.ExpiresAt);
            var replacement = await OfferCoreAsync(offer.Policy, reduced, offer.Reason, token).ConfigureAwait(false);
            return replacement.Outcome == ModelHandoffOutcome.Offered
                ? replacement with { Outcome = ModelHandoffOutcome.Removed } : replacement;
        }, token);

    private async Task<ModelHandoffResult> ExecuteAsync(Func<Task<ModelHandoffResult>> operation, CancellationToken token)
    {
        var current = HostActivity.Current;
        if (current is null || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown)
        {
            return new(ModelHandoffOutcome.Denied, ModelTurnReason.HostContextRequired);
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            "model.handoff", SecurityAuditOutcome.Requested, SecurityAuditInitiator.System,
            current.Request.TaskId.Value.ToString("D"));
        ModelHandoffOffer? produced = null;
        try
        {
            audit.Write(requested);
            token.ThrowIfCancellationRequested();
            var result = await operation().ConfigureAwait(false);
            produced = result.Offer;
            token.ThrowIfCancellationRequested();
            // No review capability is returned before its terminal audit succeeds.
            audit.Write(requested.WithOutcome(result.Outcome switch
            {
                ModelHandoffOutcome.Offered or ModelHandoffOutcome.Approved or ModelHandoffOutcome.Removed => SecurityAuditOutcome.Succeeded,
                ModelHandoffOutcome.Cancelled => SecurityAuditOutcome.Cancelled,
                _ => SecurityAuditOutcome.Denied,
            }));
            token.ThrowIfCancellationRequested();
            if (result.Offer is { } offer)
            {
                var final = await host.CheckHandoffAsync(offer, token).ConfigureAwait(false);
                if (final.Reason != ModelTurnReason.None)
                {
                    result = Denied(final.Reason);
                    audit.Write(requested.WithOutcome(SecurityAuditOutcome.Denied));
                }
            }
            await Publish(result, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Result(logger, result.Outcome, result.Reason);
            activity.Complete(result.Outcome is ModelHandoffOutcome.Offered or ModelHandoffOutcome.Approved or ModelHandoffOutcome.Removed
                ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (produced is not null) { Retire(produced); }
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Cancelled));
            activity.Complete(HostOperationOutcome.Cancelled);
            return new(ModelHandoffOutcome.Cancelled, ModelTurnReason.CallerCancelled);
        }
        catch (Exception exception)
        {
            if (produced is not null) { Retire(produced); }
            Fault(logger, exception.GetType().Name);
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Unknown));
            activity.Complete(HostOperationOutcome.Unknown);
            throw;
        }
    }

    private static ModelHandoffResult Denied(ModelTurnReason reason) => new(reason switch
    {
        ModelTurnReason.ContextExpired => ModelHandoffOutcome.Expired,
        ModelTurnReason.HandoffReviewStale or ModelTurnReason.PolicyChanged => ModelHandoffOutcome.Stale,
        ModelTurnReason.QualificationPending or ModelTurnReason.UnregisteredModel => ModelHandoffOutcome.Unavailable,
        _ => ModelHandoffOutcome.Denied,
    }, reason);

    private static ModelHandoffResult FromQuestion(HostInteractionDecision decision) => new(
        decision.Outcome == HostInteractionOutcome.Expired ? ModelHandoffOutcome.Expired : ModelHandoffOutcome.Stale,
        decision.Outcome == HostInteractionOutcome.Expired ? ModelTurnReason.ContextExpired : ModelTurnReason.HandoffReviewStale);
}
