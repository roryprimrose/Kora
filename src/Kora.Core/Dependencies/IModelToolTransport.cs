namespace Kora.Core.Dependencies;

/// <summary>One provider exchange, without automatic retries, tool execution, history storage or fallback.</summary>
public interface IModelToolTransport : IAsyncDisposable
{
    /// <summary>Exchanges one complete bounded request without acquiring caller, tool or egress authority.</summary>
    /// <param name="request">Exact host provenance, frozen context/profile, complete history and remaining byte budgets.</param>
    /// <param name="cancellationToken">Cancellation for the owned exchange.</param>
    /// <returns>A typed outcome and, on success only, a validated assistant message with complete byte charges.</returns>
    Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken);
}
