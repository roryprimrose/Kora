using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class LogonStartupConfigurationService(ILogonStartupPreferences preferences, ILogonStartupRegistration registration,
    LogonStartupAdmission admission, ISecurityAuditLog auditLog, TimeProvider time)
{
    private readonly Lock gate = new();
    private LogonStartupState state = new(null, null, false, "Inspect startup from the owning unlocked host.");
    private LogonStartupReview? review;
    private bool busy;

    public LogonStartupState Get() { lock (gate) { return state; } }
    public void Cancel() { lock (gate) { review = null; } }

    public async Task<LogonStartupState> InspectAsync(RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        Enter();
        try
        {
            var observed = await admission.RunAsync(origin, eligible, (_, _) =>
            {
                var saved = preferences.Load();
                var effective = registration.Inspect();
                return new LogonStartupState(saved, effective.Enabled, effective.Available, effective.UnavailableReason);
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (!eligible()) { throw new InvalidOperationException("Startup inspection admission changed before presentation."); }
                state = observed;
                return state;
            }
        }
        catch { Hold(); throw; }
        finally { Exit(); }
    }

    public async Task<LogonStartupReview> ReviewAsync(bool? value, RequestOrigin origin, long callRevision,
        CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        Enter();
        try
        {
            var candidate = await admission.RunAsync(origin, eligible, (request, session) =>
            {
                var saved = preferences.Load();
                var effective = registration.Inspect();
                if (!effective.Available || policy.CheckMutation(request.Origin, callRevision, eligible) is not null)
                { throw new InvalidOperationException(effective.UnavailableReason ?? "Startup review is denied by the current call/channel policy."); }
                return new LogonStartupReview(value, effective, saved, session, request.Origin, eligible,
                    callRevision, time.GetUtcNow().AddSeconds(60));
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (!eligible() || !candidate.Eligible()) { throw new InvalidOperationException("Startup review admission changed."); }
                state = new(candidate.Saved, candidate.Registration.Enabled, true, null);
                review = candidate;
                return candidate;
            }
        }
        catch { Hold(); throw; }
        finally { Exit(); }
    }

    public async Task<bool> ConfirmAsync(LogonStartupReview candidate, RequestOrigin origin, SecurityAuditInitiator initiator,
        CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        { throw new InvalidOperationException("Startup confirmation requires original user input."); }
        lock (gate)
        {
            if (busy || !ReferenceEquals(review, candidate)) { throw new InvalidOperationException("Review this exact startup change first."); }
            busy = true;
            review = null;
        }
        var writing = false;
        try
        {
            bool Current() => eligible() && candidate.Eligible() && time.GetUtcNow() < candidate.Expires
                && !token.IsCancellationRequested;
            var accepted = await admission.RunAsync(origin, Current, (request, session) =>
            {
                if (session != candidate.Session || request.Origin != candidate.Origin || preferences.Load() != candidate.Saved
                    || registration.Inspect() != candidate.Registration)
                { throw new InvalidOperationException("Startup review, install identity, registration, saved intent or original channel changed."); }
                var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    "configuration.start-at-logon", SecurityAuditOutcome.Requested, initiator, "preferences.device-local");
                auditLog.Write(audit);
                var denied = policy.CommitVoiceSetting(request.Origin, candidate.CallRevision,
                    () => Current() && ReferenceEquals(HostActivity.RequireCurrent().Request, request), () =>
                    {
                        if (preferences.Load() != candidate.Saved)
                        { throw new InvalidDataException("Startup intent changed during requested audit."); }
                        preferences.BeginWrite();
                        writing = true;
                        try
                        {
                            registration.Apply(candidate.Value == true, candidate.Registration, Current);
                            preferences.Save(candidate.Value);
                            RequireReadback(candidate);
                        }
                        catch
                        {
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "startup-write-unconfirmed"));
                            throw;
                        }
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                    });
                if (denied is not null)
                { auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, "call-channel-denied")); return false; }
                return true;
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                if (accepted)
                {
                    if (!Current()) { throw new InvalidOperationException("Startup admission changed before its completed receipt."); }
                    RequireReadback(candidate);
                    preferences.ConfirmWrite();
                    try
                    {
                        if (preferences.Load() != candidate.Value) { throw new InvalidDataException("Confirmed startup intent does not match."); }
                        RequireReadback(candidate);
                        if (!Current()) { throw new InvalidOperationException("Startup admission changed during preference confirmation."); }
                    }
                    catch { preferences.BeginWrite(); throw; }
                    state = new(candidate.Value, candidate.Value == true, true, null);
                }
                return accepted;
            }
        }
        catch
        {
            Hold();
            if (writing) { preferences.BeginWrite(); }
            throw;
        }
        finally { Exit(); }
    }

    private void RequireReadback(LogonStartupReview candidate)
    {
        var effective = registration.Inspect();
        if (preferences.ReadBack() != candidate.Value || !effective.Available
            || !string.Equals(effective.Identity, candidate.Registration.Identity, StringComparison.Ordinal) || effective.Enabled != (candidate.Value == true))
        { throw new InvalidDataException("Startup registration or saved intent readback does not match. No automatic retry or rollback."); }
    }
    private void Enter()
    {
        lock (gate)
        {
            if (busy) { throw new InvalidOperationException("Startup control is already active."); }
            busy = true;
            review = null;
        }
    }
    private void Exit() { lock (gate) { busy = false; } }
    private void Hold()
    {
        lock (gate)
        {
            review = null;
            state = state with { Available = false, Effective = null,
                Error = "Startup outcome unconfirmed. Inspect Windows startup registration, saved intent and audit; a write may remain. No automatic retry or rollback." };
        }
    }
}
