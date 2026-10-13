using Kora.Core.Storage;

namespace Kora.Core.Conversation;

/// <summary>Passive lexical matching of original USER content and typed HOST status, not receipt history.</summary>
public sealed class ConversationSearch(string query)
{
    private readonly SessionHistorySearch lexical = new(query);
    public string Digest => lexical.Digest;

    public bool Matches(ConversationMessage message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Fields(message).Any(field => lexical.MatchesText(field, token));
    }

    private static IEnumerable<string> Fields(ConversationMessage message)
    {
        yield return message.Body;
        if (message.Outcome is { } outcome) { yield return outcome.ToString(); }
        if (message.Reason is { } reason) { yield return reason.ToString(); }
        yield return message.ExpectedTaskState.ToString();
    }
}
