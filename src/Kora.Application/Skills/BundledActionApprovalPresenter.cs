using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Skills;

public delegate Task<HostInteractionDecision> BundledActionApprovalPresenter(
    HostOperationProposal proposal, HostRevision generation, Func<bool> admitted, CancellationToken token);