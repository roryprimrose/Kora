using Kora.Core.Conversation;

namespace Kora.Application.Hosting;

/// <summary>Host-owned one-shot playback of an exactly revalidated retained assistant answer.</summary>
public interface ISessionAnswerPlayback
{
    /// <summary>Reads the complete admitted body once, or returns a truthful unavailable outcome.</summary>
    /// <param name="source">The exact retained message containing a finalized assistant answer.</param>
    /// <param name="admitted">The original private source and input admission, never model authority.</param>
    /// <param name="validateSource">Re-reads the exact retained source immediately before speech admission.</param>
    /// <param name="token">Cancellation of this deliberate request.</param>
    Task<string> ReadAnswerOnceAsync(ConversationMessage source, Func<bool> admitted,
        Func<CancellationToken, Task> validateSource, CancellationToken token);
}
