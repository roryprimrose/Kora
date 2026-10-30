using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Dependencies;

/// <summary>Original-user data issued only after host question receipt validation; it grants no authority.</summary>
public sealed class ModelQuestionAnswerMessage : ModelToolMessage
{
    public ModelQuestionAnswerMessage(ModelClarificationQuestion question, QuestionAnswer answer, RequestOrigin channel)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(answer);
        if (channel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !LocalModelQuestionPolicy.CreateSpec(question.Question).Accepts(answer, submitting: true))
        { throw new InvalidDataException("Only a complete original-user answer for the declared clarification shape is admitted."); }
        LocalModelQuestionPolicy.RequireAnswerContent(answer);
        Question = question;
        Answer = LocalModelQuestionPolicy.CreateSpec(question.Question).CanonicalAnswer(answer);
        Channel = channel;
    }

    public ModelClarificationQuestion Question { get; }
    public QuestionAnswer Answer { get; }
    public RequestOrigin Channel { get; }
}
