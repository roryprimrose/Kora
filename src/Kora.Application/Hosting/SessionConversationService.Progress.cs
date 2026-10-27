using Kora.Core.Conversation;

namespace Kora.Application.Hosting;

public sealed partial class SessionConversationService
{
    /// <summary>Revalidates a volatile host step immediately before deferred native disclosure.</summary>
    public bool IsExecutionProgressCurrent(ConversationExecutionStatus observation)
    {
        lock (cancellationGate)
        {
            return observation.Phase is ConversationExecutionPhase.Waiting or ConversationExecutionPhase.Running
                && runningConversations.TryGetValue(observation.Target.Request.SessionId, out var running)
                && running.Status == observation && running.CanContinue && !running.Token.IsCancellationRequested;
        }
    }
}
