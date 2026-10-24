using Kora.Core.Artifacts;

namespace Kora.Application.Agents;

public sealed record AgentRunCommand(string Action, string Argument)
{
    public AgentRunSetCommand? RunSet { get; init; }
    public static AgentRunCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input, assistantName);
        foreach (var (phrase, action) in new[]
        {
            ("agent source ", "Source"), ("agent request ", "Request"), ("agent target ", "Target"),
            ("review agent", "Open"), ("agent targets", "Targets"), ("agent next targets", "NextTargets"),
            ("agent review invocation", "Review"), ("agent confirm invocation", "Invoke"), ("agent stop", "Stop"),
            ("agent review references", "Review"), ("agent consent resolution", "Resolve"),
        })
        {
            if (phrase.EndsWith(' ') && text.StartsWith(phrase, StringComparison.OrdinalIgnoreCase))
            { return new(action, text[phrase.Length..]); }
            if (string.Equals(phrase, text, StringComparison.OrdinalIgnoreCase)) { return new(action, string.Empty); }
        }
        return AgentRunSetCommand.Parse(text) is { } runSet
            ? new("RunSet", string.Empty) { RunSet = runSet } : null;
    }
}
