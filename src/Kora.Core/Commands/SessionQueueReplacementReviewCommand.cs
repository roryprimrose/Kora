using System.Text;

using Kora.Core.Configuration;

namespace Kora.Core.Commands;

/// <summary>Exact native-review preparation only; never replacement or confirmation authority.</summary>
public sealed record SessionQueueReplacementReviewCommand(Guid SessionId, Guid TaskId, bool IsHelp = false, string? Error = null)
{
    public const string Syntax = "session queue replacement review <session-id> <task-id> | session queue replacement help";
    public const string Boundary = "Review only in private native Sessions. Separate native confirmation is required; "
        + "no removal, new task, enqueue, dispatch, replay, pause change or old deadline extension.";

    public static SessionQueueReplacementReviewCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var prefix = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length
            && (text[prefix.Length] == ',' || char.IsWhiteSpace(text[prefix.Length])))
        { text = text[prefix.Length..].TrimStart(' ', ',', '\t'); }
        var head = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (head.Length < 3 || !head[0].Equals("session", StringComparison.OrdinalIgnoreCase)
            || !head[1].Equals("queue", StringComparison.OrdinalIgnoreCase)
            || !head[2].Equals("replacement", StringComparison.OrdinalIgnoreCase))
        { return null; }
        static SessionQueueReplacementReviewCommand Invalid() => new(Guid.Empty, Guid.Empty, Error: Syntax + ". " + Boundary);
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return Invalid(); }
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 4 && words[3].Equals("help", StringComparison.OrdinalIgnoreCase))
        { return new(Guid.Empty, Guid.Empty, IsHelp: true); }
        if (words.Length != 6 || !words[3].Equals("review", StringComparison.OrdinalIgnoreCase)
            || !SessionCommand.ExactId(words[4], out var session)
            || !string.Equals(words[4], session.ToString("D"), StringComparison.Ordinal)
            || !SessionCommand.ExactId(words[5], out var task)
            || !string.Equals(words[5], task.ToString("D"), StringComparison.Ordinal))
        { return Invalid(); }
        return new(session, task);
    }
}
