using System.Text;

using Kora.Core.Configuration;

namespace Kora.Core.Commands;

/// <summary>Exact adjacent-pair preparation, never a confirmation or positional selector.</summary>
public sealed record SessionQueueMoveReviewCommand(Guid SessionId, Guid TaskId, Guid NeighborId, bool Up, string? Error = null)
{
    public const string Syntax = "session queue move review <session-id> <task-id> <neighbor-task-id> <up|down>";
    public const string Boundary = "Preparation only in private native Sessions. Both exact IDs must be adjacent in the current queue. "
        + "Review current/proposed order and original deadlines; confirmation is a separate one-shot native action. "
        + "Nothing moves, dispatches, pauses, resumes or renews through this command. Changes require a fresh command.";

    public static SessionQueueMoveReviewCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var prefix = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length
            && (text[prefix.Length] == ',' || char.IsWhiteSpace(text[prefix.Length])))
        {
            text = text[prefix.Length..].TrimStart(' ', ',', '\t');
        }
        var head = text.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
        if (head.Length < 3 || !head[0].Equals("session", StringComparison.OrdinalIgnoreCase)
            || !head[1].Equals("queue", StringComparison.OrdinalIgnoreCase)
            || !head[2].Equals("move", StringComparison.OrdinalIgnoreCase))
        { return null; }

        static SessionQueueMoveReviewCommand Invalid() => new(Guid.Empty, Guid.Empty, Guid.Empty, false, Syntax + ". " + Boundary);
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return Invalid(); }
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 8 || !words[3].Equals("review", StringComparison.OrdinalIgnoreCase)
            || !CanonicalId(words[4], out var session) || !CanonicalId(words[5], out var task)
            || !CanonicalId(words[6], out var neighbor) || task == neighbor
            || words[7] is not ("up" or "down"))
        { return Invalid(); }
        return new(session, task, neighbor, string.Equals(words[7], "up", StringComparison.Ordinal));
    }

    private static bool CanonicalId(string text, out Guid id) =>
        SessionCommand.ExactId(text, out id) && string.Equals(text, id.ToString("D"), StringComparison.Ordinal);
}
