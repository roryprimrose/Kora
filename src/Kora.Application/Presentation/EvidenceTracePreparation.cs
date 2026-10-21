using Kora.Core.Diagnostics;

namespace Kora.Application.Presentation;

/// <summary>A native-only, original-input-bound proposal, not query execution authority.</summary>
public sealed class EvidenceTracePreparation
{
    private readonly Func<bool> isCurrent;

    public EvidenceTracePreparation(string traceId, Func<bool> isCurrent)
    {
        if (!TraceSnapshot.IsValidW3CId(traceId, 32))
        {
            throw new ArgumentException("An exact W3C trace ID is required.", nameof(traceId));
        }
        TraceId = traceId;
        this.isCurrent = isCurrent;
    }

    public string TraceId { get; }
    public bool IsCurrent => isCurrent();
    public override string ToString() => nameof(EvidenceTracePreparation);
}
