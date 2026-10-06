using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public interface IEvidenceGapReporter
{
    void Report(EvidenceGap gap);
}
