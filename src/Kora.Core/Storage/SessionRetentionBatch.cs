namespace Kora.Core.Storage;

public sealed record SessionRetentionBatch(int Archived, int Deleted, int Held, bool HasMore);
