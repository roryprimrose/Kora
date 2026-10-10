namespace Kora.Core.Authorization;

/// <summary>A continuation of one initialized private store and unchanged authority snapshot; not authorization.</summary>
public sealed record ExactGrantCursor(string StoreIdentity, long AuditSequence, string AuditHash, Guid After);
