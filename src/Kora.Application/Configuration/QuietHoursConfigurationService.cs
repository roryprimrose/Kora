using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class QuietHoursConfigurationService
{
    private readonly Lock gate = new();
    private readonly Lock clockGate = new();
    private readonly IQuietHoursPreferences preferences;
    private readonly AudioControlAdmission admission;
    private readonly ISecurityAuditLog audit;
    private readonly TimeProvider time;
    private readonly ILogger<QuietHoursConfigurationService> logger;
    private QuietHoursState state = new(null, null, 0, false, "Inspect quiet hours from the owning unlocked host.");
    private QuietHoursReview? review;
    private DateTimeOffset highWatermark;
    private bool busy;

    public QuietHoursConfigurationService(IQuietHoursPreferences preferences, AudioControlAdmission admission,
        ISecurityAuditLog audit, TimeProvider time, ILogger<QuietHoursConfigurationService> logger)
    {
        this.preferences = preferences;
        this.admission = admission;
        this.audit = audit;
        this.time = time;
        this.logger = logger;
        try
        {
            var saved = preferences.Load();
            state = new(saved, saved ?? QuietHoursSchedule.Off, 0, true, null);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            PreferenceUnavailable(exception.GetType().Name);
            state = state with { Recovery = "Saved quiet hours are invalid, unreadable or unconfirmed. Proactive speech is held; inspect saved intent and audit receipts before explicit repair." };
        }
    }

    internal event Action? SpeechAdmissionChanged;
    public QuietHoursState Get() { lock (gate) { return state; } }
    internal long SpeechRevision => Volatile.Read(ref state).Revision;
    public void Cancel() { lock (gate) { review = null; } }

    internal bool AllowsSpeech(long revision)
    {
        DateTimeOffset now;
        lock (clockGate)
        {
            now = time.GetUtcNow();
            if (now < highWatermark) { return false; }
            highWatermark = now;
        }
        var current = Volatile.Read(ref state);
        return !Volatile.Read(ref busy) && current.Revision == revision
            && current.Effective is { } schedule && !schedule.IsQuiet(now);
    }

    internal void StartSpeech(long revision, Action start)
    {
        lock (gate)
        {
            if (!AllowsSpeech(revision))
            { throw new OperationCanceledException("Scheduled proactive speech admission changed or is quiet/unavailable."); }
            start();
        }
    }

    public async Task InspectAsync(RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        Enter();
        try
        {
            var saved = await admission.RunAsync(origin, eligible, (_, _) => preferences.Load(), token).ConfigureAwait(false);
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (!eligible() || preferences.Load() != saved)
                { throw new InvalidOperationException("Quiet-hours inspection or saved state changed before its receipt."); }
                state = new(saved, saved ?? QuietHoursSchedule.Off, checked(state.Revision + 1), true, null);
                SpeechAdmissionChanged?.Invoke();
            }
        }
        catch { Hold(); throw; }
        finally { Exit(); }
    }

    public async Task<QuietHoursReview> ReviewAsync(QuietHoursSchedule? value, RequestOrigin origin,
        SecurityAuditInitiator initiator, long callRevision, CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        origin = HostActivity.Current?.Request.Origin ?? origin;
        ValidateInput(origin, initiator);
        Enter();
        try
        {
            var candidate = await admission.RunAsync(origin, eligible, (request, session) =>
            {
                lock (gate)
                {
                    var saved = preferences.Load();
                    if (state.Effective is null || state.Saved != saved
                        || policy.CheckMutation(request.Origin, callRevision, eligible) is not null)
                    { throw new InvalidOperationException("Inspect quiet hours under current original input and call/channel policy before reviewing."); }
                    var now = time.GetUtcNow();
                    return new QuietHoursReview(value, saved, state.Revision, session, request.Origin, initiator,
                        eligible, callRevision, now, now.AddSeconds(60));
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (!eligible() || preferences.Load() != candidate.Saved)
                { throw new InvalidOperationException("Quiet-hours review admission changed."); }
                review = candidate;
                return candidate;
            }
        }
        finally { Exit(); }
    }

    public async Task<bool> ConfirmAsync(QuietHoursReview candidate, RequestOrigin origin, SecurityAuditInitiator initiator,
        CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        origin = HostActivity.Current?.Request.Origin ?? origin;
        ValidateInput(origin, initiator);
        lock (gate)
        {
            if (busy || !ReferenceEquals(review, candidate))
            { throw new InvalidOperationException("Review this exact quiet-hours change before separate confirmation."); }
            busy = true;
            review = null;
        }
        var writing = false;
        bool Current() => eligible() && candidate.Eligible() && !token.IsCancellationRequested
            && time.GetUtcNow() >= candidate.Created && time.GetUtcNow() < candidate.Expires;
        try
        {
            var accepted = await admission.RunAsync(origin, Current, (request, session) =>
            {
                lock (gate)
                {
                    if (session != candidate.Session || request.Origin != candidate.Origin || initiator != candidate.Initiator
                        || state.Revision != candidate.Revision || preferences.Load() != candidate.Saved)
                    { throw new InvalidOperationException("Quiet-hours review, saved state, session or original channel changed."); }
                    var record = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        "configuration.notices-quiet-hours", SecurityAuditOutcome.Requested, initiator, "preferences.device-local");
                    using var activity = HostActivity.BeginAudit(request, record);
                    audit.Write(record);
                    var denied = policy.CommitVoiceSetting(request.Origin, candidate.CallRevision,
                        () => Current() && ReferenceEquals(HostActivity.RequireCurrent().Request, request), () =>
                        {
                            try
                            {
                                if (preferences.Load() != candidate.Saved)
                                { throw new InvalidDataException("Saved quiet hours changed during requested audit."); }
                                state = state with { Effective = null, Revision = checked(state.Revision + 1) };
                                SpeechAdmissionChanged?.Invoke();
                                preferences.BeginWrite();
                                writing = true;
                                preferences.Save(candidate.Value);
                                if (preferences.ReadBack() != candidate.Value)
                                { throw new InvalidDataException("Quiet-hours readback does not match the reviewed intent."); }
                                if (!Current()) { throw new InvalidOperationException("Quiet-hours authority changed during persistence."); }
                                audit.Write(record.WithOutcome(SecurityAuditOutcome.Succeeded));
                            }
                            catch
                            {
                                audit.Write(record.WithOutcome(SecurityAuditOutcome.Failed, "quiet-hours-write-unconfirmed"));
                                activity.Complete(HostOperationOutcome.Failed);
                                throw;
                            }
                        });
                    if (denied is not null)
                    {
                        audit.Write(record.WithOutcome(SecurityAuditOutcome.Denied, "call-channel-denied"));
                        activity.Complete(HostOperationOutcome.Failed);
                        return false;
                    }
                    activity.Complete(HostOperationOutcome.Completed);
                    return true;
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                if (!accepted) { return false; }
                if (!Current() || preferences.ReadBack() != candidate.Value)
                { throw new InvalidOperationException("Quiet-hours saved state or admission changed before its completed receipt."); }
                preferences.ConfirmWrite();
                if (!Current() || preferences.Load() != candidate.Value)
                { throw new InvalidDataException("Quiet-hours confirmation readback is unconfirmed."); }
                state = new(candidate.Value, candidate.Value ?? QuietHoursSchedule.Off, state.Revision, true, null);
                SpeechAdmissionChanged?.Invoke();
                return true;
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

    private static void ValidateInput(RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || (initiator == SecurityAuditInitiator.VoiceCommand) != (origin == RequestOrigin.ActivatedVoice))
        { throw new InvalidOperationException("Quiet hours require original admitted user input on the original channel."); }
    }

    private void Enter()
    {
        lock (gate)
        {
            if (busy) { throw new InvalidOperationException("Quiet-hours control is already active."); }
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
            state = state with { Effective = null, SavedStateKnown = false, Revision = checked(state.Revision + 1),
                Recovery = "Quiet-hours intent or evidence is unconfirmed. Proactive speech is held; inspect saved intent and audit receipts before explicit repair and inspection. No retry or default fallback." };
            SpeechAdmissionChanged?.Invoke();
        }
    }
}
