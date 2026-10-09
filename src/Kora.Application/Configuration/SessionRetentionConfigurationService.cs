using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Configuration;

public sealed class SessionRetentionConfigurationService(
    ISessionRetentionPreferences preferences, SessionRetentionPolicy policy,
    DiagnosticRetentionAdmission admission, ISecurityAuditLog auditLog)
{
    private readonly Lock gate = new();
    private SessionRetentionSettings? observed;
    private bool available;
    private bool applying;

    public bool Available { get { lock (gate) { return available; } } }
    public SessionRetentionSettings Settings => policy.Settings;

    public void Observe()
    {
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("Session-retention preference is committing."); }
            available = false;
            policy.HoldUnavailable();
            observed = preferences.Load();
            policy.Activate(observed ?? SessionRetentionSettings.Default);
            available = true;
        }
    }

    public async Task<bool> SaveAsync(SessionRetentionSettings? value, CallCommunicationPolicy callPolicy,
        long callRevision, Func<bool> eligible, CancellationToken token)
    {
        value?.Validate();
        try
        {
            var accepted = await admission.RunAsync(RequestOrigin.LocalUi, eligible, (request, _) =>
            {
                lock (gate)
                {
                    if (!available || preferences.Load() != observed)
                    {
                        throw new InvalidOperationException("Refresh session retention before changing its future policy.");
                    }
                    var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        "configuration.session-retention", SecurityAuditOutcome.Requested,
                        SecurityAuditInitiator.LocalUser, "preferences.device-local");
                    auditLog.Write(audit);
                    applying = true;
                    var denied = callPolicy.CommitVoiceSetting(request.Origin, callRevision,
                        () => eligible() && !token.IsCancellationRequested && preferences.Load() == observed, () =>
                        {
                            try
                            {
                                available = false;
                                preferences.BeginWrite();
                                if (value is { } settings) { preferences.Save(settings); } else { preferences.Reset(); }
                                if (preferences.ReadBack() != value)
                                {
                                    throw new InvalidDataException("Session-retention readback differs from the requested preference.");
                                }
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, "preference-unconfirmed"));
                                throw;
                            }
                        });
                    if (denied is { })
                    {
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, "host-admission-changed"));
                        return false;
                    }
                    return true;
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                if (accepted)
                {
                    if (!eligible() || preferences.ReadBack() != value)
                    {
                        throw new InvalidDataException("Session-retention preference or host admission changed before its durable receipt.");
                    }
                    preferences.ConfirmWrite();
                    try
                    {
                        if (preferences.Load() != value || !eligible())
                        {
                            throw new InvalidDataException("Confirmed session retention or admission changed before activation.");
                        }
                        observed = value;
                        policy.Activate(value ?? SessionRetentionSettings.Default);
                        available = true;
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
                    {
                        preferences.BeginWrite();
                        throw;
                    }
                }
            }
            return accepted;
        }
        catch
        {
            lock (gate) { available = false; policy.HoldUnavailable(); }
            throw;
        }
        finally { lock (gate) { applying = false; } }
    }
}
