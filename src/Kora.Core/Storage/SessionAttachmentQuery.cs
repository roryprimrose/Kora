namespace Kora.Core.Storage;

/// <summary>A query over retained slots, optionally including content-free removal provenance.</summary>
public sealed record SessionAttachmentQuery(bool IncludeRemoved = false);
