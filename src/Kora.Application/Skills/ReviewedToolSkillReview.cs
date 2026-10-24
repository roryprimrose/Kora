using System.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Skills;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

/// <summary>A volatile host-issued review, not source consent, model consent or action permission.</summary>
public sealed class ReviewedToolSkillReview : IDisposable
{
    private readonly Lock gate = new();
    private readonly ReviewedToolSkillInvocationWorkflow owner;
    private readonly Func<bool> current;
    private readonly TimeProvider time;
    private readonly DateTimeOffset created;
    private SharedSkillSnapshot? snapshot;
    private bool enabled;
    private bool consumed;

    internal ReviewedToolSkillReview(ReviewedToolSkillInvocationWorkflow owner, SharedSkillSource source,
        SharedSkillSnapshot snapshot, SessionWorkspaceEntry target, RequestOrigin origin,
        Func<bool> current, TimeProvider time, ActivityLink cause)
    {
        this.owner = owner;
        Source = source;
        this.snapshot = snapshot;
        Target = target;
        Origin = origin;
        this.current = current;
        this.time = time;
        created = time.GetUtcNow();
        ExpiresAt = created.AddMinutes(2);
        Cause = cause;
    }

    internal SharedSkillSource Source { get; }
    internal ActivityLink Cause { get; }
    public SessionWorkspaceEntry Target { get; }
    public RequestOrigin Origin { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string Text { get { lock (gate) { return IsCurrent ? snapshot!.Text! : string.Empty; } } }
    public bool IsCurrent
    {
        get
        {
            lock (gate)
            {
                if (snapshot is null) { return false; }
                if (!current() || time.GetUtcNow() < created || time.GetUtcNow() >= ExpiresAt)
                { Dispose(); return false; }
                return true;
            }
        }
    }
    public bool IsEnabled { get { lock (gate) { return IsCurrent && enabled && !consumed; } } }

    internal SharedSkillSnapshot Require(ReviewedToolSkillInvocationWorkflow expected, RequestOrigin origin)
    {
        lock (gate)
        {
            if (!ReferenceEquals(owner, expected) || origin != Origin || !IsCurrent)
            { throw new InvalidOperationException("The exact source, target and original-channel review is no longer admitted."); }
            return snapshot!;
        }
    }

    internal void Enable(ReviewedToolSkillInvocationWorkflow expected, RequestOrigin origin)
    {
        lock (gate)
        {
            _ = Require(expected, origin);
            if (enabled) { throw new InvalidOperationException("This review cannot be reenabled or replayed."); }
            enabled = true;
        }
    }

    internal void Consume(ReviewedToolSkillInvocationWorkflow expected, RequestOrigin origin)
    {
        lock (gate)
        {
            _ = Require(expected, origin);
            if (!enabled || consumed)
            { throw new InvalidOperationException("Separately enable this exact tool-capable revision before one invocation."); }
            consumed = true;
        }
    }

    public void Dispose()
    {
        lock (gate) { snapshot = null; enabled = false; }
    }
}
