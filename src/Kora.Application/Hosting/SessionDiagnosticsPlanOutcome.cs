using System.Collections.Immutable;

namespace Kora.Application.Hosting;

public sealed record SessionDiagnosticsPlanOutcome(Guid ReviewId, ImmutableArray<SessionDiagnosticsPlanItem> Items,
    SessionDiagnosticsPlanItemState? StoppedBy);
