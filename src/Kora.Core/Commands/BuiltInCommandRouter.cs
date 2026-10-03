using System.Text;

namespace Kora.Core.Commands;

public sealed class BuiltInCommandRouter(BuiltInCommandCatalog catalog)
{
    private readonly Dictionary<string, CommandDefinition> commands =
        catalog.GetCommands()
            .SelectMany(command => command.AllPhrases.Select(phrase => (Phrase: Normalize(phrase), Command: command)))
            .ToDictionary(item => item.Phrase, item => item.Command, StringComparer.Ordinal);

    public CommandMatch Match(string transcript)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcript);

        var normalized = Normalize(transcript);
        if (string.Equals(normalized, "kora", StringComparison.Ordinal))
        {
            return CommandMatch.NotMatched(transcript, string.Empty);
        }

        const string activationName = "kora ";
        if (normalized.StartsWith(activationName, StringComparison.Ordinal))
        {
            normalized = normalized[activationName.Length..];
        }

        return commands.TryGetValue(normalized, out var command)
            ? new CommandMatch(true, transcript, normalized, command)
            : CommandMatch.NotMatched(transcript, normalized);
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWasSpace = true;

        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().TrimEnd();
    }
}