using System.Collections.Immutable;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Single-use host review. Disposing releases only this attempt's reversible dispatch barrier.</summary>
public sealed class ApplicationLifecycleReview : IDisposable
{
    private readonly Action<ApplicationLifecycleReview> release;
    internal readonly Func<bool> SourceCurrent;
    internal readonly long ControlRevision;
    internal readonly long WorkRevision;
    internal readonly ImmutableArray<ConversationExecutionStatus> Running;
    internal readonly ImmutableArray<ConversationPendingSnapshot> Pending;
    internal bool Consumed;
    internal bool Joining;
    internal bool ReleasePending;
    internal ApplicationLifecycleReview(ApplicationLifecycleOperation operation, RequestOrigin origin,
        long controlRevision, long workRevision, DateTimeOffset expiresAt, Func<bool> sourceCurrent,
        ImmutableArray<ApplicationLifecycleWork> work, ImmutableArray<ConversationExecutionStatus> running,
        ImmutableArray<ConversationPendingSnapshot> pending, Action<ApplicationLifecycleReview> release)
    {
        Operation = operation;
        Origin = origin;
        ControlRevision = controlRevision;
        WorkRevision = workRevision;
        ExpiresAt = expiresAt;
        SourceCurrent = sourceCurrent;
        Work = work;
        Running = running;
        Pending = pending;
        this.release = release;
    }

    public ApplicationLifecycleOperation Operation { get; }
    public RequestOrigin Origin { get; }
    public DateTimeOffset ExpiresAt { get; }
    public ImmutableArray<ApplicationLifecycleWork> Work { get; }
    public bool Joined { get; internal set; }
    public bool StopAccepted => Consumed;
    public ImmutableArray<AllWorkStopResult.Original> Results { get; internal set; } = [];
    public void Dispose() => release(this);
}
