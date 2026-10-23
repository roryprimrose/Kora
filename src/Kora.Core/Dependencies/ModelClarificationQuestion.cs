using System.Collections.Immutable;

namespace Kora.Core.Dependencies;

/// <summary>Structured provider question, never prose interpreted as host intent.</summary>
public sealed class ModelClarificationQuestion : ModelAssistantMessage
{
    public ModelClarificationQuestion(LocalModelQuestion question)
    {
        var spec = LocalModelQuestionPolicy.CreateSpec(question);
        Question = new(spec.Text, spec.Options.Select(option => option.Label).ToImmutableArray());
    }

    public LocalModelQuestion Question { get; }
}
