namespace Kora.Core.Diagnostics;

/// <summary>Admits and snapshots ordinary evidence before deferred persistence.</summary>
public interface IDeferredEvidenceSink : IEvidenceSink
{
    /// <summary>
    /// Validates call-time authority and captures an owned snapshot. The returned write must not
    /// depend on ambient context, retain caller-owned mutable state, or confer audit authority.
    /// </summary>
    Action PrepareDiagnostic(DiagnosticEnvelope envelope);

    /// <summary>Captures an owned completed-activity snapshot for persistence without ambient context.</summary>
    Action PrepareActivity(CompletedActivityEnvelope envelope);
}
