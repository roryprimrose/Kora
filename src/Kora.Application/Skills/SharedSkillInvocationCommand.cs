using System.Text;
using Kora.Core.Artifacts;
using Kora.Core.Commands;

namespace Kora.Application.Skills;

public sealed record SharedSkillInvocationCommand(string Action, string Argument)
{
    public static SharedSkillInvocationCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input, assistantName);
        if (!text.StartsWith("review shared skill ", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("shared skill ", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes)
        { return new("Invalid", string.Empty); }
        if (text.StartsWith("review shared skill ", StringComparison.OrdinalIgnoreCase))
        { return new("Review", text["review shared skill ".Length..]); }
        foreach (var (phrase, action) in new[]
        {
            ("shared skill request ", "Request"), ("shared skill target ", "Target"),
            ("shared skill enable", "Enable"), ("shared skill review invocation", "Prepare"),
            ("shared skill targets", "Targets"), ("shared skill next targets", "NextTargets"),
            ("shared skill confirm invocation", "Invoke"), ("shared skill close", "Close"),
        })
        {
            if (action is "Request" or "Target" && text.StartsWith(phrase, StringComparison.OrdinalIgnoreCase))
            { return new(action, text[phrase.Length..]); }
            if (string.Equals(text, phrase, StringComparison.OrdinalIgnoreCase)) { return new(action, string.Empty); }
        }
        return new("Invalid", string.Empty);
    }
}
