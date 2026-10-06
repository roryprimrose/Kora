using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public interface IEvidenceSink
{
    string Name { get; }
    void WriteDiagnostic(DiagnosticEnvelope envelope);
    void WriteAudit(AuditEnvelope envelope);
    void WriteActivity(CompletedActivityEnvelope envelope);
}
