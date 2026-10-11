using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Storage;

/// <summary>Bounded passive exact-grant metadata and serializable, audited original-user revocation.</summary>
public interface IExactGrantStore
{
    /// <summary>Reads at most 50 records/64 KiB without refreshing activity or creating intent.</summary>
    ValueTask<ExactGrantPage> ReadExactGrantPageAsync(ExactGrantCursor? cursor, CancellationToken cancellationToken);
    /// <summary>Reads one bounded validated storage page under exact stored-metadata predicates; empty pages may have a continuation.</summary>
    ValueTask<ExactGrantPage> ReadExactGrantPageAsync(ExactGrantQuery query, ExactGrantCursor? cursor, CancellationToken cancellationToken);
    /// <summary>Reads one exact current retained record, or reports absence without fabricating applicability.</summary>
    ValueTask<ExactGrantInspection?> InspectExactGrantAsync(HostId<ApprovalIdentity> id, CancellationToken cancellationToken);
    /// <summary>Reads bounded committed exact-ID history for the current original native viewer, not the grant's origin.</summary>
    ValueTask<ExactGrantHistoryPage> ReadExactGrantHistoryAsync(ExactGrantInspection inspected, HostRequest viewer,
        string? cursor, Func<bool> admitted, CancellationToken cancellationToken);
    /// <summary>Creates a fresh host-owned control session, never adopting a grant's original request or session.</summary>
    ValueTask<WorkSessionAuthorization> CreateExactGrantControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken);
    /// <summary>Commits only the unchanged preview's revocation and correlated requested/terminal audit.</summary>
    ValueTask<HostInteractionDecision> RevokeExactGrantAsync(HostRequest request, HostRevision controlGeneration,
        ExactGrantInspection preview, SecurityAuditEvent requested, Func<bool> admitted, CancellationToken cancellationToken);
    /// <summary>Rereads every explicitly selected current inventory record as one complete bounded preview.</summary>
    ValueTask<ExactGrantBatch> PreviewExactGrantBatchAsync(ExactGrantBatch selection, CancellationToken cancellationToken);
    /// <summary>Commits all unchanged selected records and their typed audit atomically, or revokes none.</summary>
    ValueTask<ExactGrantBatchDecision> RevokeExactGrantBatchAsync(HostRequest request, HostRevision controlGeneration,
        ExactGrantBatch preview, SecurityAuditEvent requested, Func<bool> admitted, CancellationToken cancellationToken);
}