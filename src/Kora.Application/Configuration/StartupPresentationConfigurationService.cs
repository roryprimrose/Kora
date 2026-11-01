using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class StartupPresentationConfigurationService(IStartupPresentationPreferences preferences,
    LogonStartupAdmission admission, ISecurityAuditLog auditLog, TimeProvider time)
{
    private readonly Lock gate = new();
    private StartupPresentationReview? review;
    private bool busy;
    private long controlRevision;
    private long cancellationRevision;
    private string status = "Manual startup presentation has not been inspected. No saved intent is assumed.";

    public string Status { get { lock (gate) { return status; } } }
    public void Cancel() { lock (gate) { review = null; controlRevision++; Interlocked.Increment(ref cancellationRevision); } }

    public async Task InspectAsync(Func<bool> eligible, CancellationToken token)
    {
        eligible = CaptureCancellation(eligible);
        Enter();
        try
        {
            var saved = await admission.RunAsync(RequestOrigin.LocalUi, eligible, (request, _) =>
            {
                RequireNative(request.Origin);
                return preferences.Load();
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                RequireCurrent(eligible, token);
                status = ConfirmedStatus(saved);
            }
        }
        catch { Hold(); throw; }
        finally { Exit(); }
    }

    public async Task<StartupPresentationReview> ReviewAsync(StartupPresentation? value, long callRevision,
        CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        if (value is { } selected && !Enum.IsDefined(selected)) { throw new ArgumentOutOfRangeException(nameof(value)); }
        eligible = CaptureCancellation(eligible);
        Enter();
        try
        {
            var candidate = await admission.RunAsync(RequestOrigin.LocalUi, eligible, (request, session) =>
            {
                RequireNative(request.Origin);
                if (policy.CheckMutation(request.Origin, callRevision, eligible) is not null)
                { throw new InvalidOperationException("Current call/channel policy denies startup presentation review."); }
                return new StartupPresentationReview(value, preferences.Load(), session, eligible, callRevision, time.GetUtcNow().AddSeconds(60));
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                RequireCurrent(eligible, token);
                status = ConfirmedStatus(candidate.Saved);
                review = candidate;
                return candidate;
            }
        }
        catch { Hold(); throw; }
        finally { Exit(); }
    }

    public async Task ConfirmAsync(StartupPresentationReview candidate, CallCommunicationPolicy policy,
        Func<bool> eligible, CancellationToken token)
    {
        lock (gate)
        {
            if (busy || !ReferenceEquals(review, candidate)) { throw new InvalidOperationException("Review this exact startup presentation change first."); }
            busy = true;
            controlRevision++;
            review = null;
        }
        var writing = false;
        try
        {
            bool Current() => eligible() && candidate.Eligible() && time.GetUtcNow() < candidate.Expires && !token.IsCancellationRequested;
            await admission.RunAsync(RequestOrigin.LocalUi, Current, (request, session) =>
            {
                RequireNative(request.Origin);
                if (session != candidate.Session || preferences.Load() != candidate.Saved)
                { throw new InvalidOperationException("Startup presentation review, saved intent or control session changed."); }
                var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    "configuration.manual-startup-presentation", SecurityAuditOutcome.Requested,
                    SecurityAuditInitiator.LocalUser, "preferences.device-local");
                auditLog.Write(audit);
                var denied = policy.CommitVoiceSetting(request.Origin, candidate.CallRevision,
                    () => Current() && ReferenceEquals(HostActivity.RequireCurrent().Request, request), () =>
                {
                    try
                    {
                        if (preferences.Load() != candidate.Saved)
                        { throw new InvalidDataException("Startup presentation intent changed during requested audit."); }
                        preferences.BeginWrite();
                        writing = true;
                        preferences.Save(candidate.Value);
                        RequireReadback(candidate);
                    }
                    catch
                    {
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "startup-presentation-write-unconfirmed"));
                        throw;
                    }
                    auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                });
                if (denied is not null)
                {
                    auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, "call-channel-denied"));
                    throw new InvalidOperationException("Current call/channel policy denied startup presentation confirmation.");
                }
                return true;
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                RequireCurrent(Current, token);
                RequireReadback(candidate);
                preferences.ConfirmWrite();
                if (preferences.Load() != candidate.Value)
                { throw new InvalidDataException("Confirmed startup presentation intent does not match."); }
                RequireCurrent(Current, token);
                status = ConfirmedStatus(candidate.Value);
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

    public Task<StartupPresentation?> ReadStartupAsync(CancellationToken token) =>
        Task.Run(preferences.Load, token);

    private Func<bool> CaptureCancellation(Func<bool> eligible)
    {
        var captured = Interlocked.Read(ref cancellationRevision);
        return () => eligible() && captured == Interlocked.Read(ref cancellationRevision);
    }

    public Func<bool> CaptureStartupCurrent()
    {
        lock (gate)
        {
            var captured = controlRevision;
            var admitted = !busy;
            return () => { lock (gate) { return admitted && !busy && captured == controlRevision; } };
        }
    }

    private static void RequireNative(RequestOrigin origin)
    {
        if (origin != RequestOrigin.LocalUi)
        { throw new InvalidOperationException("Startup presentation requires the original native Settings review and confirmation."); }
    }
    private void RequireReadback(StartupPresentationReview candidate)
    {
        if (preferences.ReadBack() != candidate.Value)
        { throw new InvalidDataException("Startup presentation readback is unconfirmed. No automatic retry or rollback."); }
    }
    private static void RequireCurrent(Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("Startup presentation admission changed before its receipt or presentation."); }
    }
    private void Enter()
    {
        lock (gate)
        {
            if (busy) { throw new InvalidOperationException("Startup presentation control is already active."); }
            busy = true;
            controlRevision++;
        }
    }
    private void Exit() { lock (gate) { busy = false; } }
    private void Hold() { lock (gate) { review = null; status = "Manual startup presentation unavailable or unconfirmed. Inspect saved intent and audit before manual recovery; no automatic retry."; } }
    private static string ConfirmedStatus(StartupPresentation? value) =>
        $"Last confirmed manual startup intent: {StartupPresentationReview.Describe(value)}. Applies only to the supported manual shortcut/--manual-startup launch, under fresh host gates; not a startup readiness claim.";
}
