namespace Kora.Core.Commands;

public sealed record SessionCommandSession(Guid Id, bool Active, long Generation, long? MetadataRevision, string? Name);
