using Kora.Core.Diagnostics;

namespace Kora.Application.Configuration;

public sealed record DiagnosticImpactPreviewState(string Status, int? ProposedDays = null,
    DateTimeOffset? CutoffUtc = null, bool Scanning = false, DiagnosticImpactSnapshot? Result = null);
