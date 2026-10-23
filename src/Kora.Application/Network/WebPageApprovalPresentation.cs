using Kora.Core.Interaction;

namespace Kora.Application.Network;

public sealed record WebPageApprovalPresentation(HostQuestionRecord Presented, HostInteractionDecision Completed);
