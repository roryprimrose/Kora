using Kora.Core.Hosting;

namespace Kora.Core.Dependencies;

/// <summary>A host-owned one-shot local context lease, issued only after real durable session/task admission.</summary>
public interface IAdmittedModelContextTurn : IAsyncDisposable
{
    /// <summary>The genuine admitted request used to construct the complete envelope and exact-request evidence.</summary>
    HostRequest Request { get; }

    /// <summary>The admitted active session generation, not source text or model authority.</summary>
    HostRevision SessionGeneration { get; }

    /// <summary>Truthful terminal completion, including idle cancellation and abandonment; no automatic retry.</summary>
    Task<LocalContextTurnReceipt> Completion { get; }

    /// <summary>Consumes one complete LocalOnly, tool-free envelope; disposal cancels and awaits terminal resource quiescence.</summary>
    Task<LocalContextTurnReceipt> RunAsync(ModelContextEnvelope context, CancellationToken token);
}