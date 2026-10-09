namespace Kora.Core.Context;

/// <summary>Identifies one exact immutable reviewed folder revision, never a current path.</summary>
public sealed record LocalFolderReference(Guid SourceId, Guid RevisionId);
