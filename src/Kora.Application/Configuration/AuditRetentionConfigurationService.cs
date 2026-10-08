using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class AuditRetentionConfigurationService(
    IAuditRetentionPreferences preferences, AuditRetentionPolicy policy,
    AuditRetentionAdmission admission, ISecurityAuditLog auditLog)
{
    private readonly Lock gate = new();
    private AuditRetentionState state = new(null, null, "unavailable", 0, "Refresh audit retention in the owning unlocked host.");
    private AuditRetentionProposal? proposal;
    private WorkSessionAuthorization? session;
    private RequestOrigin origin;
    private Func<bool>? eligible;
    private bool applying;
    private bool publishing;

    public event EventHandler? Changed;
    public AuditRetentionState Get() { lock (gate) { return state; } }

    public void Observe()
    {
        lock (gate)
        {
            if (applying || publishing) { throw new InvalidOperationException("Audit retention is committing or notifying."); }
            try { ObserveConfirmed(preferences.Load()); }
            catch { HoldUnavailable(); throw; }
        }
    }

    private void ObserveConfirmed(AuditRetentionDays? saved)
    {
        var desired = saved ?? AuditRetentionDays.Default;
        if (state.Saved == saved && HasConfirmedPolicy()) { return; }
        proposal = null;
        policy.Activate(desired);
        state = new(saved, desired, saved is null ? "default" : "saved", checked(state.Revision + 1), null);
        Publish();
    }

    public async Task RefreshAsync(RequestOrigin originalOrigin, Func<bool> remainsEligible, CancellationToken token)
    {
        try
        {
            var observed = await admission.RunAsync(originalOrigin, remainsEligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying || publishing) { throw new InvalidOperationException("Audit retention is committing or notifying."); }
                    var saved = preferences.Load();
                    if (session != authority || origin != request.Origin || eligible is null || !eligible())
                    {
                        proposal = null;
                        state = state with { Revision = checked(state.Revision + 1) };
                    }
                    session = authority;
                    origin = request.Origin;
                    eligible = remainsEligible;
                    return saved;
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (!remainsEligible() || preferences.Load() != observed)
                {
                    throw new InvalidOperationException("Audit-retention discovery admission or saved state changed before its receipt.");
                }
                Observe();
            }
        }
        catch { HoldUnavailable(); throw; }
    }

    public AuditRetentionProposal Propose(string? value, long expectedRevision, long callRevision)
    {
        var parsed = value is null ? (AuditRetentionDays?)null : AuditRetentionDays.Parse(value);
        lock (gate)
        {
            if (applying || publishing || expectedRevision != state.Revision || session is null || eligible is null || !eligible()
                || !HasConfirmedPolicy())
            {
                throw new InvalidOperationException("Refresh audit retention under the current original input and choose again.");
            }
            proposal = new(parsed, expectedRevision, callRevision, session, origin, eligible);
            return proposal;
        }
    }

    public async Task<bool> ApplyAsync(AuditRetentionProposal candidate, RequestOrigin originalOrigin,
        SecurityAuditInitiator initiator, CallCommunicationPolicy callPolicy, Func<bool> remainsEligible, CancellationToken token)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && originalOrigin != RequestOrigin.ActivatedVoice)
        {
            throw new InvalidOperationException("Original activated input provenance is required.");
        }
        try
        {
            var accepted = await admission.RunAsync(originalOrigin, remainsEligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying || publishing || !ReferenceEquals(proposal, candidate) || candidate.Session != authority
                        || candidate.Origin != request.Origin || candidate.Revision != state.Revision
                        || !candidate.Eligible() || !HasConfirmedPolicy()
                        || preferences.Load() != state.Saved)
                    {
                        throw new InvalidOperationException("Audit-retention proposal, policy, saved source or original admission changed.");
                    }
                    applying = true;
                    var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        "configuration.audit-retention", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = callPolicy.CommitVoiceSetting(request.Origin, candidate.CallRevision,
                        () => ReferenceEquals(HostActivity.RequireCurrent().Request, request)
                            && remainsEligible() && candidate.Eligible() && !token.IsCancellationRequested
                            && ReferenceEquals(proposal, candidate) && candidate.Revision == state.Revision && HasConfirmedPolicy(), () =>
                        {
                            try
                            {
                                if (preferences.Load() != state.Saved)
                                {
                                    throw new InvalidDataException("Saved audit-retention source changed during requested audit; refresh before a new proposal.");
                                }
                                preferences.BeginWrite();
                                if (candidate.Value is { } days) { preferences.Save(days); }
                                else { preferences.Reset(); }
                                if (preferences.ReadBack() != candidate.Value)
                                {
                                    throw new InvalidDataException("Audit-retention readback did not match the exact preference.");
                                }
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is InvalidDataException ? "invalid-data" : exception is IOException ? "io-error" : "access-denied"));
                                throw;
                            }
                            // Both this preference receipt and admission's durable intent outcome use the prior audit policy.
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            state = state with { Revision = checked(state.Revision + 1) };
                            proposal = null;
                        });
                    if (denied is { } reason)
                    {
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant()));
                        return false;
                    }
                    return true;
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                if (accepted)
                {
                    token.ThrowIfCancellationRequested();
                    if (!remainsEligible()) { throw new InvalidOperationException("Audit-retention admission changed before its apply receipt."); }
                    if (preferences.ReadBack() != candidate.Value)
                    {
                        throw new InvalidDataException("Saved audit retention changed before its completed apply receipt.");
                    }
                    preferences.ConfirmWrite();
                    try
                    {
                        var confirmed = preferences.Load();
                        if (confirmed != candidate.Value)
                        {
                            throw new InvalidDataException("Confirmed audit-retention readback did not match the exact preference.");
                        }
                        if (!remainsEligible() || !candidate.Eligible() || token.IsCancellationRequested
                            || !HasConfirmedPolicy())
                        {
                            throw new InvalidOperationException("Audit-retention original admission changed before policy activation.");
                        }
                        applying = false;
                        ObserveConfirmed(confirmed);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
                    {
                        preferences.BeginWrite();
                        throw;
                    }
                }
            }
            return accepted;
        }
        catch { HoldUnavailable(); throw; }
        finally { lock (gate) { applying = false; } }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            proposal = null;
            state = state with { Effective = null, Source = "unavailable", Revision = checked(state.Revision + 1),
                Recovery = "Audit preference or admission/evidence is unconfirmed. Required new audit commits and authority mutations are held, not defaulted to 90. Inspect saved state/receipts before explicit repair and refresh; no retry, replay or deletion." };
            policy.HoldUnavailable();
            Publish();
        }
    }

    private void Publish()
    {
        publishing = true;
        try { Changed?.Invoke(this, EventArgs.Empty); }
        finally { publishing = false; }
    }

    private bool HasConfirmedPolicy() =>
        state.Effective is { } expected && policy.Effective is { } current && expected == current;
}
