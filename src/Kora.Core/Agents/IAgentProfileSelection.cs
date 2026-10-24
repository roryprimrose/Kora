using Kora.Core.Context;

namespace Kora.Core.Agents;

/// <summary>A pinned native source, not consent or execution authority.</summary>
public interface IAgentProfileSelection : IDisposable
{
    /// <summary>Gets exact source metadata.</summary>
    LocalFileMetadata Metadata { get; }
    /// <summary>Reads and revalidates the original native identity and ownership.</summary>
    Task<byte[]> ReadAsync(CancellationToken token);
}
