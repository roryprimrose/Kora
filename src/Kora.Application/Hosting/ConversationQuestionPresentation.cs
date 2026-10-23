using Kora.Core.Interaction;

namespace Kora.Application.Hosting;

public sealed record ConversationQuestionPresentation(HostQuestionRecord Presented, HostInteractionDecision Completed);
