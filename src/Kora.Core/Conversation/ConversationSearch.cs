using Kora.Core.Storage;

namespace Kora.Core.Conversation;

/// <summary>Passive lexical matching of original USER content and typed HOST status, not receipt history.</summary>
public sealed class ConversationSearch
{
    private readonly SessionHistorySearch lexical;
    public ConversationSearch(string query, ConversationSearchRole role = ConversationSearchRole.All)
    {
        if (!Enum.IsDefined(role)) { throw new ArgumentOutOfRangeException(nameof(role)); }
        lexical = new(query);
        Role = role;
    }

    public ConversationSearchRole Role { get; }
    public string Digest => lexical.Digest;

    public bool Matches(ConversationMessage message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Fields(message).Any(field => lexical.MatchesText(field, token));
    }

    private IEnumerable<string> Fields(ConversationMessage message)
    {
        if (Role is not ConversationSearchRole.HOST) { yield return message.Body; }
        if (Role is ConversationSearchRole.USER) { yield break; }
        if (message.Outcome is { } outcome) { yield return outcome.ToString(); }
        if (message.Reason is { } reason) { yield return reason.ToString(); }
        yield return message.ExpectedTaskState.ToString();
    }
}
