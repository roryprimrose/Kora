using System.Collections.Immutable;
using System.Text.Json;

using Kora.Core.Authorization;

namespace Kora.Core.Conversation;

public sealed record ConversationPage(WorkSessionAuthorization Session, long HeadRevision, long Ceiling,
    ImmutableArray<ConversationMessage> Messages, ConversationCursor? Next)
{
    public static byte[] Serialize(ConversationPage page) => JsonSerializer.SerializeToUtf8Bytes(page);
}
