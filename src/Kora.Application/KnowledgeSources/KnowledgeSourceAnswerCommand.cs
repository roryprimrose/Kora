using Kora.Core.Artifacts;
using Kora.Core.Context;

namespace Kora.Application.KnowledgeSources;

public sealed record KnowledgeSourceAnswerCommand(string Name, string Question, bool Invalid = false)
{
    public static KnowledgeSourceAnswerCommand? Parse(string input, string assistantName)
    {
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input.TrimStart(), assistantName);
        const string prefix = "answer using source ";
        if (!text.StartsWith("answer using source", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (!text.StartsWith(prefix + "\"", StringComparison.OrdinalIgnoreCase)) { return new("", "", true); }
        var end = text.IndexOf("\": ", prefix.Length + 1, StringComparison.Ordinal);
        if (end < 0) { return new("", "", true); }
        var name = text[(prefix.Length + 1)..end];
        var question = text[(end + 3)..];
        return string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl)
            || !LocalFileRetrievalPolicy.TryQuery(question, out _)
                ? new("", "", true) : new(name, question);
    }
}
