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

        var normalized = Normalize(transcript);
        var commands = catalog.GetActivationCommands(assistantName)
            .SelectMany(command => command.AllPhrases.Select(phrase => (Phrase: Normalize(phrase), Command: command)))
            .DistinctBy(item => (item.Phrase, item.Command.Action))
            .ToDictionary(item => item.Phrase, item => item.Command, StringComparer.Ordinal);
        // A valid prefix such as "Stop" must not consume the exact privacy-reducing command.
        if (commands.TryGetValue(normalized, out var stop) && stop.Action == BuiltInAction.DisableListening)
        {
            return new CommandMatch(true, transcript, normalized, stop);
        }
        if (AssistantNameRules.GetActivationNames(assistantName)
            .Any(name => string.Equals(normalized, Normalize(name), StringComparison.Ordinal)))
        {
            return CommandMatch.NotMatched(transcript, string.Empty);
        }

        if (TryRemoveNormalizedActivationName(normalized, assistantName, out var remainder))
        {
            normalized = remainder;
        }

        return commands.TryGetValue(normalized, out var command)
            ? new CommandMatch(true, transcript, normalized, command)
            : CommandMatch.NotMatched(transcript, normalized);
    }

    public bool IsActivationPrefixed(string transcript, string assistantName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcript);
        return TryRemoveNormalizedActivationName(Normalize(transcript), assistantName, out _);
    }

    /// <summary>Removes the longest accepted activation name, followed by a word break, from normalized text.</summary>
    internal static bool TryRemoveNormalizedActivationName(
        string normalizedText, string assistantName, out string remainder)
    {
        foreach (var name in AssistantNameRules.GetActivationNames(assistantName))
        {
            var prefix = $"{Normalize(name)} ";
            if (normalizedText.StartsWith(prefix, StringComparison.Ordinal))
            {
                remainder = normalizedText[prefix.Length..];
                return true;
            }
        }
        remainder = normalizedText;
        return false;
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