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

/// <summary>
/// Automatic hosted final-request admission. Choosing a hosted service in Settings is the disclosure decision;
/// each physical send still revalidates exact authorization, account/profile and host authority and is audited.
/// </summary>
public sealed class HostedTransmissionReviewService(ISecurityAuditLog audit, TimeProvider time,
    ISessionPermissionStore? permissions = null)
{
    internal const string AutomaticReason = "hosted-provider-settings";

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
        ReferenceEquals(authorization.Owner, this) && IssuerLive(authorization.Issuer, authorization.Context)
        && authorization.ContextCurrent(time);

    internal async ValueTask<bool> PermissionCurrentAsync(HostedTurnAuthorization authorization, CancellationToken token) =>
        authorization.AutomaticPermission is not { } expected
            || await expected.Store.ReadSessionPermissionAsync(authorization.Context.Request.SessionId, token).ConfigureAwait(false) == expected.Policy;

    private bool Live(HostActivity issuer, ModelContextEnvelope context) =>
        IssuerLive(issuer, context) && context.CreatedAt <= time.GetUtcNow() && context.ExpiresAt > time.GetUtcNow();

    private static bool IssuerLive(HostActivity issuer, ModelContextEnvelope context) =>
        !issuer.Activity!.IsStopped && issuer.Outcome == HostOperationOutcome.Unknown
        && ReferenceEquals(issuer.Request, context.Request)
        && issuer.Request.Origin is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice;

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
        var outcome = SecurityAuditOutcome.Denied;
        string? reason = null;
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
                }
            }
            var admitted = Current(authorization) && await current(token).ConfigureAwait(false)
                && await PermissionCurrentAsync(authorization, token).ConfigureAwait(false)
                && Current(authorization) && await current(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            outcome = admitted ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Denied;
            reason = admitted ? AutomaticReason : null;
            return admitted;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { outcome = SecurityAuditOutcome.Cancelled; throw; }
        catch
        { outcome = SecurityAuditOutcome.Unknown; throw; }
        finally
        {
            audit.Write(requested.WithOutcome(outcome, reason));
            activity.Complete(outcome == SecurityAuditOutcome.Succeeded ? HostOperationOutcome.Completed
                : outcome == SecurityAuditOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
