using Kora.Core.Context;

namespace Kora.Core.KnowledgeSources;

/// <summary>Owns every retained native directory/file identity until review is completed or revoked.</summary>
public interface IKnowledgeSourceSelection : IDisposable
{
    /// <summary>Gets the complete immutable inventory.</summary>
    KnowledgeSourceMetadata Metadata { get; }
    /// <summary>Revalidates all retained identities and exact complete membership.</summary>
    Task ValidateAsync(CancellationToken cancellationToken);
    /// <summary>Reads one exact reviewed item once, without path fallback.</summary>
    Task<byte[]> ReadAsync(LocalFileMetadata exactItem, CancellationToken cancellationToken);
}
