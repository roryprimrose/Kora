namespace Kora.Core.Storage;

/// <summary>Volatile query-bound continuation over an exact host history snapshot.</summary>
public sealed record SessionHistorySearchCursor(SessionHistoryCursor History, string QueryDigest);
