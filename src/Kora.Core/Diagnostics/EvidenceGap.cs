using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public sealed record EvidenceGap(
    DateTimeOffset ObservedUtc, string Sink, EvidenceGapReason Reason,
    HostId<EvidenceIdentity>? EvidenceId, string? ExceptionType);
