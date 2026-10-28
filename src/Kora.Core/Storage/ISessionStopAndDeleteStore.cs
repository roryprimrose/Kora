using Kora.Core.Conversation;

namespace Kora.Core.Storage;

/// <summary>Observes disposition impact allowing only the exact host-owned running conversation, without cancelling it.</summary>
public interface ISessionStopAndDeleteStore
{
    /// <summary>Captures the unchanged retained-data source and exact dispatched original; grants no mutation authority.</summary>
    ValueTask<SessionDispositionPreview> PreviewStopAndDeleteAsync(SessionWorkspaceEntry session,
        ConversationCancellationTarget running, CancellationToken cancellationToken);
}
