using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class OrdinaryTurnDeadlineConfigurationService(
    LocalOrdinaryTurnDeadlinePreferences preferences, AudioControlAdmission admission, ISecurityAuditLog audit)
{
    private readonly Lock gate = new();
    private OrdinaryTurnDeadlineObservation? observed;
    private long revision;
    private bool applying;
    private RequestOrigin observedOrigin;
    private Func<bool> observedEligibility = static () => false;

    public OrdinaryTurnDeadline Capture()
    {
        lock (gate)
        {
            RequireIdle();
            return preferences.Load() ?? OrdinaryTurnDeadline.Default;
        }
    }

    public async Task<OrdinaryTurnDeadlineObservation> RefreshAsync(RequestOrigin origin, Func<bool> eligible, CancellationToken token)
    {
        var value = await admission.RunAsync(origin, eligible, (_, _) =>
        {
            lock (gate)
            {
                RequireIdle();
                observed = null;
                return preferences.Load();
            }
        }, token).ConfigureAwait(false);
        lock (gate)
        {
            RequireIdle();
            if (!eligible() || preferences.Load() != value)
            { throw new InvalidOperationException("Deadline discovery changed before its receipt. Refresh again."); }
            observedOrigin = origin;
            observedEligibility = eligible;
            observed = new(checked(++revision), value);
            return observed;
        }
    }

    public async Task<bool> SaveAsync(OrdinaryTurnDeadlineObservation expected, OrdinaryTurnDeadline? value,
        RequestOrigin origin, SecurityAuditInitiator initiator, CallCommunicationPolicy callPolicy,
        long callRevision, Func<bool> eligible, CancellationToken token)
    {
        if (value is { } deadline) { _ = deadline.Duration; }
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        { throw new InvalidOperationException("Only original admitted user input may change the future task deadline."); }
        var entered = false;
        try
        {
            var accepted = await admission.RunAsync(origin, eligible, (request, _) =>
            {
                lock (gate)
                {
                    RequireIdle();
                    if (!ReferenceEquals(observed, expected)
                        || observedOrigin != request.Origin || !observedEligibility()
                        || preferences.Load() != expected.Saved)
                    { throw new InvalidOperationException("The observed deadline changed. Refresh and choose again."); }
                    applying = true;
                    entered = true;
                    observed = null;
                    var item = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        "configuration.ordinary-turn-deadline", SecurityAuditOutcome.Requested,
                        initiator, "preferences.device-local");
                    audit.Write(item);
                    var denied = callPolicy.CommitVoiceSetting(origin, callRevision,
                        () => eligible() && !token.IsCancellationRequested && preferences.Load() == expected.Saved, () =>
                        {
                            try
                            {
                                preferences.BeginWrite();
                                preferences.Save(value);
                                if (preferences.ReadBack() != value)
                                { throw new InvalidDataException("The exact future task deadline readback was not confirmed."); }
                                audit.Write(item.WithOutcome(SecurityAuditOutcome.Succeeded));
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                or InvalidDataException or InvalidOperationException)
                            {
                                audit.Write(item.WithOutcome(SecurityAuditOutcome.Failed, "preference-unconfirmed"));
                                throw;
                            }
                        });
                    if (denied is not null)
                    {
                        audit.Write(item.WithOutcome(SecurityAuditOutcome.Denied, "host-admission-changed"));
                        return false;
                    }
                    return true;
                }
            }, token).ConfigureAwait(false);
            if (accepted)
            {
                lock (gate)
                {
                    if (!eligible() || preferences.ReadBack() != value)
                    { throw new InvalidDataException("Deadline admission or saved value changed before its terminal receipt."); }
                    preferences.ConfirmWrite();
                    try
                    {
                        if (!eligible() || preferences.Load() != value)
                        { throw new InvalidDataException("Confirmed deadline changed before activation."); }
                    }
                    catch
                    {
                        preferences.BeginWrite();
                        throw;
                    }
                }
            }
            return accepted;
        }
        finally { if (entered) { lock (gate) { applying = false; } } }
    }

    private void RequireIdle()
    {
        if (applying)
        { throw new InvalidDataException("Ordinary task deadline confirmation is in progress; control and dispatch are unavailable."); }
    }
}
