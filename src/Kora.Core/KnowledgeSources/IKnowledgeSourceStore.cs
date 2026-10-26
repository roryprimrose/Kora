using Kora.Core.Context;

namespace Kora.Core.KnowledgeSources;

/// <summary>Owns atomic, profile-private named source publication and inventoried deletion.</summary>
public interface IKnowledgeSourceStore
{
    /// <summary>Lists bounded source metadata without reading original source content.</summary>
    Task<IReadOnlyList<KnowledgeSource>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Revokes the exact previous revision before any refresh work starts.</summary>
    Task<KnowledgeSource> BeginRefreshAsync(KnowledgeSource exact, CancellationToken cancellationToken);

    /// <summary>Atomically publishes all exact captured items, or leaves the source unavailable.</summary>
    Task<KnowledgeSource> PublishAsync(KnowledgeSource? expected, string name, LocalFolderRevision snapshot,
        IReadOnlyList<ReadOnlyMemory<byte>> originalBytes, CancellationToken cancellationToken);

    /// <summary>Resolves only an enabled exact durable revision, never original files.</summary>
    Task<LocalFolderRevision> ReadAsync(KnowledgeSource exact, CancellationToken cancellationToken);

    /// <summary>Revokes use without deleting retained copies.</summary>
    Task DisableAsync(KnowledgeSource exact, CancellationToken cancellationToken);

    /// <summary>Revokes first and removes only the exact owned inventory; uncertainty throws.</summary>
    Task RemoveAsync(KnowledgeSource exact, CancellationToken cancellationToken);
}
