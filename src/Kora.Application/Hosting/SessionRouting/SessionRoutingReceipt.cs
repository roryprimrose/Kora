using Kora.Core.Conversation;
using Kora.Core.Storage;

namespace Kora.Application.Hosting.SessionRouting;

public sealed record SessionRoutingReceipt(SessionWorkspaceEntry Target, ConversationSubmissionReceipt Submission);
