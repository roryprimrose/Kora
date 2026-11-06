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
    private ReviewedToolSkillSource? source;
    private bool enabled;
    private bool consumed;

    internal ReviewedToolSkillReview(ReviewedToolSkillInvocationWorkflow owner, ReviewedToolSkillSource source,
        SessionWorkspaceEntry target, RequestOrigin origin,
        Func<bool> current, TimeProvider time, ActivityLink cause)
    {
        this.owner = owner;
        this.source = source;
        Target = target;
        Origin = origin;
        this.current = current;
        this.time = time;
        created = time.GetUtcNow();
        ExpiresAt = created.AddMinutes(2);
        Cause = cause;
    }

    internal ActivityLink Cause { get; }
    public SessionWorkspaceEntry Target { get; }
    public RequestOrigin Origin { get; }
    public DeclarativeMcpToolReference? McpReference
    {
        get { lock (gate) { return IsCurrent && source is SharedToolSkillSource shared ? shared.Snapshot.McpReference : null; } }
    }
    public DateTimeOffset ExpiresAt { get; }
    public string Text { get { lock (gate) { return IsCurrent ? source!.Text : string.Empty; } } }
    public string SourceDescription { get { lock (gate) { return IsCurrent
        ? source is OwnedToolSkillSource ? "Kora-owned private package" : "Registered shared skill"
        : string.Empty; } } }
    public bool IsCurrent
    {
        get
        {
            lock (gate)
            {
                if (source is null) { return false; }
                if (!current() || !source.IsCurrent || time.GetUtcNow() < created || time.GetUtcNow() >= ExpiresAt)
                { Dispose(); return false; }
                return true;
            }
        }
    }
    public bool IsEnabled { get { lock (gate) { return IsCurrent && enabled && !consumed; } } }

    internal ReviewedToolSkillSource Require(ReviewedToolSkillInvocationWorkflow expected, RequestOrigin origin)
    {
        lock (gate)
        {
            if (!ReferenceEquals(owner, expected) || origin != Origin || !IsCurrent)
            { throw new InvalidOperationException("The exact source, target and original-channel review is no longer admitted."); }
            return source!;
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
        lock (gate) { source?.Dispose(); source = null; enabled = false; }
    }
}
