using System.Security.Cryptography;
using System.Text.Json;

using Kora.Core.Storage;

namespace Kora.Core.Conversation;

/// <summary>Passive lexical matching of original USER content and typed HOST status, not receipt history.</summary>
public sealed class ConversationSearch
{
    private readonly SessionHistorySearch lexical;
    public ConversationSearch(string query, ConversationSearchRole role = ConversationSearchRole.All,
        ConversationTerminalFilter terminal = default, ConversationRangeFilter range = default,
        ConversationCorrelationFilter correlation = default)
    {
        if (!Enum.IsDefined(role)) { throw new ArgumentOutOfRangeException(nameof(role)); }
        terminal.Validate();
        try { range.Validate(); }
        catch (InvalidDataException exception) { throw new ArgumentException("Invalid conversation range.", nameof(range), exception); }
        try { correlation.Validate(); }
        catch (InvalidDataException exception) { throw new ArgumentException("Invalid conversation correlation.", nameof(correlation), exception); }
        lexical = new(query);
        Role = role;
        Terminal = terminal;
        Range = range;
        Correlation = correlation;
        Digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(
            new { lexical.Digest, Role, Terminal, Range, Correlation })));
    }

    public ConversationSearchRole Role { get; }
    public ConversationTerminalFilter Terminal { get; }
    public ConversationRangeFilter Range { get; }
    public ConversationCorrelationFilter Correlation { get; }
    public string Digest { get; }

    public bool Matches(ConversationMessage message, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        message.Validate();
        return Correlation.Matches(message) && Range.Matches(message) && Terminal.Matches(message, Role)
            && Fields(message).Any(field => lexical.MatchesText(field, token));
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
