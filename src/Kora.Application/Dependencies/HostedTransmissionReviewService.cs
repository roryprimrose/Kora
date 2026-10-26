using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.Dependencies;

/// <summary>Explicit final-byte review, independent of saved profiles and earlier envelope/handoff reviews.</summary>
public sealed class HostedTransmissionReviewService(ISecurityAuditLog audit, TimeProvider time)
{
    private readonly Lock gate = new();
    private CopilotTransmissionReview? pending;
    public event Action<CopilotTransmissionReview>? Offered;
    public event Action<CopilotTransmissionReview>? Retired;
    public event Action<HostedTransmissionReview>? ClaudeOffered;
    public event Action<HostedTransmissionReview>? ClaudeRetired;

    public CopilotTurnAuthorization PrepareTurn(ModelContextEnvelope context, ProviderProfileTuple profile)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        var current = ValidatePreparation(context, profile, ProviderService.Copilot);
        return new(this, current, context, profile);
    }

    public ClaudeTurnAuthorization PrepareClaudeTurn(ModelContextEnvelope context, ProviderProfileTuple profile)
    {
        var current = ValidatePreparation(context, profile, ProviderService.Claude);
        if (!ClaudeObservationTools.Supports(context))
        { throw new InvalidOperationException("Claude requires an explicit tool-free request or bounded original observation subset, without evidence."); }
        return new(this, current, context, profile);
    }

    private HostActivity ValidatePreparation(ModelContextEnvelope context, ProviderProfileTuple profile, ProviderService service)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        var current = HostActivity.RequireCurrent();
        if (!Live(current, context) || profile.Service != service || profile.CatalogueRevision.Value <= 0
            || context.Evidence.Any(item => item.Disclosure != ModelEvidenceDisclosure.HostedEligible))
        {
            throw new InvalidOperationException("An original-user request with an exact Copilot profile and hosted-eligible context is required.");
        }
        context.RequireCompleteInputLimit();
        return current;
    }

    internal bool Current(HostedTurnAuthorization authorization) =>
        ReferenceEquals(authorization.Owner, this) && Live(authorization.Issuer, authorization.Context);

    private bool Live(HostActivity issuer, ModelContextEnvelope context) =>
        !issuer.Activity!.IsStopped && issuer.Outcome == HostOperationOutcome.Unknown
        && ReferenceEquals(issuer.Request, context.Request)
        && issuer.Request.Origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
        && context.CreatedAt <= time.GetUtcNow() && context.ExpiresAt > time.GetUtcNow();

    internal ValueTask<bool> ReviewAsync(ClaudeTurnAuthorization authorization, ClaudeTransmission transmission,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token) =>
        ReviewAsync(authorization, new CopilotTransmission(transmission.Provenance, transmission.Profile,
            transmission.Destination, transmission.Body.ToArray(), transmission.Headers), current, token);

    internal async ValueTask<bool> ReviewAsync(HostedTurnAuthorization authorization, CopilotTransmission transmission,
        Func<CancellationToken, ValueTask<bool>> current, CancellationToken token)
    {
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.SecurityApproval,
            authorization.Profile.Service == ProviderService.Claude ? "model.claude.transmission" : "model.copilot.transmission",
            SecurityAuditOutcome.Requested, SecurityAuditInitiator.System,
            transmission.Provenance.Turn.Value.ToString("D"));
        audit.Write(requested);
        CopilotTransmissionReview? review = null;
        HostedTransmissionReview? claudeReview = null;
        var outcome = SecurityAuditOutcome.Denied;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!Current(authorization) || authorization.Profile != transmission.Profile
                || !ReferenceEquals(authorization.Context.Request, transmission.Provenance.Request)
                || !await current(token).ConfigureAwait(false))
            { return false; }
            var claude = authorization is ClaudeTurnAuthorization;
            var claudeOffered = ClaudeOffered;
            var copilotOffered = Offered;
            if (claude ? claudeOffered is null : copilotOffered is null) { return false; }
            review = new(transmission, HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation));
            lock (gate)
            {
                if (pending is not null) { return false; }
                pending = review;
            }
            if (claude) { claudeReview = new(review); claudeOffered!(claudeReview); }
            else { copilotOffered!(review); }
            var approved = await review.Completion.Task.WaitAsync(token).ConfigureAwait(false);
            approved = approved && Current(authorization) && await current(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            outcome = approved ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Denied;
            return approved;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { outcome = SecurityAuditOutcome.Cancelled; throw; }
        catch
        { outcome = SecurityAuditOutcome.Unknown; throw; }
        finally
        {
            lock (gate)
            {
                if (ReferenceEquals(pending, review)) { pending = null; }
                review?.Completion.TrySetResult(false);
            }
            audit.Write(requested.WithOutcome(outcome));
            activity.Complete(outcome == SecurityAuditOutcome.Succeeded ? HostOperationOutcome.Completed
                : outcome == SecurityAuditOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            if (claudeReview is not null) { ClaudeRetired?.Invoke(claudeReview); }
            else if (review is not null) { Retired?.Invoke(review); }
        }
    }

    public bool Confirm(CopilotTransmissionReview review, bool approve, RequestOrigin channel)
    {
        ArgumentNullException.ThrowIfNull(review);
        var current = HostActivity.Current;
        lock (gate)
        {
            if (!ReferenceEquals(pending, review) || current is null || current.Activity!.IsStopped
                || current.Outcome != HostOperationOutcome.Unknown
                || !ReferenceEquals(current.Request, review.Transmission.Provenance.Request)
                || (review.Transmission.Profile.Service == ProviderService.Claude
                    ? channel != RequestOrigin.LocalUi
                    : channel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)))
            { return false; }
            return review.Completion.TrySetResult(approve);
        }

    }

    public bool Confirm(HostedTransmissionReview review, bool approve, RequestOrigin channel)
    {
        ArgumentNullException.ThrowIfNull(review);
        return Confirm(review.Review, approve, channel);
    }
}
