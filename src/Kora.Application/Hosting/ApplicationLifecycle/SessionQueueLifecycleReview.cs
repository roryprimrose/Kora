using System.Collections.Immutable;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed class SessionQueueLifecycleReview : IDisposable
{
    private readonly Action<SessionQueueLifecycleReview> release;
    internal readonly Func<bool> Current;
    internal readonly long Revision;
    internal readonly long Epoch;
    internal bool Consumed;
    internal bool Joining;
    internal bool ReleasePending;
    internal SessionQueueLifecycleReview(ImmutableArray<SessionQueueSnapshot> queues, long revision, long epoch,
        RequestOrigin origin, DateTimeOffset expiresAt, Func<bool> current, Action<SessionQueueLifecycleReview> release)
    { Queues = queues; Revision = revision; Epoch = epoch; Origin = origin; ExpiresAt = expiresAt; Current = current; this.release = release; }
    public ImmutableArray<SessionQueueSnapshot> Queues { get; }
    public RequestOrigin Origin { get; }
    public DateTimeOffset ExpiresAt { get; }
    public bool Joined { get; internal set; }
    public void Dispose() => release(this);
}
