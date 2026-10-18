using System.Collections.Immutable;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

internal sealed class MemoryBatchDisableReview(
    HostId<SessionIdentity> session, ImmutableArray<MemoryRecord> records)
{
    internal HostId<SessionIdentity> Session { get; } = session;
    internal ImmutableArray<MemoryRecord> Records { get; } = records;
}

/// <summary>Truthful per-record outcomes; Succeeded is the individual committed transition, not batch atomicity.</summary>
internal enum MemoryBatchDisableState
{
    /// <summary>The individual action returned its committed success.</summary>
    Succeeded,
    /// <summary>The exact reviewed record changed or its transition conflicted.</summary>
    Conflict,
    /// <summary>Admission or a confirmed commit result is unavailable; inspect durable state manually.</summary>
    Unavailable,
    /// <summary>Cancellation preceded the disable call, or the authoritative action returned Cancelled.</summary>
    Cancelled,
    /// <summary>No disable call was made for this record.</summary>
    NotAttempted,
}

internal sealed record MemoryBatchDisableItem(
    Guid Id, long ReviewedRevision, MemoryBatchDisableState State, MemoryResult? ActionResult = null);

internal sealed record MemoryBatchDisableOutcome(
    ImmutableArray<MemoryBatchDisableItem> Items, MemoryBatchDisableState? StoppedBy = null)
{
    internal const string Disclosure =
        "Sequential individual disables, not atomic-all. Earlier committed actions cannot be rolled back. "
        + "Unavailable may include an unknown commit; inspect durable state manually. No automatic retry.";
}

internal sealed class MemoryBatchDisableConflictException()
    : InvalidOperationException("The exact selected memory revision changed. List and review again.")
{
}
