using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class InputDevicePreferenceService(IAudioDevicePreferences preferences, ISecurityAuditLog auditLog)
{
    public const string AuditAction = "configuration.microphone";
    private readonly Lock gate = new();
    private bool applying;

    public string Source { get; private set; } = "unavailable";
    public string? DesiredId { get; private set; }

    public string? Load()
    {
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("Input preference is committing; inspect after it completes."); }
            Source = "unavailable";
            DesiredId = null;
            var id = preferences.LoadMicrophoneId();
            DesiredId = id ?? SystemAudioDevices.Microphone.Id;
            Source = id is null ? "default" : "saved";
            return id;
        }
    }

    internal bool Apply(MicrophoneDevice choice, HostRequest request, long callRevision,
        SecurityAuditInitiator initiator, CallCommunicationPolicy policy,
        Func<bool> hostEligible, Action invalidateInput)
    {
        lock (gate)
        {
            if (applying) { return false; }
            applying = true;
            try
            {
                var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                    AuditAction, SecurityAuditOutcome.Requested,
                    request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                    "preferences.device-local");
                auditLog.Write(audit);
                invalidateInput();
                var saved = false;
                var denied = policy.CommitVoiceSetting(request.Origin, callRevision,
                    () => ReferenceEquals(HostActivity.Current?.Request, request) && hostEligible(),
                    () =>
                    {
                        try
                        {
                            if (choice.IsSystemDefault) { preferences.ClearMicrophoneId(); }
                            else { preferences.SaveMicrophoneId(choice.Id); }
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                        {
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                exception is UnauthorizedAccessException ? "access-denied" : "io-error"));
                            throw;
                        }
                        // Terminal evidence failure does not publish or activate a replaced preference.
                        Source = "unavailable";
                        DesiredId = null;
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                        Source = choice.IsSystemDefault ? "default" : "saved";
                        DesiredId = choice.Id;
                        saved = true;
                    });
                if (denied is { } reason)
                {
                    auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant()));
                }
                return saved;
            }
            finally { applying = false; }
        }
    }
}
