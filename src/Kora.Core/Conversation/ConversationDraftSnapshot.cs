using Kora.Core.Authorization;
using Kora.Core.Storage;

namespace Kora.Core.Conversation;

public sealed record ConversationDraftSnapshot(WorkSessionAuthorization Session, SavedConversationDraft Draft)
{
    /// <summary>Optional exact native target bound to a legacy compatibility read or write.</summary>
    public SessionWorkspaceEntry? ExpectedTarget { get; init; }
}
