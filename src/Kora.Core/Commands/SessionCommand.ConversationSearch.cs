using System.Text;

using Kora.Core.Conversation;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    public const string ConversationSearchSyntax =
        "conversation search <session-id> [--role All|USER|HOST|ASSISTANT] -- <verbatim query> | conversation search next <session-id> | "
        + "conversation search cancel <session-id>. IDs require canonical lowercase nonempty D GUIDs. "
        + "Everything after the single separator space following -- is the query, unchanged. "
        + "Search reads only one retained Active/Done conversation, never receipt history, drafts or model context. "
        + "All defaults to USER/HOST only; ASSISTANT matches only the complete retained assistant body. "
        + "Next uses only the host-held original-source snapshot, expiring after 60 seconds; cancel clears it. "
        + "Voice requires the current assistant-name prefix.";

    public string? ConversationQuery { get; init; }
    public ConversationSearchRole ConversationRole { get; init; }

    public bool IsConversationSearch => Operation is SessionCommandOperation.ConversationSearch
        or SessionCommandOperation.ConversationSearchNext or SessionCommandOperation.ConversationSearchCancel;

    private static SessionCommand? ParseConversationSearch(string text)
    {
        if (!text.Equals("conversation", StringComparison.OrdinalIgnoreCase)
            && !(text.StartsWith("conversation", StringComparison.OrdinalIgnoreCase)
                && char.IsWhiteSpace(text[12])))
        { return null; }
        static SessionCommand Invalid() => new(SessionCommandOperation.Invalid, Error: ConversationSearchSyntax);
        var separator = text.IndexOf(" -- ", StringComparison.Ordinal);
        var header = separator < 0 ? text.TrimEnd() : text[..separator];
        if (Encoding.UTF8.GetByteCount(header) > MaximumInputBytes || header.Any(char.IsControl))
        { return Invalid(); }
        var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !parts[1].Equals("search", StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
        if (parts.Length is 3 or 5 && separator >= 0)
        {
            try
            {
                var id = OptionalCanonicalGuid.ParseRequired(parts[2]);
                var role = ConversationSearchRole.All;
                if (parts.Length == 5)
                {
                    if (!parts[3].Equals("--role", StringComparison.OrdinalIgnoreCase)
                        || !Enum.GetNames<ConversationSearchRole>().Contains(parts[4], StringComparer.OrdinalIgnoreCase))
                    { return Invalid(); }
                    role = Enum.Parse<ConversationSearchRole>(parts[4], ignoreCase: true);
                }
                var query = text[(separator + 4)..];
                _ = new ConversationSearch(query, role);
                return new(SessionCommandOperation.ConversationSearch, id) { ConversationQuery = query, ConversationRole = role };
            }
            catch (InvalidDataException) { return Invalid(); }
            catch (ArgumentException) { return Invalid(); }
        }
        if (parts.Length == 4 && separator < 0)
        {
            try
            {
                var id = OptionalCanonicalGuid.ParseRequired(parts[3]);
                if (parts[2].Equals("next", StringComparison.OrdinalIgnoreCase))
                { return new(SessionCommandOperation.ConversationSearchNext, id); }
                if (parts[2].Equals("cancel", StringComparison.OrdinalIgnoreCase))
                { return new(SessionCommandOperation.ConversationSearchCancel, id); }
            }
            catch (InvalidDataException) { return Invalid(); }
        }
        return Invalid();
    }
}
