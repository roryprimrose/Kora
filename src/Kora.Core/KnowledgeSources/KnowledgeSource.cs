using Kora.Core.Context;

namespace Kora.Core.KnowledgeSources;

public sealed record KnowledgeSource(Guid Id, long Version, string Name, LocalFolderMetadata Folder,
    KnowledgeSourceState State, DateTimeOffset ChangedAt, int StoredBytes);
