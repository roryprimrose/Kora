using Kora.Core.Storage;

namespace Kora.Core.Conversation;

/// <summary>Passive lexical matching of original USER content and typed HOST status, not receipt history.</summary>
public sealed class ConversationSearch
{
    private readonly SessionHistorySearch lexical;
    public ConversationSearch(string query, ConversationSearchRole role = ConversationSearchRole.All,
        ConversationTerminalFilter terminal = default)
    {
        if (!Enum.IsDefined(role)) { throw new ArgumentOutOfRangeException(nameof(role)); }
        terminal.Validate();
        lexical = new(query);
        Role = role;
        Terminal = terminal;
    }

    public ConversationSearchRole Role { get; }
    public ConversationTerminalFilter Terminal { get; }
    public string Digest => lexical.Digest;

    public bool Matches(ConversationMessage message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Terminal.Matches(message, Role) && Fields(message).Any(field => lexical.MatchesText(field, token));
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
