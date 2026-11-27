using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class RoutingAdviceConfigurationService(
    LocalRoutingAdvicePreferences preferences, AudioControlAdmission admission, ISecurityAuditLog auditLog)
{
    public sealed record State(bool Enabled, long Revision);
    private readonly Lock gate = new();
    private bool? saved;
    private volatile State? current;
    private bool applying;
    private long revision;
    public State? Get() => current;

    public void Observe()
    {
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("The routing advice preference is committing."); }
            try
            {
                saved = preferences.Load();
                current = new(saved == true, checked(++revision));
            }
            catch { HoldUnavailable(); throw; }
        }
    }

    private void HoldUnavailable()
    {
        current = null;
        revision = checked(revision + 1);
    }

    public async Task<bool> SetAsync(bool enabled, long expectedRevision, long callRevision,
        RequestOrigin origin, SecurityAuditInitiator initiator, CallCommunicationPolicy policy,
        Func<bool> eligible, CancellationToken token)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        { throw new InvalidOperationException("Routing advice settings require original admitted user input."); }
        var entered = false;
        var originalOrigin = origin;
        try
        {
            var accepted = await admission.RunAsync(origin, eligible, (request, _) =>
            {
                lock (gate)
                {
                    if (applying || current is null || revision != expectedRevision)
                    { throw new InvalidOperationException("The routing advice preference changed or is unavailable."); }
                    try
                    {
                        if (preferences.Load() != saved)
                        { throw new InvalidDataException("The saved routing advice preference changed."); }
                    }
                    catch { HoldUnavailable(); throw; }
                    applying = true;
                    entered = true;
                    originalOrigin = request.Origin;
                    var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                        "configuration.routing-advice", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(request.Origin, callRevision,
                        () => eligible() && !token.IsCancellationRequested, () =>
                        {
                            HoldUnavailable();
                            try
                            {
                                preferences.BeginWrite();
                                preferences.Save(enabled);
                                RequireReadback();
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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
            }, token).ConfigureAwait(false);
            if (accepted)
            {
                lock (gate)
                {
                    token.ThrowIfCancellationRequested();
                    var denied = policy.CommitVoiceSetting(originalOrigin, callRevision, eligible, () =>
                    {
                        RequireReadback();
                        try
                        {
                            preferences.ConfirmWrite();
                            if (preferences.Load() != enabled) { throw new InvalidDataException("Confirmed routing advice preference does not match."); }
                            token.ThrowIfCancellationRequested();
                            if (policy.CheckMutation(originalOrigin, callRevision, eligible) is not null)
                            { throw new InvalidOperationException("Routing advice setting lost original admission during confirmation."); }
                        }
                        catch { preferences.BeginWrite(); throw; }
                        current = new(enabled, revision);
                    });
                    if (denied is not null)
                    { throw new InvalidOperationException("Routing advice setting lost original admission before confirmation."); }
                }
            }
            return accepted;
        }
        catch { if (entered) { lock (gate) { HoldUnavailable(); } } throw; }
        finally { if (entered) { lock (gate) { applying = false; } } }

        void RequireReadback()
        {
            if (preferences.ReadBack() != enabled) { throw new InvalidDataException("Routing advice preference readback does not match."); }
        }
    }
}
