using System.Diagnostics;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Core.Diagnostics;

public sealed record EvidenceValue(EvidenceValueKind Kind, string? CanonicalValue);
