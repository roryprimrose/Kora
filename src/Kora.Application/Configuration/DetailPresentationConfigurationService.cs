using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Presentation;

namespace Kora.Application.Configuration;

public sealed class DetailPresentationConfigurationService(
    IDetailPresentationPreferences preferences, AudioControlAdmission admission, ISecurityAuditLog auditLog, TimeProvider time)
{
    private readonly Lock gate = new();
    private DetailPresentationMode? saved;
    private DetailPresentationReview? review;
    private bool available;
    private bool busy;
    private long revision;
    private long offerEpoch;
    public event EventHandler? Changed;

    public string Description
    {
        get
        {
            lock (gate)
            {
                return available
                    ? $"Detail presentation: {saved ?? DetailPresentationPreference.Default} ({(saved is null ? "unsaved default" : "saved")}). "
                        + "Device-local, future publications only. Manual details remain available. Automatic opening is unavailable."
                    : "Detail presentation unavailable; proactive offers are held. Inspect saved state and audit before explicit recovery. Manual details remain available.";
            }
        }
    }

    public long? CaptureOfferEpoch()
    {
        lock (gate)
        {
            return available && (saved ?? DetailPresentationPreference.Default) == DetailPresentationMode.Offer ? offerEpoch : null;
        }
    }

    public bool IsOfferCurrent(long? epoch) => epoch is not null && epoch == CaptureOfferEpoch();
    public void Cancel() { lock (gate) { review = null; } }

    private DetailPresentationMode? Read(bool readBack = false)
    {
        var value = readBack ? preferences.ReadBack() : preferences.Load();
        if (value is { } mode) { _ = DetailPresentationPreference.Format(mode); }
        return value;
    }

    public void Observe()
    {
        lock (gate)
        {
            if (busy) { throw new InvalidOperationException("Detail presentation control is already active."); }
            try { Publish(Read()); }
            catch { HoldUnavailable(); throw; }
        }
    }

    public async Task InspectAsync(RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        Enter();
        try
        {
            var value = await admission.RunAsync(origin, eligible, (_, _) => Read(), token).ConfigureAwait(false);
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (!eligible()) { throw new InvalidOperationException("Detail preference inspection lost original admission."); }
                Publish(value);
            }
        }
        catch { HoldUnavailable(); throw; }
        finally { Exit(); }
    }

    public async Task<DetailPresentationReview> ReviewAsync(DetailPresentationMode value, RequestOrigin origin, long callRevision,
        CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        _ = DetailPresentationPreference.Format(value);
        Enter();
        try
        {
            var candidate = await admission.RunAsync(origin, eligible, (request, session) =>
            {
                lock (gate)
                {
                    if (policy.CheckMutation(request.Origin, callRevision, eligible) is not null)
                    { throw new InvalidOperationException("Detail preference review denied by original call/channel policy."); }
                    Publish(Read());
                    return new DetailPresentationReview(value, saved, revision, session, request.Origin, eligible,
                        callRevision, time.GetUtcNow().AddSeconds(60));
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (!eligible() || revision != candidate.Revision)
                { throw new InvalidOperationException("Detail preference review lost original admission or revision."); }
                review = candidate;
                return candidate;
            }
        }
        catch { HoldUnavailable(); throw; }
        finally { Exit(); }
    }

    public async Task<bool> ConfirmAsync(DetailPresentationReview candidate, RequestOrigin origin, SecurityAuditInitiator initiator,
        CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || (origin == RequestOrigin.ActivatedVoice) != (initiator == SecurityAuditInitiator.VoiceCommand))
        { throw new InvalidOperationException("Detail preference confirmation requires original user channel provenance."); }
        lock (gate)
        {
            if (busy || !available || !ReferenceEquals(review, candidate) || revision != candidate.Revision)
            { throw new InvalidOperationException("Review this exact detail preference change first."); }
            busy = true;
            review = null;
        }
        try
        {
            bool Current() => eligible() && candidate.Eligible() && time.GetUtcNow() < candidate.Expires
                && !token.IsCancellationRequested;
            var accepted = await admission.RunAsync(origin, Current, (request, session) =>
            {
                lock (gate)
                {
                    if (session != candidate.Session || request.Origin != origin || request.Origin != candidate.Origin || Read() != candidate.Saved
                        || revision != candidate.Revision)
                    { throw new InvalidOperationException("Detail preference source, session, channel or revision changed."); }
                    var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        "configuration.detail-presentation", SecurityAuditOutcome.Requested, initiator, "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(request.Origin, candidate.CallRevision,
                        () => Current() && ReferenceEquals(HostActivity.RequireCurrent().Request, request), () =>
                        {
                            if (Read() != candidate.Saved)
                            { throw new InvalidDataException("Detail preference changed during requested audit."); }
                            HoldUnavailable();
                            try
                            {
                                preferences.BeginWrite();
                                preferences.Save(candidate.Value);
                                RequireReadback(candidate.Value);
                            }
                            catch
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "preference-write-unconfirmed"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                        });
                    if (denied is not null)
                    { auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, "call-channel-denied")); return false; }
                    return true;
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                if (accepted)
                {
                    if (!Current()) { throw new InvalidOperationException("Detail preference lost admission before its completed receipt."); }
                    RequireReadback(candidate.Value);
                    preferences.ConfirmWrite();
                    try
                    {
                        if (Read() != candidate.Value) { throw new InvalidDataException("Confirmed detail preference does not match."); }
                        if (!Current()) { throw new InvalidOperationException("Detail preference lost admission during confirmation."); }
                    }
                    catch { preferences.BeginWrite(); throw; }
                    Publish(candidate.Value);
                }
                return accepted;
            }
        }
        catch { HoldUnavailable(); throw; }
        finally { Exit(); }
    }

    private void RequireReadback(DetailPresentationMode value)
    {
        if (Read(readBack: true) != value) { throw new InvalidDataException("Detail preference readback does not match."); }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            available = false;
            review = null;
            revision = checked(revision + 1);
            offerEpoch = checked(offerEpoch + 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Publish(DetailPresentationMode? value)
    {
        if (available && saved == value) { return; }
        saved = value;
        available = true;
        review = null;
        revision = checked(revision + 1);
        offerEpoch = checked(offerEpoch + 1);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Enter()
    {
        lock (gate)
        {
            if (busy) { throw new InvalidOperationException("Detail presentation control is already active."); }
            busy = true;
            review = null;
        }
    }
    private void Exit() { lock (gate) { busy = false; } }
}
