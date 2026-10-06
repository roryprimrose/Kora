using System.Collections.Immutable;
using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Core.Interaction;

public sealed record HostInteractionSnapshot(
    HostTaskRecord Intent,
    WorkSessionAuthorization Session,
    HostAuthorizationPolicy Policy,
    HostOperationProposal? Proposal,
    ImmutableArray<HostQuestionRecord> Questions,
    ImmutableArray<OperationGrant> Grants);
