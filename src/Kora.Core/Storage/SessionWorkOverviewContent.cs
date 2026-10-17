namespace Kora.Core.Storage;

/// <summary>Positive predicates over retained observed metadata only; no absence or authority inference.</summary>
public enum SessionWorkOverviewContent
{
    Any,
    QueueReceipt,
    Task,
    PendingQuestion
}
