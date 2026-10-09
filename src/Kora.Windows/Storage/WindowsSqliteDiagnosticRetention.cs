using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Windows.Storage;

public sealed partial class WindowsSqliteDiagnosticRetention(
    WindowsSqliteEvidenceSink sink, TimeProvider time, ILogger<WindowsSqliteDiagnosticRetention> logger)
{
    public const int MaximumLogs = 128;
    public const int MaximumSpans = 32;
    public const int MaximumLinks = MaximumSpans * 32;

    public async ValueTask<DiagnosticRetentionBatch> RunAsync(CancellationToken cancellationToken)
    {
        var current = HostActivity.RequireCurrent();
        if (current.Activity!.IsStopped || current.Request.Origin != RequestOrigin.HostSystem)
        {
            throw new InvalidOperationException("Diagnostic retention requires a live host-system lifecycle request.");
        }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Windows, HostOperation.Retention);
        DiagnosticRetentionBatch result;
        try
        {
            var cutoff = time.GetUtcNow();
            result = await Task.Run(() => sink.PruneOrdinaryDiagnostics(cutoff, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Completed);
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The lifecycle caller reports storage failures through its independent file/startup path.
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        // Delivery failure cannot relabel a verified COMMIT as cancellation or rollback.
        // Providers may write to the same partition, so emit only after releasing its lease.
        Pruned(logger, result.Logs, result.Spans, result.Links, result.HasMore);
        return result;
    }
}
