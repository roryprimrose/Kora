using Kora.Core.Artifacts;

namespace Kora.Application.Agents;

public sealed record AgentProfileAuthoringCommand(AgentProfileAuthoringAction Action, string Argument)
{
    public string? Description { get; init; }

    public static AgentProfileAuthoringCommand? Parse(string text)
    {
        const string draft = "agent author draft ";
        if (text.StartsWith(draft, StringComparison.OrdinalIgnoreCase))
        {
            var separator = text.IndexOf(": ", draft.Length, StringComparison.Ordinal);
            if (separator < 0) { throw new InvalidDataException("Use agent author draft <new-id>: <original description>."); }
            var name = OwnedInstructionNames.WriteName(text[draft.Length..separator]);
            return new(AgentProfileAuthoringAction.DraftFromDescription, name) { Description = text[(separator + 2)..] };
        }
        foreach (var (phrase, action) in new[]
        {
            ("agent author create ", AgentProfileAuthoringAction.Create),
            ("agent author source ", AgentProfileAuthoringAction.Stage),
            ("agent author describe ", AgentProfileAuthoringAction.Describe),
            ("agent author ask", AgentProfileAuthoringAction.Ask),
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
                if (action is not (AgentProfileAuthoringAction.Stage or AgentProfileAuthoringAction.Describe))
                { OwnedInstructionNames.WriteName(argument); }
                return new(action, argument);
            }
            if (string.Equals(phrase, text, StringComparison.OrdinalIgnoreCase)) { return new(action, string.Empty); }
        }
        return null;
    }
}
