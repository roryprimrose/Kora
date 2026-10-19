namespace Kora.Core.Dependencies;

/// <summary>One provider exchange, without automatic retries, tool execution, history storage or fallback.</summary>
public interface IModelToolTransport : IAsyncDisposable
{
    Task<ModelToolTransportReply> CompleteAsync(ModelToolRequest request, CancellationToken cancellationToken);
}
