using System.Collections.Immutable;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Session-retained observations, never model execution or renewed session authority.</summary>
public interface ISessionModelMetricsStore
{
    /// <summary>Records actual usage under a live exact host context; exact turn replays are idempotent.</summary>
    /// <param name="usage">The original host-bound turn and content-free usage.</param>
    /// <param name="token">Cancels storage, independently of any completed adapter cancellation.</param>
    ValueTask RecordModelUsageAsync(SessionModelUsage usage, CancellationToken token);

    /// <summary>Passively reads all retained observations for the exact session without renewing activity.</summary>
    /// <param name="session">The exact retained session.</param>
    /// <param name="token">Cancels the read.</param>
    /// <returns>Every retained original observation, ordered by observation time and turn identity.</returns>
    ValueTask<ImmutableArray<SessionModelUsage>> ReadModelUsageAsync(HostId<SessionIdentity> session, CancellationToken token);
}
