using System.Text;

using Kora.Core.Artifacts;
using Kora.Core.Commands;

namespace Kora.Application.Skills;

public sealed record SkillReviewCommand(SkillReviewSurface? Surface)
{
    public const string Syntax = "open skill packages | show skill package review | open shared skill sources | show shared skill sources";
    public static IReadOnlyList<string> FixedPhrases { get; } = Array.AsReadOnly(new[]
    {
        "open skill packages", "show skill package review", "open shared skill sources", "show shared skill sources",
    });

    public static SkillReviewCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input.Trim(), assistantName);
        if (!text.StartsWith("open skill", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("show skill package", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("open shared skill", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("show shared skill", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(Surface: null); }
        return new(text.ToLowerInvariant() switch
        {
            "open skill packages" or "show skill package review" => SkillReviewSurface.BundledPackages,
            "open shared skill sources" or "show shared skill sources" => SkillReviewSurface.SharedSources,
            _ => null,
        });
    }
}
