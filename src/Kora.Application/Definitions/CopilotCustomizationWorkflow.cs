using Kora.Application.Dependencies;
using Kora.Application.Hosting;
using Kora.Core.Auditing;
using Kora.Core.Conversation;
using Kora.Core.Customizations;
using Kora.Core.Hosting;
using Kora.Core.Providers;

namespace Kora.Application.Definitions;

/// <summary>Preserves the native Copilot selection API over the shared source/review/retention lifecycle.</summary>
public sealed class CopilotCustomizationWorkflow : IAsyncDisposable
{
    private readonly HostedCustomizationWorkflow workflow;

    public CopilotCustomizationWorkflow(CustomizationManagementService management, SessionWorkspaceService workspace,
        SessionConversationService conversation, IModelTurnExecution execution, ISecurityAuditLog audit, TimeProvider time) =>
        workflow = new(management, workspace, conversation, execution, audit, time, ProviderService.Copilot);

    public async Task<CopilotCustomizationReview> ReviewAsync(CustomizationSource source, string targetName, string body,
        RequestOrigin origin, Func<string, CancellationToken, Task<bool>>? authorizeRead,
        Func<bool> eligible, CancellationToken token) =>
        new(await workflow.ReviewAsync(source, targetName, body, origin, authorizeRead, eligible, token).ConfigureAwait(false));

    public async Task<CopilotCustomizationReview> ApproveHostedSourceAsync(Guid expectedReview, Func<bool> eligible, CancellationToken token) =>
        new(await workflow.ApproveHostedSourceAsync(expectedReview, eligible, token).ConfigureAwait(false));

    public Task<ConversationSubmissionReceipt> ConfirmAsync(Guid expectedReview, Func<bool> eligible, CancellationToken token) =>
        workflow.ConfirmAsync(expectedReview, eligible, token);

    public Task StopAsync() => workflow.StopAsync();
    public ValueTask DisposeAsync() => workflow.DisposeAsync();
}
