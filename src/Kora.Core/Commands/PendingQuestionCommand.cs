using Kora.Core.Artifacts;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

public sealed record PendingQuestionCommand(bool IsHelp = false, Guid? SessionId = null, Guid? TaskId = null)
{
    public const string Help = "pending question help | open pending question <session-id> <task-id>. "
        + "Use nonempty canonical lowercase D-format IDs. Focus only the exact currently hosted native question. "
        + "No historical recovery, answer, approval, selection, replay or lifetime extension.";

    public static PendingQuestionCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = input.Trim();
        // Literal reserved commands win over an assistant name that collides with their prefix.
        if (!text.Equals("pending question help", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("open pending question ", StringComparison.OrdinalIgnoreCase))
        {
            var withoutActivation = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName);
            if (Reserved(withoutActivation)) { text = withoutActivation; }
        }
        if (!Reserved(text)) { return null; }
        if (input.Any(char.IsControl)) { return new(); }
        if (text.Equals("pending question help", StringComparison.OrdinalIgnoreCase)) { return new(true); }
        var words = text.Split(' ');
        if (words.Length != 5 || !string.Equals(string.Join(' ', words.Take(3)), "open pending question",
            StringComparison.OrdinalIgnoreCase)) { return new(); }
        try
        {
            var session = OptionalCanonicalGuid.Parse(words[3]);
            var task = OptionalCanonicalGuid.Parse(words[4]);
            return session is not null && task is not null ? new(SessionId: session, TaskId: task) : new();
        }
        catch (InvalidDataException) { return new(); }
    }

    private static bool Reserved(string text)
    {
        var normalized = BuiltInCommandRouter.Normalize(text);
        return normalized.StartsWith("open pending", StringComparison.Ordinal)
            || normalized.StartsWith("pending question", StringComparison.Ordinal);
    }
}
