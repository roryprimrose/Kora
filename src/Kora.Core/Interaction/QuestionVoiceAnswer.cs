using System.Globalization;

namespace Kora.Core.Interaction;

/// <summary>Maps an entire utterance to one exact schema. No command interpretation or approval authority.</summary>
public static class QuestionVoiceAnswer
{
    public static QuestionAnswer? Parse(HostQuestionRecord question, string transcript)
    {
        return question.Proposal is null ? Parse(question.Spec, transcript) : null;
    }

    public static QuestionAnswer? Parse(QuestionSpec spec, string transcript)
    {
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
        var matches = spec.Options.Where((option, index) =>
            string.Equals(spoken, option.Label, StringComparison.OrdinalIgnoreCase)
            || string.Equals(spoken, (index + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            || string.Equals(spoken, "option " + (index + 1).ToString(CultureInfo.InvariantCulture),
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1) { return null; }
        var answer = new QuestionAnswer([matches[0].Id]);
        return spec.Accepts(answer, submitting: true) ? answer : null;
    }
}
