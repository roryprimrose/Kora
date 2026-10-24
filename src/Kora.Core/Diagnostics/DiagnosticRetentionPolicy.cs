using Kora.Core.Configuration;

namespace Kora.Core.Diagnostics;

/// <summary>A coherent future-commit policy snapshot. It grants no execution or pruning authority.</summary>
public sealed class DiagnosticRetentionPolicy
{
    private readonly Lock gate = new();
    private DiagnosticRetentionDays? effective;
    private long revision;

    public DiagnosticRetentionDays? Effective { get { lock (gate) { return effective; } } }
    public long Revision { get { lock (gate) { return revision; } } }
    public bool IsCurrent(DiagnosticRetentionDays days, long expectedRevision)
    {
        lock (gate) { return effective == days && revision == expectedRevision; }
    }

    public void Activate(DiagnosticRetentionDays days)
    {
        // A default(struct) must not bypass the domain constructor.
        var validated = new DiagnosticRetentionDays(days.Days);
        lock (gate) { effective = validated; revision = checked(revision + 1); }
    }

    public void HoldUnavailable() { lock (gate) { effective = null; revision = checked(revision + 1); } }

    public DateTimeOffset Due(DateTimeOffset committedUtc)
    {
        lock (gate)
        {
            return committedUtc.ToUniversalTime().AddDays((effective
                ?? throw new DiagnosticRetentionUnavailableException()).Days);
        }
    }
}
