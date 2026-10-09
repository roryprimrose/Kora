using Kora.Core.Storage;

namespace Kora.Core.Commands;

public sealed partial record SessionCommand
{
    private static SessionCommand ParseHistorySearch(string[] words, string? query)
    {
        static SessionCommand Invalid() => new(SessionCommandOperation.Invalid, Error: Syntax);
        if (words.Length < 3 || !ExactId(words[2], out var id) || query is null) { return Invalid(); }
        var start = 3;
        SessionHistorySearchCursor? cursor = null;
        if (words.Length > start && words[start].Equals("after", StringComparison.OrdinalIgnoreCase))
        {
            if (words.Length <= start + 1) { return Invalid(); }
            var segments = words[start + 1].Split(':');
            if (segments.Length != 4 || !Revision(segments[0], true, out var generation)
                || !Revision(segments[1], true, out var snapshot) || !Revision(segments[2], true, out var after)
                || after > snapshot || segments[3].Length != 64 || !segments[3].All(char.IsAsciiHexDigit))
            {
                return Invalid();
            }
            cursor = new(new(new(id), new(generation), snapshot, after), segments[3].ToUpperInvariant());
            start += 2;
        }
        var limit = DefaultPageSize;
        if (words.Length > start && words[start].Equals("limit", StringComparison.OrdinalIgnoreCase))
        {
            if (words.Length <= start + 1 || !Revision(words[start + 1], true, out var count)
                || count > SessionHistoryPage.MaximumRecords) { return Invalid(); }
            limit = (int)count;
            start += 2;
        }
        return start == words.Length
            ? new(SessionCommandOperation.HistorySearch, id, Limit: limit) { HistoryQuery = query, HistorySearchCursor = cursor }
            : Invalid();
    }
}
