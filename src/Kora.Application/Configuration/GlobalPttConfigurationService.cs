using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class GlobalPttConfigurationService(
    LocalGlobalPttPreferences preferences, AudioControlAdmission admission, ISecurityAuditLog auditLog)
{
    public sealed record State(GlobalPttShortcut Shortcut, long Revision);
    private readonly Lock gate = new();
    private GlobalPttShortcut? saved;
    private volatile State? current;
    private bool applying;
    private long revision;
    public State? Get() => current;

    public void Observe()
    {
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("The global PTT preference is committing."); }
            try
            {
                saved = preferences.Load();
                current = new(saved, checked(++revision));
            }
            catch { HoldUnavailable(); throw; }
        }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            current = null;
            revision = checked(revision + 1);
        }
    }

    public async Task<bool> SetAsync(GlobalPttShortcut shortcut, long expectedRevision, long callRevision,
        RequestOrigin origin, SecurityAuditInitiator initiator, CallCommunicationPolicy policy,
        Func<bool> eligible, CancellationToken cancellationToken)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        { throw new InvalidOperationException("Global PTT settings require original admitted user input."); }
        var entered = false;
        try
        {
            var accepted = await admission.RunAsync(origin, eligible, (request, _) =>
            {
                lock (gate)
                {
                    if (applying || current is null || revision != expectedRevision)
                    { throw new InvalidOperationException("The global PTT preference changed or is unavailable. Inspect before choosing again."); }
                    try
                    {
                        if (preferences.Load() != saved)
                        { throw new InvalidDataException("The saved global PTT preference changed. Inspect before choosing again."); }
                    }
                    catch { HoldUnavailable(); throw; }
                    applying = true;
                    entered = true;
                    var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                        "configuration.global-ptt-shortcut", SecurityAuditOutcome.Requested,
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
                                preferences.Save(shortcut);
                                RequireReadback();
                            }
                            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "preference-write-unconfirmed"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            saved = shortcut;
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
                    if (!eligible()) { throw new InvalidOperationException("The global PTT setting lost original admission before confirmation."); }
                    RequireReadback();
                    preferences.ConfirmWrite();
                    try
                    {
                        if (preferences.Load() != shortcut) { throw new InvalidDataException("The confirmed global PTT preference does not match."); }
                    }
                    catch { preferences.BeginWrite(); throw; }
                    current = new(shortcut, revision);
                }
            }
            return accepted;
        }
        catch { if (entered) { HoldUnavailable(); } throw; }
        finally { if (entered) { lock (gate) { applying = false; } } }

        void RequireReadback()
        {
            if (preferences.ReadBack() != shortcut) { throw new InvalidDataException("Global PTT preference readback does not match."); }
        }
    }
}
