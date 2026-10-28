using System.Collections.Immutable;

using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>Facts for one exact host review, never a continuing global cancellation grant.</summary>
public sealed record AllWorkStopResult(AllWorkStopResult.OutcomeKind Outcome,
    ImmutableArray<ApplicationLifecycleWork> Reviewed = default,
    ImmutableArray<AllWorkStopResult.Original> Originals = default,
    ImmutableArray<SessionQueueSnapshot> Compatibility = default,
    bool CompatibilityJoined = false,
    ImmutableArray<SessionQueueSnapshot> ReviewedCompatibility = default)
{
    public enum OutcomeKind { Completed, PartialUnconfirmed, Stale, Cancelled, NothingToStop, Unavailable, InProgress }
    public sealed record Original(HostRequest Request, bool StopAccepted, bool PhysicallyJoined, HostTaskState? TerminalState);

    public string Summary => Outcome switch
    {
        OutcomeKind.Completed => "The exact reviewed originals stopped or finished and physically joined, with terminal readback. Kora stays open. Existing lane holds still require fresh Resume.",
        OutcomeKind.PartialUnconfirmed => "Some stops may have been accepted. Stop, audit, retention, source or release remains unconfirmed; accepted resources remain owned until actual release. Inspect current work; do not assume all work stopped or retry old confirmation.",
        OutcomeKind.Stale => "The exact work or original private authority changed. This confirmation cannot authorize a new set; review again.",
        OutcomeKind.Cancelled => "Review cancelled. This attempt requested no stop and released only its own review hold.",
        OutcomeKind.NothingToStop => "The complete host review found no submitted work to stop at that observation. This is not a future all-clear.",
        OutcomeKind.Unavailable => "Stop all work is unavailable: an eligible original user and the private native review owner are required. No stop was requested.",
        OutcomeKind.InProgress => "A Stop All attempt is already reviewing or joining. No additional attempt was started.",
        _ => throw new InvalidOperationException("Unknown Stop All outcome."),
    };
}
