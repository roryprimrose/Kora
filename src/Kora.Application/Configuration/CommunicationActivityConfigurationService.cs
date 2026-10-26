using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class CommunicationActivityConfigurationService(
    LocalCommunicationActivityPreferences preferences, CommunicationActivityCallStateService observations,
    AudioControlAdmission admission, ISecurityAuditLog auditLog,
    ILogger<CommunicationActivityConfigurationService> logger)
{
    private int applying;

    public async Task<CallMutationOutcome> SelectAsync(CommunicationActivitySelection selection,
        RequestOrigin origin, long callRevision, CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        if (!Enum.IsDefined(selection)) { throw new ArgumentOutOfRangeException(nameof(selection)); }
        if (Interlocked.CompareExchange(ref applying, 1, 0) != 0)
        { throw new InvalidOperationException("A communication activity preference operation is already pending."); }
        var enteredWrite = false;
        var readSaved = false;
        try
        {
            var result = await admission.RunAsync(origin, eligible, (request, _) =>
            {
                var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    "configuration.communication-activity", SecurityAuditOutcome.Requested,
                    request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
                    "preferences.device-local");
                auditLog.Write(audit);
                var outcome = CallMutationOutcome.Applied;
                var denied = policy.CommitVoiceSetting(request.Origin, callRevision,
                    () => eligible() && !token.IsCancellationRequested, () =>
                    {
                        try
                        {
                            if (!observations.PreferencesAvailable || observations.ResourceReleaseFailed)
                            {
                                outcome = CallMutationOutcome.HostUnavailable;
                                return;
                            }
                            if (selection == CommunicationActivitySelection.WindowsCommunicationsAudio && !observations.IsQualified)
                            {
                                outcome = CallMutationOutcome.HostUnavailable;
                                return;
                            }
                            readSaved = true;
                            if (preferences.Load() != observations.Selection)
                            { throw new InvalidDataException("Communication activity preference changed; inspect again."); }
                            if (policy.CheckMutation(request.Origin, callRevision, eligible) is { } changed)
                            { outcome = changed; return; }
                            if (selection == observations.Selection) { outcome = CallMutationOutcome.Unchanged; return; }
                            enteredWrite = true;
                            preferences.BeginWrite();
                            preferences.Save(selection);
                            if (preferences.ReadBack() != selection)
                            { throw new InvalidDataException("Communication activity preference readback did not match."); }
                            token.ThrowIfCancellationRequested();
                            if (!eligible()) { throw new InvalidOperationException("Communication activity input or host ownership changed before confirmation."); }
                        }
                        catch
                        {
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "preference-unconfirmed"));
                            throw;
                        }
                    });
                if (denied is { } reason) { outcome = reason; }
                auditLog.Write(audit.WithOutcome(outcome is CallMutationOutcome.Applied or CallMutationOutcome.Unchanged
                    ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Denied, outcome.ToString().ToLowerInvariant()));
                return outcome;
            }, token).ConfigureAwait(false);
            if (enteredWrite)
            {
                token.ThrowIfCancellationRequested();
                if (!eligible() || preferences.ReadBack() != selection)
                { throw new InvalidOperationException("Communication activity preference or original admission changed before its receipt."); }
                if (policy.CheckMutation(origin, callRevision, eligible) is not null)
                { throw new InvalidOperationException("Call policy changed before communication activity preference confirmation."); }
                preferences.ConfirmWrite();
                try
                {
                    if (!eligible() || preferences.Load() != selection)
                    { throw new InvalidDataException("Communication activity preference confirmation is unavailable."); }
                    observations.HoldUnavailable();
                    await observations.ApplyConfirmed(selection).ConfigureAwait(false);
                    if (selection == CommunicationActivitySelection.None)
                    { await observations.JoinAsync().ConfigureAwait(false); }
                }
                catch { preferences.BeginWrite(); throw; }
            }
            return result;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (enteredWrite || readSaved) { observations.HoldUnavailable(); }
            ConfigurationFailed(logger);
            throw;
        }
        finally { Volatile.Write(ref applying, 0); }
    }
}