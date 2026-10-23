using Kora.Core.Authorization;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Network;

/// <summary>Native host presents and commits a once-only exact operation; provider text cannot implement this port.</summary>
public delegate Task<WebPageApprovalPresentation> WebPageApprovalPresenter(
    HostOperationProposal proposal, HostRevision generation, Uri address,
    Func<bool> admitted, CancellationToken token);
