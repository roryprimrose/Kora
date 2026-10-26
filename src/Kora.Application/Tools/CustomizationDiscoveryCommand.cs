using Kora.Core.Artifacts;

namespace Kora.Application.Tools;

public sealed record CustomizationDiscoveryCommand(string Request)
{
    public const string Phrase = "discover customizations";

    public static CustomizationDiscoveryCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input, assistantName);
        if (string.Equals(text, Phrase, StringComparison.OrdinalIgnoreCase)) { return new(string.Empty); }
        return text.StartsWith(Phrase + " ", StringComparison.OrdinalIgnoreCase)
            ? new(text[(Phrase.Length + 1)..]) : null;
    }
}
