using Kora.Core.Commands;
using Kora.Core.Hosting;
using Kora.Core.Memory;

namespace Kora.Application.Memory;

/// <summary>Shared bounded selection, complete-record validation and exact transition expectations.</summary>
internal static class MemoryBatchPolicy
{
    internal static void ValidateOperation(MemoryBatchOperation operation)
    {
        if (operation is not (MemoryBatchOperation.Disable or MemoryBatchOperation.Forget))
        {
            throw new InvalidDataException("Only disable and forget are available for selected subsets.");
        }
    }

    internal static bool Eligible(MemoryBatchOperation operation, MemorySummary row) =>
        row.Review == MemoryReviewState.Admitted
        && (row.Retention == MemoryRetentionState.Enabled
            || operation == MemoryBatchOperation.Forget && row.Retention == MemoryRetentionState.Disabled);

    internal static MemorySummary Summary(MemoryRecord record) =>
        new(record.Id.Value, record.Revision.Value, record.Scope, record.Review, record.Retention, record.CreatedAt);

    internal static void ValidateRecord(MemoryBatchOperation operation, HostId<SessionIdentity> session,
        MemorySummary row, MemoryRecord record)
    {
        if (record is null || Summary(record) != row || record.Revision.Value == long.MaxValue
            || !Eligible(operation, row) || record.Lineage is null || record.Lineage.Request is null
            || record.Lineage.Request.SessionId != session || record.Receipt is null
            || MemoryPolicy.Eligible(record with { Retention = MemoryRetentionState.Enabled },
                record.Receipt.Boundary, MemoryDestination.Local) != MemoryReason.None)
        {
            throw new InvalidDataException("The complete selected content, classification or reviewed lineage is invalid.");
        }
    }

    internal static MemoryRecord Expected(MemoryBatchOperation operation, MemoryRecord record) =>
        operation switch
        {
            MemoryBatchOperation.Disable => record with
            {
                Revision = new(checked(record.Revision.Value + 1)), Retention = MemoryRetentionState.Disabled,
            },
            MemoryBatchOperation.Forget => record with
            {
                Revision = new(checked(record.Revision.Value + 1)), Retention = MemoryRetentionState.Forgotten,
                Candidate = null, Receipt = null,
            },
            _ => throw new InvalidDataException("Unsupported selected-subset operation."),
        };

    internal static string Disclosure(MemoryBatchOperation operation) => operation == MemoryBatchOperation.Disable
        ? MemoryBatchOutcome.Disclosure
        : "Sequential individual forget actions, not atomic-all. Irreversible content removal retains a content-free, "
            + "non-reusable identity tombstone. Earlier committed actions cannot be rolled back. "
            + "This is logical application content removal, not physical or forensic erasure of database pages, "
            + "backups or copies. Unavailable may include an unknown commit; inspect durable state manually. No automatic retry.";
}
