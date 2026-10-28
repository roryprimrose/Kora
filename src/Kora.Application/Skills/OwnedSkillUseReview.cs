using System.Diagnostics;
using Kora.Core.Skills;

namespace Kora.Application.Skills;

/// <summary>Host-issued volatile authority, never a property of the saved document.</summary>
public sealed class OwnedSkillUseReview : IDisposable
{
    private readonly Lock gate = new();
    private readonly OwnedSkillInvocationWorkflow owner;
    private readonly Func<bool> current;
    private readonly TimeProvider time;
    private readonly DateTimeOffset created;
    private OwnedDeclarativeSkillSnapshot? snapshot;
    private bool enabled;
    private bool consumed;

    internal OwnedSkillUseReview(OwnedSkillInvocationWorkflow owner, OwnedDeclarativeSkillSnapshot snapshot,
        Func<bool> current, TimeProvider time, ActivityLink cause)
    {
        this.owner = owner;
        this.snapshot = snapshot;
        this.current = current;
        this.time = time;
        created = time.GetUtcNow();
        ExpiresAt = created.AddMinutes(2);
        Cause = cause;
    }

    internal ActivityLink Cause { get; }
    public IReadOnlyList<string> RequiredNames
    {
        get { lock (gate) { return Require(owner).Document.InputTemplate?.RequiredNames.ToArray() ?? []; } }
    }
    public OwnedSkillToolSelection Tool
    {
        get { lock (gate) { return snapshot?.Document.Tool ?? throw new InvalidOperationException("Review the current source first."); } }
    }
    public DateTimeOffset ExpiresAt { get; }
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
    public string Text
    {
        get
        {
            lock (gate)
            {
                return IsCurrent
                    ? $"manifest.yaml\n{snapshot!.Document.ManifestText}\ninstructions.md\n{snapshot.Document.Guidance}"
                    : string.Empty;
            }
        }
    }

    internal OwnedDeclarativeSkillSnapshot Require(OwnedSkillInvocationWorkflow expected)
    {
        lock (gate)
        {
            if (!ReferenceEquals(owner, expected) || !IsCurrent)
            { throw new InvalidOperationException("This exact owned skill review expired or was revoked. Review again."); }
            return snapshot!;
        }
    }

    internal void Enable(OwnedSkillInvocationWorkflow expected)
    {
        lock (gate)
        {
            _ = Require(expected);
            if (enabled) { throw new InvalidOperationException("Enablement cannot renew an old or consumed review."); }
            enabled = true;
        }
    }

    internal void Consume(OwnedSkillInvocationWorkflow expected)
    {
        lock (gate)
        {
            _ = Require(expected);
            if (!enabled || consumed)
            { throw new InvalidOperationException("Separately enable this exact revision; one use cannot be replayed."); }
            consumed = true;
        }
    }

    public void Dispose() { lock (gate) { snapshot = null; enabled = false; } }
}
