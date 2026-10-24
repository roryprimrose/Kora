using Kora.Core.Authorization;
using Kora.Core.Hosting;

namespace Kora.Application.Mcp;

public delegate Task<McpApprovalPresentation> McpApprovalPresenter(HostOperationProposal proposal,
    HostRevision generation, McpReview review, Func<bool> admitted, CancellationToken token);
