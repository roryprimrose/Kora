namespace Kora.Core.KnowledgeSources;

public sealed record KnowledgeSource(Guid Id, long Version, string Name, KnowledgeSourceMetadata Folder,
    KnowledgeSourceState State, DateTimeOffset ChangedAt, int StoredBytes);
