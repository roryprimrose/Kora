using System.Globalization;

using Kora.Core.Configuration;
using Kora.Core.Dependencies;

namespace Kora.Core.Interaction;

/// <summary>Maps an entire utterance to one exact schema. No command interpretation or approval authority.</summary>
public static class QuestionVoiceAnswer
{
    public static QuestionAnswer? Parse(HostQuestionRecord question, string transcript, string? activationName = null)
    {
        return question.Proposal is null ? Parse(question.Spec, transcript, activationName) : null;
    }

    public static QuestionAnswer? Parse(QuestionSpec spec, string transcript, string? activationName = null)
    {
        var name = activationName is null ? null : AssistantNameRules.Normalize(activationName);
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return null;
        }
        if (spec.Kind == QuestionKind.Text)
        {
            var text = new QuestionAnswer([], transcript);
            return spec.Accepts(text, submitting: true) ? text : null;
        }
        var spoken = transcript.Trim();
        var matches = MatchingOptions(spec, spoken);
        // Even ambiguous bare labels retain their meaning; only an unmatched choice may use a prefix.
        if (matches.Length == 0 && name is not null && spoken.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && spoken.Length > name.Length && (char.IsWhiteSpace(spoken[name.Length]) || spoken[name.Length] == ','))
        {
            var start = name.Length + (spoken[name.Length] == ',' ? 1 : 0);
            spoken = spoken[start..].Trim();
            matches = MatchingOptions(spec, spoken);
        }
        if (matches.Length == 0 && spec.Kind == QuestionKind.MultipleChoice)
        {
            if (spec.Minimum == 0 && (string.Equals(spoken, "none", StringComparison.OrdinalIgnoreCase)
                || string.Equals(spoken, "no options", StringComparison.OrdinalIgnoreCase)))
            { return new([]); }
            var parts = spoken.Replace(" and ", ",", StringComparison.OrdinalIgnoreCase)
                .Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2 || parts.Length > spec.Maximum) { return null; }
            var selected = parts.Select(part => MatchingOptions(spec, part)).ToArray();
            if (selected.Any(options => options.Length != 1)) { return null; }
            var multiple = new QuestionAnswer(selected.Select(options => options[0].Id));
            return spec.Accepts(multiple, submitting: true) ? multiple : null;
        }
        if (matches.Length != 1) { return null; }
        var answer = new QuestionAnswer([matches[0].Id]);
        return spec.Accepts(answer, submitting: true) ? answer : null;
    }

    private static QuestionOption[] MatchingOptions(QuestionSpec spec, string spoken) =>
        spec.Options.Where((option, index) =>
            string.Equals(spoken, option.Label, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(spec.Purpose, "computer-power", StringComparison.Ordinal)
                && (string.Equals(spoken, (index + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    || string.Equals(spoken, "option " + (index + 1).ToString(CultureInfo.InvariantCulture),
                        StringComparison.OrdinalIgnoreCase)
                    || LocalModelQuestionSpeech.MatchesNumberPhrase(spoken, index))).ToArray();
}