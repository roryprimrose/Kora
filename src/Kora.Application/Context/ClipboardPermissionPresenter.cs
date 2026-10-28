using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Context;

public delegate Task<HostInteractionDecision> ClipboardPermissionPresenter(
    HostOperationProposal proposal, HostRevision generation, Func<bool> current, CancellationToken token);
