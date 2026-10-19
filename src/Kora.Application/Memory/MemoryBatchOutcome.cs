using System.Collections.Immutable;

namespace Kora.Application.Memory;

internal sealed record MemoryBatchOutcome(
    ImmutableArray<MemoryBatchItem> Items, MemoryBatchState? StoppedBy = null)
{
    internal const string Disclosure =
        "Sequential individual disables, not atomic-all. Earlier committed actions cannot be rolled back. "
        + "Unavailable may include an unknown commit; inspect durable state manually. No automatic retry.";
}
