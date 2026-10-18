using System.Collections.Immutable;

using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

public enum SessionDiagnosticsPlanItemState { NotAttempted, Accepted, Conflict, Unavailable, Cancelled }

/// <summary>IDs identify this new attempt even if its commit receipt is unavailable. Never retry automatically.</summary>
public sealed record SessionDiagnosticsPlanItem(SessionQueueReadProfile Profile, SessionDiagnosticsPlanItemState State,
    HostRequest? Request = null, SessionQueueEntry? CommittedEntry = null, long? CommittedQueueRevision = null);

public sealed record SessionDiagnosticsPlanOutcome(Guid ReviewId, ImmutableArray<SessionDiagnosticsPlanItem> Items,
    SessionDiagnosticsPlanItemState? StoppedBy);
