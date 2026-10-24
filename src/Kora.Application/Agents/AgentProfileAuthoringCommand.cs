using Kora.Core.Artifacts;

namespace Kora.Application.Agents;

public sealed record AgentProfileAuthoringCommand(AgentProfileAuthoringAction Action, string Argument)
{
    public static AgentProfileAuthoringCommand? Parse(string text)
    {
        foreach (var (phrase, action) in new[]
        {
            ("agent author create ", AgentProfileAuthoringAction.Create),
            ("agent author source ", AgentProfileAuthoringAction.Stage),
            ("agent author inspect ", AgentProfileAuthoringAction.Inspect),
            ("agent author validate", AgentProfileAuthoringAction.Validate),
            ("agent author save as new", AgentProfileAuthoringAction.ReviewSave),
            ("agent author list", AgentProfileAuthoringAction.List),
            ("agent author cancel review", AgentProfileAuthoringAction.CancelReview),
            ("agent author", AgentProfileAuthoringAction.Open),
        })
        {
            if (phrase.EndsWith(' ') && text.StartsWith(phrase, StringComparison.OrdinalIgnoreCase))
            {
                var argument = text[phrase.Length..];
                if (action is not AgentProfileAuthoringAction.Stage) { OwnedInstructionNames.WriteName(argument); }
                return new(action, argument);
            }
            if (string.Equals(phrase, text, StringComparison.OrdinalIgnoreCase)) { return new(action, string.Empty); }
        }
        return null;
    }
}
