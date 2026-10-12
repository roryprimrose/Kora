using Kora.Core.Authorization;

namespace Kora.Core.Conversation;

public sealed record ConversationDraftSnapshot(WorkSessionAuthorization Session, SavedConversationDraft Draft);
