using System.Collections.Immutable;

namespace Kora.Core.Dependencies;

/// <summary>Structured provider question, never prose interpreted as host intent.</summary>
public sealed class ModelClarificationQuestion : ModelAssistantMessage
{
    public ModelClarificationQuestion(LocalModelQuestion question, string? callId = null)
    {
        if (callId is not null && !ModelToolProtocol.ValidCallId(callId))
        { throw new InvalidDataException("Question tool correlation must be bounded ASCII, not host identity."); }
        var spec = LocalModelQuestionPolicy.CreateSpec(question);
        Question = new(spec.Text, spec.Options.Select(option => option.Label).ToImmutableArray(), spec.Minimum, spec.Maximum,
            spec.Kind == Interaction.QuestionKind.Form ? spec.Fields : null);
        CallId = callId;
    }

    public LocalModelQuestion Question { get; }
    public string? CallId { get; }
}
