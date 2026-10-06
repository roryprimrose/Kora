using Kora.Core.Diagnostics;
using Kora.Core.Storage;

namespace Kora.Application.Diagnostics;

public sealed class UnavailableEvidenceSink : IEvidenceSink
{
    public string Name => "private-sqlite";
    public void WriteDiagnostic(DiagnosticEnvelope envelope) => throw new StorageAdmissionException();
    public void WriteAudit(AuditEnvelope envelope) => throw new StorageAdmissionException();
    public void WriteActivity(CompletedActivityEnvelope envelope) => throw new StorageAdmissionException();
}
