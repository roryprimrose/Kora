using Kora.Core.Configuration;

namespace Kora.Core.Diagnostics;

/// <summary>A coherent future-commit policy snapshot. It grants no execution or pruning authority.</summary>
public sealed class DiagnosticRetentionPolicy
{
    private readonly Lock gate = new();
    private DiagnosticRetentionDays? effective;

    public DiagnosticRetentionDays? Effective { get { lock (gate) { return effective; } } }

    public void Activate(DiagnosticRetentionDays days)
    {
        // A default(struct) must not bypass the domain constructor.
        var validated = new DiagnosticRetentionDays(days.Days);
        lock (gate) { effective = validated; }
    }

    public void HoldUnavailable() { lock (gate) { effective = null; } }

    public DateTimeOffset Due(DateTimeOffset committedUtc)
    {
        lock (gate)
        {
            return committedUtc.ToUniversalTime().AddDays((effective
                ?? throw new DiagnosticRetentionUnavailableException()).Days);
        }
    }
}
