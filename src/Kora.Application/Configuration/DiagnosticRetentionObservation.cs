using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record DiagnosticRetentionObservation(DiagnosticRetentionState State, long PolicyRevision)
{
    public DiagnosticRetentionDays? Saved => State.Source switch
    {
        "default" => null,
        "saved" when State.Desired is { } days => days,
        _ => throw new InvalidDataException("Diagnostic retention has no confirmed provenance."),
    };
}
