using System.Globalization;
using System.Text;

using Kora.Core.Configuration;

namespace Kora.Core.Commands;

/// <summary>Exact preview-only grammar; no command can confirm disposition.</summary>
public sealed record SessionDispositionReviewCommand(Guid SessionId, long Generation, long MetadataRevision, string? Error = null)
{
    public const string Syntax = "review session disposition <canonical-D-guid> <positive-generation> <nonnegative-metadata-revision>";
    public const string Boundary = "Preview only in the private native Sessions window. Nothing is removed. "
        + "Confirm logical disposition requires a separate deliberate native action; typed/voice confirmation is unavailable.";

    public static SessionDispositionReviewCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var prefix = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length
            && (text[prefix.Length] == ',' || char.IsWhiteSpace(text[prefix.Length])))
        {
            text = text[prefix.Length..].TrimStart(' ', ',', '\t');
        }
        var head = text.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries);
        if (head.Length < 2 || !(head[0].Equals("review", StringComparison.OrdinalIgnoreCase)
                || head[0].Equals("confirm", StringComparison.OrdinalIgnoreCase))
            || !head[1].Equals("session", StringComparison.OrdinalIgnoreCase)
            || head.Length > 2 && !head[2].Equals("disposition", StringComparison.OrdinalIgnoreCase))
        { return null; }

        static SessionDispositionReviewCommand Invalid() => new(Guid.Empty, 0, 0, Syntax + ". " + Boundary);
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl)
            || !head[0].Equals("review", StringComparison.OrdinalIgnoreCase))
        { return Invalid(); }
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length != 6 || !SessionCommand.ExactId(words[3], out var id)
            || !string.Equals(words[3], id.ToString("D"), StringComparison.Ordinal)
            || !long.TryParse(words[4], NumberStyles.None, CultureInfo.InvariantCulture, out var generation) || generation < 1
            || !long.TryParse(words[5], NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 0)
        { return Invalid(); }
        return new(id, generation, revision);
    }
}
