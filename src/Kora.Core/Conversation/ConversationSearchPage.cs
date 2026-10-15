using System.Collections.Immutable;
using System.Text.Json;

using Kora.Core.Authorization;

namespace Kora.Core.Conversation;

public sealed record ConversationSearchPage(WorkSessionAuthorization Session, long HeadRevision, long Ceiling, string QueryDigest,
    ImmutableArray<ConversationMessage> Messages, int Scanned, ConversationSearchCursor? Next,
    ConversationSearchRole Role = ConversationSearchRole.All)
{
    public const int DefaultRecords = 25;
    public int OmittedMessages => 0;
    public int UnavailableMessages => 0;
    public static byte[] Serialize(ConversationSearchPage page) => JsonSerializer.SerializeToUtf8Bytes(page);
}
