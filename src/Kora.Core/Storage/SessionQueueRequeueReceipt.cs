using Kora.Core.Hosting;

namespace Kora.Core.Storage;

/// <summary>Typed original/fresh lineage retained by the authoritative audit, never a replay token.</summary>
public sealed record SessionQueueRequeueReceipt(Guid ReviewId, long QueueRevision, SessionQueueDispatchControl Control,
    SessionQueueEntry Original, HostTaskRecord OriginalTask, SessionQueueEntry Fresh, long ConfigurationRevision)
{
    public void Validate()
    {
        if (ReviewId == Guid.Empty || QueueRevision < 0 || Control is null || Original is null
            || OriginalTask is null || Fresh is null || ConfigurationRevision < 0)
        {
            throw new InvalidDataException("The expired replacement receipt is incomplete.");
        }
        Original.Validate();
        Fresh.Validate();
        try
        {
            SessionQueueRequeue.RequireExpired(Original, OriginalTask, Control.RunId, Control.Generation,
                Fresh.AdmissionRevision, Fresh.EnqueuedAt);
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidDataException("The retained expired replacement source is not valid original pending authority.", exception);
        }
        if (!Fresh.IsPending || Fresh.DispatchOrder != 0 || Fresh.RunId != Original.RunId
            || Fresh.Generation != Original.Generation || Fresh.Request.SessionId != Original.Request.SessionId
            || Fresh.Request.TaskId == Original.Request.TaskId || Fresh.Request.RequestId == Original.Request.RequestId
            || Fresh.Dependency != Original.Dependency || Fresh.ReadProfile != Original.ReadProfile || Fresh.Target != Original.Target
            || Fresh.EnqueuedAt.Offset != TimeSpan.Zero || Fresh.ExpiresAt.Offset != TimeSpan.Zero
            || Fresh.Position <= Original.Position || Fresh.Revision.Value != 1 || Control.Held)
        {
            throw new InvalidDataException("The expired replacement receipt does not bind a separate fresh pending decision.");
        }
    }
}
