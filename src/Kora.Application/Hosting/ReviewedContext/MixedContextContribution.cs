using Kora.Core.Conversation;

namespace Kora.Application.Hosting.ReviewedContext;

internal sealed record MixedContextContribution(string Label, ConversationHistorySelection? History, Guid? MemoryId)
{
    internal bool CanOmit => History is not null || MemoryId is not null;
}
