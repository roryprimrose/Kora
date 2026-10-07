namespace Kora.Core.Diagnostics;

public sealed record AuthorityAuditChange(string Kind, string Id, long Revision, string Digest);
