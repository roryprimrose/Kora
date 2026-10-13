using Kora.Core.Authorization;
using Kora.Core.Storage;

namespace Kora.Core.Conversation;

public sealed record ConversationDraftSnapshot(WorkSessionAuthorization Session, SavedConversationDraft Draft)
{
    /// <summary>Optional exact native target reviewed for this write, checked in the draft transaction.</summary>
    public SessionWorkspaceEntry? ExpectedTarget { get; init; }
}
