using Kora.Core.Configuration;

namespace Kora.Core.Diagnostics;

/// <summary>A future-commit snapshot shared by required authority audit and diagnostic audit projections, never grant authority.</summary>
public sealed class AuditRetentionPolicy
{
    private readonly Lock gate = new();
    private AuditRetentionDays? effective;

    public AuditRetentionDays? Effective { get { lock (gate) { return effective; } } }

    public void Activate(AuditRetentionDays days)
    {
        var validated = new AuditRetentionDays(days.Days);
        lock (gate) { effective = validated; }
    }

    public void HoldUnavailable() { lock (gate) { effective = null; } }

    public DateTimeOffset Due(DateTimeOffset committedUtc)
    {
        lock (gate)
        {
            return committedUtc.ToUniversalTime().AddDays((effective
                ?? throw new AuditRetentionUnavailableException()).Days);
        }
    }
}
