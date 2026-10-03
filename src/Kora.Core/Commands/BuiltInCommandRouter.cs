using System.Text;

using Kora.Core.Configuration;

namespace Kora.Core.Commands;

public sealed class BuiltInCommandRouter(BuiltInCommandCatalog catalog)
{
    public CommandMatch Match(string transcript) =>
        Match(transcript, AssistantNameRules.DefaultName);

    public CommandMatch Match(string transcript, string assistantName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcript);

        var normalizedAssistantName = Normalize(AssistantNameRules.Normalize(assistantName));
        var normalized = Normalize(transcript);
        if (string.Equals(normalized, normalizedAssistantName, StringComparison.Ordinal))
        {
            return CommandMatch.NotMatched(transcript, string.Empty);
        }

        var activationName = $"{normalizedAssistantName} ";
        if (normalized.StartsWith(activationName, StringComparison.Ordinal))
        {
            normalized = normalized[activationName.Length..];
        }

        var commands = catalog.GetCommands(assistantName)
            .SelectMany(command => command.AllPhrases.Select(phrase => (Phrase: Normalize(phrase), Command: command)))
            .DistinctBy(item => (item.Phrase, item.Command.Action))
            .ToDictionary(item => item.Phrase, item => item.Command, StringComparer.Ordinal);
        return commands.TryGetValue(normalized, out var command)
            ? new CommandMatch(true, transcript, normalized, command)
            : CommandMatch.NotMatched(transcript, normalized);
    }

    public bool IsActivationPrefixed(string transcript, string assistantName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcript);
        var normalizedAssistantName = Normalize(AssistantNameRules.Normalize(assistantName));
        var normalizedTranscript = Normalize(transcript);
        return normalizedTranscript.StartsWith(
            $"{normalizedAssistantName} ",
            StringComparison.Ordinal);
    }

    public bool ContainsNormalizedPhrase(string text, string phrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(phrase);
        return Normalize(text).Contains(Normalize(phrase), StringComparison.Ordinal);
    }

    internal static string Normalize(string value)
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