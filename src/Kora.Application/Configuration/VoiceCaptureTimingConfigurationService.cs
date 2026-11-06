using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class VoiceCaptureTimingConfigurationService : IVoiceCaptureTimingSource
{
    private readonly Lock gate = new();
    private readonly IVoiceCaptureTimingPreferences preferences;
    private readonly AudioControlAdmission admission;
    private readonly ISecurityAuditLog auditLog;
    private readonly ILogger<VoiceCaptureTimingConfigurationService> logger;
    private VoiceCaptureTiming? saved;
    private VoiceCaptureTiming? timing;
    private long revision;
    private bool writing;

    public VoiceCaptureTimingConfigurationService(IVoiceCaptureTimingPreferences preferences,
        AudioControlAdmission admission, ISecurityAuditLog auditLog, ILogger<VoiceCaptureTimingConfigurationService> logger)
    {
        this.preferences = preferences;
        this.admission = admission;
        this.auditLog = auditLog;
        this.logger = logger;
        Reload();
    }

    public (VoiceCaptureTiming? Timing, bool IsSaved, long Revision) Get()
    {
        lock (gate) { return (timing, saved is not null, revision); }
    }

    public VoiceCaptureTiming GetTiming()
    {
        lock (gate) { return timing ?? throw new InvalidOperationException("Capture timing is invalid, unreadable or unconfirmed. Inspect the preference and audit before explicit recovery."); }
    }

    public void Reload()
    {
        lock (gate)
        {
            if (writing) { throw new InvalidOperationException("Capture timing is committing."); }
            timing = null;
            revision = checked(revision + 1);
            try
            {
                saved = preferences.Load();
                var confirmed = saved ?? VoiceCaptureTiming.Default;
                confirmed.Validate();
                timing = confirmed;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentOutOfRangeException)
            { TimingUnavailable(exception); }
        }
    }

    public async Task<bool> SetAsync(VoiceCaptureTiming proposed, long expectedRevision, long callRevision,
        RequestOrigin origin, SecurityAuditInitiator initiator, CallCommunicationPolicy policy,
        Func<bool> eligible, CancellationToken cancellationToken)
    {
        proposed.Validate();
        origin = HostActivity.Current?.Request.Origin ?? origin;
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        { throw new InvalidOperationException("Capture timing requires original admitted user input."); }
        var entered = false;
        try
        {
            var accepted = await admission.RunAsync(origin, eligible, (request, _) =>
            {
                lock (gate)
                {
                    if (writing || timing is null || revision != expectedRevision)
                    { throw new InvalidOperationException("Capture timing is stale or unavailable. Inspect before reviewing again."); }
                    try
                    {
                        if (preferences.Load() != saved)
                        { throw new InvalidDataException("Saved capture timing changed. Reload before reviewing again."); }
                    }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                    { timing = null; throw; }
                    writing = entered = true;
                    var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        "configuration.voice-capture-timing", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(request.Origin, callRevision,
                        () => eligible() && !cancellationToken.IsCancellationRequested, () =>
                        {
                            timing = null;
                            revision = checked(revision + 1);
                            try
                            {
                                preferences.BeginWrite();
                                preferences.Save(proposed);
                                RequireReadback();
                            }
                            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "preference-write-unconfirmed"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                        });
                    if (denied is { } reason)
                    {
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant()));
                        return false;
                    }
                    return true;
                }
            }, cancellationToken).ConfigureAwait(false);
            if (accepted)
            {
                lock (gate)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!eligible() || policy.CheckMutation(origin, callRevision, eligible) is not null)
                    { throw new InvalidOperationException("Capture timing lost original admission before confirmation."); }
                    RequireReadback();
                    preferences.ConfirmWrite();
                    try
                    {
                        if (preferences.Load() != proposed) { throw new InvalidDataException("Confirmed capture timing does not match."); }
                    }
                    catch { preferences.BeginWrite(); throw; }
                    saved = timing = proposed;
                }
            }
            return accepted;
        }
        catch
        {
            if (entered) { lock (gate) { timing = null; } }
            throw;
        }
        finally { if (entered) { lock (gate) { writing = false; } } }

        void RequireReadback()
        {
            if (preferences.ReadBack() != proposed) { throw new InvalidDataException("Capture timing readback does not match."); }
        }
    }
}
