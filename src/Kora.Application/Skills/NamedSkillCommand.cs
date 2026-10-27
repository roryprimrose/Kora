using System.Globalization;
using Kora.Core.Artifacts;
using Kora.Core.Context;

namespace Kora.Application.Skills;

/// <summary>Reserved original-user operations, never commands embedded in a skill task.</summary>
public sealed record NamedSkillCommand(string Action, string Name = "", int Choice = 0)
{
    public static NamedSkillCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input.TrimStart(), assistantName);
        if (Reserved(text, "use skill"))
        {
            if (!text.StartsWith("use skill ", StringComparison.OrdinalIgnoreCase)) { return new("Invalid"); }
            var name = text["use skill ".Length..];
            try { ValidateName(name); }
            catch (InvalidDataException) { return new("Invalid"); }
            return new("Review", name);
        }
        if (!Reserved(text, "skill")) { return null; }
        if (string.Equals(text, "skill close", StringComparison.OrdinalIgnoreCase)) { return new("Close"); }
        const string prefix = "skill choice ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(text[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var choice)
            && choice is > 0 and <= 160
            && string.Equals(choice.ToString(CultureInfo.InvariantCulture), text[prefix.Length..], StringComparison.Ordinal))
        { return new("Choice", Choice: choice); }
        return new("Invalid");
    }

    internal static void ValidateName(string name)
    {
        LiteralMetadataQuery.Validate(name);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128)
        { throw new InvalidDataException("Use an exact readable skill name of at most 128 UTF-16 units, without normalization."); }
    }

    private static bool Reserved(string text, string prefix) =>
        text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        && (text.Length == prefix.Length || char.IsWhiteSpace(text[prefix.Length]) || char.IsControl(text[prefix.Length]));
}
