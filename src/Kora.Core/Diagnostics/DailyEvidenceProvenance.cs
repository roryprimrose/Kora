using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public sealed record DailyEvidenceProvenance(
    string FileName, string FileIdentity, long ByteOffset, string RecordDigest,
    HostId<EvidenceIdentity> SourceEvidenceId);
