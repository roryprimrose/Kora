using Kora.Core.Artifacts;

namespace Kora.Application.Agents;

public sealed record AgentRunCommand(string Action, string Argument)
{
    public AgentRunSetCommand? RunSet { get; init; }
    public AgentProfileAuthoringCommand? Authoring { get; init; }
    public static AgentRunCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input, assistantName);
        if (string.Equals(text, "agent invoke", StringComparison.OrdinalIgnoreCase))
        { return new("Named", string.Empty); }
        if (text.StartsWith("agent invoke ", StringComparison.OrdinalIgnoreCase))
        { return new("Named", text["agent invoke ".Length..]); }
        if (AgentProfileAuthoringCommand.Parse(text) is { } authoring)
        { return new("Authoring", string.Empty) { Authoring = authoring }; }
        foreach (var (phrase, action) in new[]
        {
            ("agent source ", "Source"), ("agent request ", "Request"), ("agent target ", "Target"),
            ("review agent", "Open"), ("agent targets", "Targets"), ("agent next targets", "NextTargets"),
            ("agent review invocation", "Review"), ("agent confirm invocation", "Invoke"), ("agent stop", "Stop"),
            ("agent review references", "Review"), ("agent consent resolution", "Resolve"),
            ("agent skill sources", "SkillSources"), ("agent skill source ", "SkillSource"),
            ("agent skill catalogue", "SkillCatalogue"), ("agent skill select ", "SkillSelect"),
            ("agent skill review declaration", "SkillDeclaration"), ("agent skill consent references", "SkillConsent"),
            ("agent skill enable", "SkillEnable"), ("agent conversation ", "SkillTarget"),
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
