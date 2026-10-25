using Kora.Core.Hosting;

namespace Kora.Core.Authorization.SessionPermission;

// The host compares this receipt with its committed copy; caller-created records confer no authority.
public sealed record SessionPermissionAdmission(
    Guid Id, HostOperationProposal Proposal, SessionPermissionPolicy Policy, SessionPermissionBasis Basis,
    HostId<ApprovalIdentity>? GrantId = null, HostRevision? GrantRevision = null, bool Dispatched = false,
    SessionPermissionReviewResult? Review = null);
