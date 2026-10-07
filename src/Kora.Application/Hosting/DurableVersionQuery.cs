using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed class DurableVersionQuery(
    HostTaskCoordinator coordinator,
    ISecurityAuditLog audit,
    ILogger<DurableVersionQuery> logger)
{
    public const string StorageDisclosure =
        "Kora stores task and content-free diagnostic/audit records in private local Windows-profile SQLite files. "
        + "These files are not encrypted: copies outside the private location are readable, and same-user/admin access is not prevented. "
        + "Database records receive 30-day diagnostic and 90-day audit due dates. "
        + "Each admitted startup prunes at most 128 due diagnostics and 32 due spans with their links; due backlog can remain. "
        + "Audit, task and session deletion is not implemented. Reading evidence does not extend retention. "
        + "Daily files retain at most 30 days/30 files. Credentials remain Windows-protected.";

    public async Task<HostTaskRecord> RunAsync(
        RequestOrigin origin, Func<Task> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var request = HostRequest.Create(origin);
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
        var intent = await coordinator.RecordIntentAsync(request, cancellationToken);
        var requested = new SecurityAuditEvent(request.RequestId.Value,
            SecurityAuditCategory.ApplicationExecution, "host.query.version",
            SecurityAuditOutcome.Requested, origin == RequestOrigin.ActivatedVoice
                ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            "application.current");
        audit.Write(requested);
        ApplicationLog.Information(logger, "Admitted durable local version query");
        cancellationToken.ThrowIfCancellationRequested();
        var dispatched = await coordinator.RecordDispatchAsync(intent, cancellationToken);
        try
        {
            await query().WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            // Cancellation/late completion is not a verified terminal query receipt.
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            audit.Write(requested.WithOutcome(SecurityAuditOutcome.Failed, "query-failed"));
            ApplicationLog.Error(logger, exception, "Durable local version query");
            await coordinator.RecordOutcomeAsync(dispatched, HostTaskState.Failed, CancellationToken.None);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }

        // Required terminal evidence precedes the receipt. A failed write leaves Unknown recovery,
        // never a successful receipt with a missing audit.
        audit.Write(requested.WithOutcome(SecurityAuditOutcome.Succeeded, "local-query-returned"));
        ApplicationLog.Information(logger, "Durable local version query returned");
        var terminal = await coordinator.RecordOutcomeAsync(
            dispatched, HostTaskState.Succeeded, CancellationToken.None);
        activity.Complete(HostOperationOutcome.Completed);
        return terminal;
    }
}
