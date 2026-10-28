using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

public sealed record ApplicationLifecycleWork(SessionWorkspaceEntry Session, HostRequest Request,
    ConversationExecutionPhase? Execution, ConversationPendingPhase? Pending, string Description);
