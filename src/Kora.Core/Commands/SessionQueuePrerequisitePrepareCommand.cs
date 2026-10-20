using System.Text;

using Kora.Core.Configuration;

namespace Kora.Core.Commands;

/// <summary>Preparation only; choosing a prerequisite remains a separate native gesture.</summary>
public sealed record SessionQueuePrerequisitePrepareCommand(Guid SessionId, Guid? TaskId, bool Help = false, string? Error = null)
{
    public const string Syntax = "session queue prerequisite prepare <session-id> <task-id|none> | session queue prerequisite help";
    public const string Boundary = "PREPARATION only in private native Sessions. IDs must be nonempty lowercase canonical D GUIDs; "
        + "literal none explicitly proposes no prerequisite. Separately Apply prepared choice before any selection changes. "
        + "Only exact Succeeded permits later dispatch. No enqueue, task creation, dispatch, resume, approval or grant.";

    public static SessionQueuePrerequisitePrepareCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var prefix = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length
            && (text[prefix.Length] == ',' || char.IsWhiteSpace(text[prefix.Length])))
        {
            text = text[prefix.Length..].TrimStart(' ', ',', '\t');
        }
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3 || !words[0].Equals("session", StringComparison.OrdinalIgnoreCase)
            || !words[1].Equals("queue", StringComparison.OrdinalIgnoreCase)
            || !words[2].Equals("prerequisite", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) <= SessionCommand.MaximumInputBytes && !input.Any(char.IsControl))
        {
            if (words.Length == 4 && words[3].Equals("help", StringComparison.OrdinalIgnoreCase))
            { return new(Guid.Empty, null, Help: true); }
            if (words.Length == 6 && words[3].Equals("prepare", StringComparison.OrdinalIgnoreCase)
                && CanonicalId(words[4], out var session))
            {
                if (words[5].Equals("none", StringComparison.Ordinal)) { return new(session, null); }
                if (CanonicalId(words[5], out var task)) { return new(session, task); }
            }
        }
        return new(Guid.Empty, null, Error: Syntax + ". " + Boundary);
    }

    private static bool CanonicalId(string text, out Guid id) => SessionCommand.ExactId(text, out id)
        && string.Equals(text, id.ToString("D"), StringComparison.Ordinal);
}
