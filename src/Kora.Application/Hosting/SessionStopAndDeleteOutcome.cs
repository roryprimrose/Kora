namespace Kora.Application.Hosting;

/// <summary>Truthful compound outcomes; stopping is not rollback or deletion.</summary>
public enum SessionStopAndDeleteOutcome
{
    /// <summary>No stop was accepted and no deletion was committed.</summary>
    NotStopped,
    /// <summary>Stop was requested but physical completion is unconfirmed. Deletion is forbidden.</summary>
    StopUnconfirmed,
    /// <summary>Owned work physically joined, but deletion was not attempted. A new review is required.</summary>
    WorkStoppedNotDeleted,
    /// <summary>Work joined, but deletion commit/readback is uncertain. Inspect receipts without replay.</summary>
    DeletionUnconfirmed,
    /// <summary>The existing logical disposition transaction returned its committed receipt.</summary>
    Deleted,
}
