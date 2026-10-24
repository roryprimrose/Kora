using System.Diagnostics;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>A volatile, exact-source, separately enabled, single-use review.</summary>
public sealed class SharedSkillUseReview : IDisposable
{
    private readonly Lock gate = new();
    private readonly SharedSkillInvocationWorkflow owner;
    private readonly Func<bool> current;
    private readonly TimeProvider time;
    private readonly DateTimeOffset created;
    private SharedSkillSnapshot? snapshot;
    private bool enabled;
    private bool consumed;

    internal SharedSkillUseReview(SharedSkillInvocationWorkflow owner, SharedSkillSource source,
        SharedSkillSnapshot snapshot, Func<bool> current, TimeProvider time, ActivityLink cause)
    {
        this.owner = owner;
        Source = source;
        this.snapshot = snapshot;
        this.current = current;
        this.time = time;
        created = time.GetUtcNow();
        ExpiresAt = created.AddMinutes(2);
        Cause = cause;
    }

    internal SharedSkillSource Source { get; }
    internal ActivityLink Cause { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string Text
    {
        get
        {
            lock (gate)
            {
                if (!IsCurrent) { return string.Empty; }
                return snapshot!.ReferenceBundle is not { } bundle ? snapshot.Text!
                    : $"PRIMARY {snapshot.RelativeFile}\n{snapshot.Text}\n\n"
                        + string.Join("\n\n", bundle.References.Select(reference =>
                            $"REFERENCE {reference.RelativePath}\n{reference.Text}"));
            }
        }
    }
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

    internal SharedSkillSnapshot Require(SharedSkillInvocationWorkflow expected)
    {
        lock (gate)
        {
            if (!ReferenceEquals(owner, expected) || !IsCurrent)
            { throw new InvalidOperationException("This exact skill review expired or was revoked. Review and enable again."); }
            return snapshot!;
        }
    }

    internal void Enable(SharedSkillInvocationWorkflow expected)
    {
        lock (gate)
        {
            _ = Require(expected);
            if (enabled) { throw new InvalidOperationException("Enablement cannot revive an old or consumed review."); }
            enabled = true;
        }
    }

    internal void Consume(SharedSkillInvocationWorkflow expected)
    {
        lock (gate)
        {
            _ = Require(expected);
            if (!enabled || consumed) { throw new InvalidOperationException("Separately enable this exact revision; an invocation cannot be replayed."); }
            consumed = true;
        }
    }

    public void Dispose()
    {
        lock (gate) { snapshot = null; enabled = false; }
    }
}
