using System.Collections.Immutable;
using Kora.Core.Authorization;
using Kora.Core.Authorization.SessionPermission;
using Kora.Core.Hosting;

namespace Kora.Core.Interaction;

public sealed record HostInteractionSnapshot(
    HostTaskRecord Intent,
    WorkSessionAuthorization Session,
    HostAuthorizationPolicy Policy,
    HostOperationProposal? Proposal,
    ImmutableArray<HostQuestionRecord> Questions,
    ImmutableArray<OperationGrant> Grants)
{
    public SessionPermissionPolicy? Permission { get; init; }
    public SessionPermissionPolicy EffectivePermission => Permission ?? SessionPermissionPolicy.Default(Session);
    public ImmutableArray<SessionPermissionAdmission> Admissions { get; init; } = [];
}
