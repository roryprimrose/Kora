using Kora.Core.Conversation;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public ConversationMessage? RequestExpiredMessage { get; init; }
}
