using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Providers;
using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kora.Application.Dependencies;

/// <summary>Explicit final-byte review, independent of saved profiles and earlier envelope/handoff reviews.</summary>
public sealed class HostedTransmissionReviewService(ISecurityAuditLog audit, TimeProvider time,
    ISessionPermissionStore? permissions = null)
{
    private readonly Lock gate = new();
    private CopilotTransmissionReview? pending;
    public event Action<CopilotTransmissionReview>? Offered;
    public event Action<CopilotTransmissionReview>? Retired;
    public event Action<HostedTransmissionReview>? ClaudeOffered;
    public event Action<HostedTransmissionReview>? ClaudeRetired;

    public CopilotTurnAuthorization PrepareTurn(ModelContextEnvelope context, ProviderProfileTuple profile,
        CopilotCustomizationSelection? selection = null,
        Func<CancellationToken, ValueTask<bool>>? sourceCurrent = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        var current = ValidatePreparation(context, profile, ProviderService.Copilot);
        if (selection is not null && (sourceCurrent is null || context.ToolMode != ModelToolMode.None))
        { throw new InvalidOperationException("Selected native Copilot intent requires exact source validation and no tools."); }
        return new(this, current, context, profile, selection, sourceCurrent);
    }

    public ClaudeTurnAuthorization PrepareClaudeTurn(ModelContextEnvelope context, ProviderProfileTuple profile,
        bool streamAnswer = false, SelectedHostedDefinition? selection = null,
        Func<CancellationToken, ValueTask<bool>>? sourceCurrent = null)
    {
        var current = ValidatePreparation(context, profile, ProviderService.Claude);
        if (!ClaudeObservationTools.Supports(context) || streamAnswer && context.ToolMode != ModelToolMode.None)
        { throw new InvalidOperationException("Claude requires an explicit text, observation or dedicated clarification operation, without evidence; streaming is text-only."); }
        if (selection is not null && (sourceCurrent is null || streamAnswer || context.ToolMode != ModelToolMode.None))
        { throw new InvalidOperationException("Selected Claude USER copy requires exact source validation and no streaming, tools or evidence."); }
        return new(this, current, context, profile, streamAnswer, selection, sourceCurrent);
    }

    private HostActivity ValidatePreparation(ModelContextEnvelope context, ProviderProfileTuple profile, ProviderService service)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        var current = HostActivity.RequireCurrent();
        if (!Live(current, context) || profile.Service != service || profile.CatalogueRevision.Value <= 0
            || context.ToolMode == ModelToolMode.HostedClarification && service != ProviderService.Claude
            || context.Evidence.Any(item => item.Disclosure != ModelEvidenceDisclosure.HostedEligible))
        {
            throw new InvalidOperationException("An original-user request with an exact Copilot profile and hosted-eligible context is required.");
        }
        context.RequireCompleteInputLimit();
        return current;
    }

    internal bool Current(HostedTurnAuthorization authorization) =>
        ReferenceEquals(authorization.Owner, this) && Live(authorization.Issuer, authorization.Context);

    internal async ValueTask<bool> PermissionCurrentAsync(HostedTurnAuthorization authorization, CancellationToken token) =>
        authorization.AutomaticPermission is not { } expected
            || await expected.Store.ReadSessionPermissionAsync(authorization.Context.Request.SessionId, token).ConfigureAwait(false) == expected.Policy;

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
            if (permissions is not null)
            {
                var policy = await permissions.ReadSessionPermissionAsync(
                    authorization.Context.Request.SessionId, token).ConfigureAwait(false);
                if (!Current(authorization) || !await current(token).ConfigureAwait(false)) { return false; }
                if (policy.Level == SessionPermissionLevel.ApproveAll)
                {
                    var bodyDigest = Convert.ToHexStringLower(SHA256.HashData(transmission.Body.AsSpan()));
                    var details = new AutomaticApprovalAudit(authorization.Context.Request, policy,
                        requested.ActionId, HostOperationEffect.BoundedRead, "hosted-final-request-disclosure", bodyDigest,
                        destinationDigest: Hash(transmission.Destination.AbsoluteUri),
                        accountDigest: Hash(JsonSerializer.Serialize(transmission.Profile.Account)),
                        profileDigest: Hash(JsonSerializer.Serialize(transmission.Profile)),
                        requestBodyDigest: bodyDigest);
                    await permissions.RecordAutomaticApprovalAsync(authorization.Context.Request, policy, details,
                        () => Current(authorization) && !token.IsCancellationRequested, token).ConfigureAwait(false);
                    authorization.AutomaticPermission = (permissions, policy);
                    var automatic = Current(authorization) && await current(token).ConfigureAwait(false)
                        && await PermissionCurrentAsync(authorization, token).ConfigureAwait(false)
                        && Current(authorization) && await current(token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    outcome = automatic ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Denied;
                    return automatic;
                }
            }
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

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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
