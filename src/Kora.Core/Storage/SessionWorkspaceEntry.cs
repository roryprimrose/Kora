using Kora.Core.Authorization;

namespace Kora.Core.Storage;

/// <summary>Null metadata means an unnamed host session, not missing authority.</summary>
public sealed record SessionWorkspaceEntry(WorkSessionAuthorization Authority, SessionMetadata? Metadata);
