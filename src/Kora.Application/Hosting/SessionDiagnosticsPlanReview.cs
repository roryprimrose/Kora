using System.Collections.Immutable;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Inert exact review, not execution authority. Only the issuing service can consume it once.</summary>
public sealed class SessionDiagnosticsPlanReview
{
    internal SessionDiagnosticsPlanReview(SessionQueueSnapshot queue, ImmutableArray<SessionQueueReadProfile> profiles,
        long admissionRevision, SessionQueueLimits limits, long configurationRevision)
    {
        Queue = queue with { Entries = queue.Entries.ToImmutableArray(), Orders = queue.Orders.ToImmutableArray() };
        Profiles = profiles;
        AdmissionRevision = admissionRevision;
        Limits = limits;
        ConfigurationRevision = configurationRevision;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public SessionQueueSnapshot Queue { get; }
    public ImmutableArray<SessionQueueReadProfile> Profiles { get; }
    public long AdmissionRevision { get; }
    public SessionQueueLimits Limits { get; }
    public long ConfigurationRevision { get; }
    public SessionDiagnosticsPlanPrerequisite Prerequisite => SessionDiagnosticsPlanPrerequisite.None;
    public int ExistingPending => SessionQueueCapacity.PendingCount(Queue.Entries, Queue.DispatchControl!.RunId);
    public int ResultingPending => ExistingPending + Profiles.Length;

    public const string Disclosure = "Sequential existing single-item enqueues, NOT an atomic batch. Possibly partial: "
        + "stop at the first conflict, unavailable/uncertain result or cancellation; later items are NotAttempted. "
        + "Earlier committed pending items remain queued; no rollback or automatic retries. "
        + "None is the prerequisite for EVERY planned item, independent of any selected work row. "
        + "New host request/task IDs and original pending deadlines are captured separately at each enqueue; review reserves nothing. "
        + "Existing IDs, deadlines, pause, order and run controls are unchanged. "
        + "Dispatch remains a separate manual fair bounded action; no pump, model, network, provider, executor or action authority.";
}

public enum SessionDiagnosticsPlanPrerequisite { None }
