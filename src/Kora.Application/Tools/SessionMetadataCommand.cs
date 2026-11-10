using Kora.Core.Artifacts;
using Kora.Core.Tools;

namespace Kora.Application.Tools;

public sealed record SessionMetadataCommand(string Subject, string Caller, string Question)
{
    public const string Phrase = "review session metadata";
    public string? Error { get; init; }

    public static SessionMetadataCommand? Parse(string input, string assistantName)
    {
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input, assistantName);
        if (!text.StartsWith(Phrase, StringComparison.OrdinalIgnoreCase)) { return null; }
        var rest = text[Phrase.Length..].Trim();
        var parts = rest.Split(" for ", 2, StringSplitOptions.None);
        var colon = parts.Length == 2 ? parts[1].IndexOf(':', StringComparison.Ordinal) : -1;
        if (parts.Length != 2 || colon < 0 || !SessionMetadataSnapshot.IsCanonicalId(parts[0])
            || !SessionMetadataSnapshot.IsCanonicalId(parts[1][..colon])
            || string.IsNullOrWhiteSpace(parts[1][(colon + 1)..]))
        {
            return new(string.Empty, string.Empty, string.Empty)
            { Error = "Use review session metadata <exact subject ID> for <exact caller ID>: <question>. No session was inspected or selected." };
        }
        return new(parts[0], parts[1][..colon], parts[1][(colon + 1)..].Trim());
    }
}
