using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Authorization;

/// <summary>A passive exact pending-operation observation, never admission or a dispatch token.</summary>
public sealed record PendingOperationExplanation(
    HostQuestionKey Question,
    HostRevision SessionGeneration,
    DateTimeOffset QuestionExpiresAt,
    HostOperationProposal Proposal,
    SessionPermissionPolicy SessionPolicy,
    HostAuthorizationPolicy Gates,
    OperationGrant? ApplicableGrant);
