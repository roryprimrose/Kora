using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

internal sealed record ConversationExecutionBudget(ConversationExecutionLimit Limit, long Revision, bool Saved);
