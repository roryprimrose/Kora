namespace Kora.Core.Storage;

/// <summary>Catalogue disclosure projection; unavailable provenance has no retained digest.</summary>
public sealed record SessionAttachmentFileMetadata(Guid SourceId, Guid RevisionId, Guid ItemId, string? Digest);
