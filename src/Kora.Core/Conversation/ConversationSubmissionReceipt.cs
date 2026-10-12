using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

public sealed record ConversationSubmissionReceipt(ConversationMessage Message, HostTaskRecord Task);
