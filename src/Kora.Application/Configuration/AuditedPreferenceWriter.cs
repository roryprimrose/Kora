using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Configuration;

public sealed class AuditedPreferenceWriter(ISecurityAuditLog auditLog)
{
    public Task WriteAsync(Action persist, string actionId, SecurityAuditInitiator initiator,
        RequestOrigin origin, Func<bool> eligible, string? failureReason = null) => Task.Run(() =>
    {
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Storage, origin);
        var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
            actionId, SecurityAuditOutcome.Requested, initiator, "preferences.device-local");
        auditLog.Write(audit);
        try
        {
            if (!eligible())
            {
                throw new InvalidOperationException("The original preference input is no longer eligible. Initiate a fresh change.");
            }
            persist();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or InvalidDataException or InvalidOperationException)
        {
            activity.Complete(HostOperationOutcome.Failed);
            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed, failureReason ?? (exception switch
            {
                UnauthorizedAccessException => "access-denied",
                IOException => "io-error",
                _ => "preference-write-not-confirmed",
            })));
            throw;
        }
        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
        activity.Complete(HostOperationOutcome.Completed);
    });
}
