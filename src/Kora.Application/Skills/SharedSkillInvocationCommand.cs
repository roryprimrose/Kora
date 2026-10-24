using System.Text;
using Kora.Core.Artifacts;
using Kora.Core.Commands;

namespace Kora.Application.Skills;

public sealed record SharedSkillInvocationCommand(string Action, string Argument, bool ToolCapable = false)
{
    public static SharedSkillInvocationCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input, assistantName);
        var toolCapable = text.StartsWith("review tool skill ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("tool skill ", StringComparison.OrdinalIgnoreCase);
        if (toolCapable)
        {
            text = text.StartsWith("review tool skill ", StringComparison.OrdinalIgnoreCase)
                ? "review shared skill " + text["review tool skill ".Length..]
                : "shared skill " + text["tool skill ".Length..];
        }
        if (!text.StartsWith("review shared skill ", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("shared skill ", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes)
        { return new("Invalid", string.Empty, toolCapable); }
        if (text.StartsWith("review shared skill ", StringComparison.OrdinalIgnoreCase))
        { return new("Review", text["review shared skill ".Length..], toolCapable); }
        if (toolCapable && string.Equals(text, "shared skill review tool revision", StringComparison.OrdinalIgnoreCase))
        { return new("ReviewToolRevision", string.Empty, true); }
        if (toolCapable && text.StartsWith("shared skill address ", StringComparison.OrdinalIgnoreCase))
        { return new("Address", text["shared skill address ".Length..], true); }
        foreach (var (phrase, action) in new[]
        {
            ("shared skill request ", "Request"), ("shared skill target ", "Target"),
            ("shared skill enable", "Enable"), ("shared skill review invocation", "Prepare"),
            ("shared skill targets", "Targets"), ("shared skill next targets", "NextTargets"),
            ("shared skill confirm invocation", "Invoke"), ("shared skill close", "Close"),
        })
        {
            if (action is "Request" or "Target" && text.StartsWith(phrase, StringComparison.OrdinalIgnoreCase))
            { return new(action, text[phrase.Length..], toolCapable); }
            if (string.Equals(text, phrase, StringComparison.OrdinalIgnoreCase)) { return new(action, string.Empty, toolCapable); }
        }
        return new("Invalid", string.Empty, toolCapable);
    }
}
