using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class ConversationalReplyConfigurationService(
    IConversationalReplyPreferences preferences, AudioControlAdmission admission, ISecurityAuditLog auditLog)
{
    private readonly Lock gate = new();
    private bool? saved;
    private bool available;
    private volatile ConversationalReplyState? current;
    private int writesInProgress;
    private long revision;
    public event EventHandler? Changed;
    public ConversationalReplyState? Get() => current;

    public void Observe()
    {
        lock (gate)
        {
            if (writesInProgress != 0) { throw new InvalidOperationException("Conversational reply preference is committing."); }
            try
            {
                saved = preferences.Load();
                available = true;
                revision = checked(revision + 1);
                current = new(saved == true, revision);
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch { HoldUnavailable(); throw; }
        }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            available = false;
            current = null;
            revision = checked(revision + 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<bool> SetAsync(bool enabled, long expectedRevision, long callRevision,
        RequestOrigin origin, SecurityAuditInitiator initiator, CallCommunicationPolicy policy,
        Func<bool> eligible, CancellationToken cancellationToken)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        {
            throw new InvalidOperationException("Conversational reply settings require original admitted user input.");
        }
        var entered = false;
        try
        {
            var accepted = await admission.RunAsync(origin, eligible, (request, _) =>
            {
                lock (gate)
                {
                    if (!available || revision != expectedRevision)
                    { throw new InvalidOperationException("Conversational reply preference changed or is unavailable. Inspect before choosing again."); }
                    try
                    {
                        if (preferences.Load() != saved)
                        { throw new InvalidOperationException("Saved conversational reply preference changed. Inspect before choosing again."); }
                    }
                    catch { HoldUnavailable(); throw; }
                    writesInProgress = checked(writesInProgress + 1);
                    entered = true;
                    var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                        "configuration.conversational-replies", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(request.Origin, callRevision,
                        () => eligible() && !cancellationToken.IsCancellationRequested, () =>
                        {
                            HoldUnavailable();
                            try
                            {
                                preferences.BeginWrite();
                                preferences.Save(enabled);
                                RequireReadback();
                            }
                            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "preference-write-unconfirmed"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            saved = enabled;
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
                    if (!eligible()) { throw new InvalidOperationException("Conversational reply setting lost original admission before confirmation."); }
                    RequireReadback();
                    preferences.ConfirmWrite();
                    try
                    {
                        if (preferences.Load() != enabled) { throw new InvalidDataException("Confirmed conversational reply preference does not match."); }
                    }
                    catch { preferences.BeginWrite(); throw; }
                    available = true;
                    current = new(enabled, revision);
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            return accepted;
        }
        catch { if (entered) { HoldUnavailable(); } throw; }
        finally { if (entered) { lock (gate) { writesInProgress--; } } }

        void RequireReadback()
        {
            if (preferences.ReadBack() != enabled) { throw new InvalidDataException("Conversational reply preference readback does not match."); }
        }
    }
}
