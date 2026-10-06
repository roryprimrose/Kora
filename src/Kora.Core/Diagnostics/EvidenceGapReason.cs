using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public enum EvidenceGapReason { StorageNotAdmitted, SinkFailure, InvalidStructuredState, ExpiredTraceSegment, MissingHostContext }
