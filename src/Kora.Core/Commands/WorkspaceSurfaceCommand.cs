using Kora.Core.Artifacts;

namespace Kora.Core.Commands;

public sealed record WorkspaceSurfaceCommand(WorkspaceSurfaceTarget Target)
{
    private static readonly (string Phrase, WorkspaceSurfaceTarget Target)[] Routes =
    [
        ("open all work", WorkspaceSurfaceTarget.AllWork),
        ("open compact conversation", WorkspaceSurfaceTarget.CompactConversation),
    ];

    public static IReadOnlyList<string> FixedPhrases { get; } =
        Array.AsReadOnly(Routes.Select(route => route.Phrase).ToArray());

    public static WorkspaceSurfaceCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = input.Trim();
        if (!FixedPhrases.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            text = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName);
        }
        var normalized = BuiltInCommandRouter.Normalize(text);
        if (!normalized.StartsWith("open all work", StringComparison.Ordinal)
            && !normalized.StartsWith("open saved conversation", StringComparison.Ordinal)
            && !normalized.StartsWith("open compact", StringComparison.Ordinal))
        {
            return null;
        }
        foreach (var route in Routes)
        {
            if (string.Equals(text, route.Phrase, StringComparison.OrdinalIgnoreCase)
                && !input.Any(char.IsControl))
            {
                return new(route.Target);
            }
        }
        return new(WorkspaceSurfaceTarget.Invalid);
    }
}
