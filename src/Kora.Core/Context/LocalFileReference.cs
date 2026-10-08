namespace Kora.Core.Context;

public sealed record LocalFileReference(Guid SourceId, Guid RevisionId, Guid ItemId, string Digest);
