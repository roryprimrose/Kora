using Kora.Core.Artifacts;
using Kora.Core.Customizations;

namespace Kora.Application.Definitions;

public sealed record CustomizationCommand(string Operation, CustomizationKind? Kind = null, string? Name = null, string? Text = null)
{
    public Func<bool> IsCurrent { get; init; } = static () => false;

    private static readonly string[] EditorReferences =
        ["this editor", "this artifact", "this instruction", "this skill", "this agent", "this MCP server", "this prompt"];

    public static bool ReferencesEditor(string input) =>
        EditorReferences.Any(phrase => input.Contains(phrase, StringComparison.OrdinalIgnoreCase));

    public static CustomizationCommand? Parse(string input, string assistantName)
    {
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input.Trim(), assistantName);
        const string adaptation = "create kora adaptation ";
        if (text.StartsWith(adaptation, StringComparison.OrdinalIgnoreCase) && text.Length > adaptation.Length)
        { return new("Adapt", CustomizationKind.Skill, text[adaptation.Length..].Trim()); }
        if (text.Equals("save skill adaptation", StringComparison.OrdinalIgnoreCase))
        { return new("SaveAdaptation", CustomizationKind.Skill); }
        if (text.Equals("customizations", StringComparison.OrdinalIgnoreCase)
            || text.Equals("show customizations", StringComparison.OrdinalIgnoreCase)
            || text.Equals("open customizations", StringComparison.OrdinalIgnoreCase))
        { return new("List"); }
        foreach (var (singular, plural, kind) in new[]
        {
            ("skill", "skills", CustomizationKind.Skill), ("instruction", "instructions", CustomizationKind.Instruction),
            ("agent", "agents", CustomizationKind.Agent), ("MCP server", "MCP servers", CustomizationKind.McpServer),
            ("prompt", "prompts", CustomizationKind.Prompt),
        })
        {
            if (text.Equals("show " + plural, StringComparison.OrdinalIgnoreCase)
                || text.Equals("show my " + plural, StringComparison.OrdinalIgnoreCase))
            { return new("List", kind); }
            foreach (var (prefix, operation) in new[]
            { ("show me ", "Select"), ("show ", "Select"), ("edit ", "Select"), ("disable ", "Disable"), ("enable ", "Enable") })
            {
                var suffix = " " + singular;
                if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length + suffix.Length)
                { return new(operation, kind, text[prefix.Length..^suffix.Length].Trim()); }
            }
            foreach (var prefix in new[] { "add a " + singular, "add an " + singular, "create a " + singular, "create an " + singular })
            {
                if (text.Equals(prefix, StringComparison.OrdinalIgnoreCase)) { return new("Create", kind); }
                if (text.StartsWith(prefix + " named ", StringComparison.OrdinalIgnoreCase))
                { return new("Create", kind, text[(prefix.Length + 7)..].Trim()); }
                if (text.StartsWith(prefix + " that ", StringComparison.OrdinalIgnoreCase))
                { return new("Create", kind, Text: text[(prefix.Length + 1)..]); }
            }
        }
        const string refine = "refine this artifact ";
        if (text.StartsWith(refine, StringComparison.OrdinalIgnoreCase) && text.Length > refine.Length)
        { return new("Refine", Text: text[refine.Length..]); }
        return null;
    }
}
