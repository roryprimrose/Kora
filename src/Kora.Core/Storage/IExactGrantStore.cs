using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Storage;

/// <summary>Bounded passive exact-grant metadata and one serializable, audited original-user revocation.</summary>
public interface IExactGrantStore
{
    /// <summary>Reads at most 50 records/64 KiB without refreshing activity or creating intent.</summary>
    ValueTask<ExactGrantPage> ReadExactGrantPageAsync(ExactGrantCursor? cursor, CancellationToken cancellationToken);
    /// <summary>Reads one exact current retained record, or reports absence without fabricating applicability.</summary>
    ValueTask<ExactGrantInspection?> InspectExactGrantAsync(HostId<ApprovalIdentity> id, CancellationToken cancellationToken);
    /// <summary>Creates a fresh host-owned control session, never adopting a grant's original request or session.</summary>
    ValueTask<WorkSessionAuthorization> CreateExactGrantControlSessionAsync(
        HostRequest request, Func<bool> admitted, CancellationToken cancellationToken);
    /// <summary>Commits only the unchanged preview's revocation and correlated requested/terminal audit.</summary>
    ValueTask<HostInteractionDecision> RevokeExactGrantAsync(HostRequest request, HostRevision controlGeneration,
        ExactGrantInspection preview, SecurityAuditEvent requested, Func<bool> admitted, CancellationToken cancellationToken);
}
