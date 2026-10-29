using Kora.Core.Interaction;

namespace Kora.Application.Mcp;

public sealed record McpApprovalPresentation(HostQuestionRecord? Presented, HostInteractionDecision Completed);
