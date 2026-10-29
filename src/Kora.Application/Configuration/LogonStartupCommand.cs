using Kora.Core.Artifacts;

namespace Kora.Application.Configuration;

public static class LogonStartupCommand
{
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["check start at logon", "enable start at logon", "disable start at logon",
            "reset start at logon", "confirm start at logon change", "cancel start at logon review"];
    public const string Syntax = "Use check/enable/disable/reset start at logon; then separately confirm start at logon change or cancel start at logon review.";

    public static LogonStartupOperation? Parse(string text, string assistantName)
    {
        var value = ArtifactCommandRouter.RemoveActivationPrefix(text.Trim(), assistantName);
        if (!value.Contains("start at logon", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (text.Length > 256 || text.Any(char.IsControl)) { return LogonStartupOperation.Invalid; }
        for (var index = 0; index < FixedPhrases.Count; index++)
        {
            if (string.Equals(value, FixedPhrases[index], StringComparison.OrdinalIgnoreCase))
            { return (LogonStartupOperation)index; }
        }
        return LogonStartupOperation.Invalid;
    }
}
