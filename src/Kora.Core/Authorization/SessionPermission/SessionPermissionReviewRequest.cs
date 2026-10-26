namespace Kora.Core.Authorization.SessionPermission;

public sealed record SessionPermissionReviewRequest(
    Guid Id, HostOperationProposal Proposal, SessionPermissionPolicy Policy)
{
    public int ProtocolVersion => 1;
    public bool ToolsEnabled => false;
    public bool ApprovalCallbacksEnabled => false;
    public bool ConversationContextEnabled => false;
    public bool HostedFallbackEnabled => false;
}
