using Kora.Core.Diagnostics;

namespace Kora.Application.Configuration;

public sealed record AuditImpactPreviewState(string Status, int? ProposedDays = null,
    DateTimeOffset? CutoffUtc = null, AuditImpactResult? RequiredAuthority = null,
    AuditImpactResult? DiagnosticProjection = null);
