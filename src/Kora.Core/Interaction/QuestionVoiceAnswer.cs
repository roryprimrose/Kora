using System.Globalization;
using Kora.Core.Configuration;

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
            matches = MatchingOptions(spec, spoken[start..].Trim());
        }
        if (matches.Length != 1) { return null; }
        var answer = new QuestionAnswer([matches[0].Id]);
        return spec.Accepts(answer, submitting: true) ? answer : null;
    }

    private static QuestionOption[] MatchingOptions(QuestionSpec spec, string spoken) =>
        spec.Options.Where((option, index) =>
            string.Equals(spoken, option.Label, StringComparison.OrdinalIgnoreCase)
            || string.Equals(spoken, (index + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || string.Equals(spoken, "option " + (index + 1).ToString(CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase)).ToArray();
}
