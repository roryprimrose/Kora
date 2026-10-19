using Kora.Core.Hosting;

namespace Kora.Application.Hosting;

/// <summary>IDs identify this new attempt even if its commit receipt is unavailable. Never retry automatically.</summary>
public sealed record SessionDiagnosticsPlanItem(SessionQueueReadProfile Profile, SessionDiagnosticsPlanItemState State,
    HostRequest? Request = null, SessionQueueEntry? CommittedEntry = null, long? CommittedQueueRevision = null);
