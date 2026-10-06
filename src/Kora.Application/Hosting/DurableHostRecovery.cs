using Kora.Application.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Hosting;

public sealed class DurableHostRecovery(
    IHostTaskStore store,
    HostTaskCoordinator coordinator,
    ISecurityAuditLog audit,
    ILogger<DurableHostRecovery> logger)
{
    public async Task<IReadOnlyList<HostTaskRecord>> RecoverAsync(CancellationToken cancellationToken)
    {
        var incomplete = await store.ReadIncompleteAsync(100, cancellationToken).ConfigureAwait(false);
        if (incomplete.Count > 100 || incomplete.Any(record => record.IsTerminal))
        {
            throw new InvalidDataException("The store returned invalid bounded recovery data.");
        }
        var recovered = new List<HostTaskRecord>(incomplete.Count);
        foreach (var prior in incomplete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var activity = HostActivity.BeginRoot(
                prior.Request, HostActivityLayer.Application, HostOperation.Recovery);
            var outcome = prior.Recover().State;
            audit.Write(new SecurityAuditEvent(prior.Request.RequestId.Value,
                SecurityAuditCategory.ApplicationExecution, "host.task.recovery",
                SecurityAuditOutcome.Unknown, SecurityAuditInitiator.System, "application.current",
                reasonCode: outcome == HostTaskState.Interrupted ? "intent-interrupted" : "dispatch-unverified"));
            ApplicationLog.Information(logger, outcome == HostTaskState.Interrupted
                ? "Recovering intent-only task as Interrupted without replay"
                : "Recovering dispatched task as Unknown without replay");
            recovered.Add(await coordinator.RecordOutcomeAsync(prior, outcome, cancellationToken).ConfigureAwait(false));
            activity.Complete(HostOperationOutcome.Completed);
        }
        if ((await store.ReadIncompleteAsync(1, cancellationToken).ConfigureAwait(false)).Count != 0)
        {
            throw new InvalidOperationException("The startup recovery batch is exhausted; further explicit recovery is required.");
        }
        return recovered.AsReadOnly();
    }
}
