using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Core.Dependencies;

public static class LocalModelQuestionSpeech
{
    private static readonly string[][] NumberPhrases =
    [
        ["one", "option one", "first option"],
        ["two", "option two", "second option"],
        ["three", "option three", "third option"],
        ["four", "option four", "fourth option"],
    ];

    public static bool AreOptionsUnambiguous(IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count is < 2 or > 4)
        {
            return false;
        }
        var phrases = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < options.Count; index++)
        {
            var variants = new[] { options[index], (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) }
                .Concat(NumberPhrases[index])
                .Select(BuiltInCommandRouter.Normalize)
                .Distinct(StringComparer.Ordinal);
            if (variants.Any(phrase => !phrases.Add(phrase)))
            {
                return false;
            }
        }
        return true;
    }

    public static IEnumerable<string> GetPhrases(LocalModelQuestion question, string assistantName)
    {
        ArgumentNullException.ThrowIfNull(question);
        var name = AssistantNameRules.Normalize(assistantName);
        var phrases = question.Options.SelectMany((option, index) =>
            new[] { (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) }
                .Concat(NumberPhrases[index])
                .Concat(option.All(character => char.IsLetterOrDigit(character)
                    || char.IsWhiteSpace(character) || character is '-' or '\'')
                    ? [option] : []));
        return phrases.Concat(["cancel question", "never mind", "reject",
                "accept", "approve", "yes", "no", "deny"])
            .SelectMany(phrase => new[] { phrase, $"{name} {phrase}" });
    }

    public static int Match(string transcript, LocalModelQuestion question, string assistantName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcript);
        ArgumentNullException.ThrowIfNull(question);
        var name = BuiltInCommandRouter.Normalize(AssistantNameRules.Normalize(assistantName));
        var normalized = BuiltInCommandRouter.Normalize(transcript);
        if (normalized.StartsWith($"{name} ", StringComparison.Ordinal))
        {
            normalized = normalized[(name.Length + 1)..];
        }

        for (var index = 0; index < question.Options.Count; index++)
        {
            if (string.Equals(normalized, BuiltInCommandRouter.Normalize(question.Options[index]),
                    StringComparison.Ordinal)
                || string.Equals(normalized, (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    StringComparison.Ordinal)
                || NumberPhrases[index].Any(phrase =>
                    string.Equals(normalized, phrase, StringComparison.Ordinal)))
            {
                return index;
            }
        }

        if (normalized is "yes" or "accept" or "approve")
        {
            return FindUniqueOption(question, "yes", "accept", "approve");
        }
        if (normalized is "no" or "reject" or "deny")
        {
            return FindUniqueOption(question, "no", "reject", "deny");
        }
        return -1;
    }

    public static bool IsCancellation(string transcript, string assistantName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(transcript);
        var name = BuiltInCommandRouter.Normalize(AssistantNameRules.Normalize(assistantName));
        var normalized = BuiltInCommandRouter.Normalize(transcript);
        if (normalized.StartsWith($"{name} ", StringComparison.Ordinal))
        {
            normalized = normalized[(name.Length + 1)..];
        }
        return normalized is "cancel question" or "never mind" or "reject";
    }

    private static int FindUniqueOption(LocalModelQuestion question, params string[] labels)
    {
        var matches = question.Options
            .Select((option, index) => (option, index))
            .Where(item => labels.Contains(
                BuiltInCommandRouter.Normalize(item.option), StringComparer.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0].index : -1;
    }
}
