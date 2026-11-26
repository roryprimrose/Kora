using Kora.Core.Dependencies;
using Microsoft.Extensions.AI;

namespace Kora.Application.Dependencies;

internal static class ModelChatMessageProjection
{
    // This changes only the package transcript: non-tool question answers remain USER data.
    internal sealed class QuestionContinuationClient(IChatClient inner) : FunctionInvokingChatClient(inner)
    {
        protected override IList<ChatMessage> CreateResponseMessages(ReadOnlySpan<FunctionInvocationResult> results)
        {
            if (results.Length == 1 && results[0].Exception is null
                && results[0].Result is ModelQuestionAnswerMessage { Question.CallId: null } answer)
            { return [new(ChatRole.User, ModelToolChatMessages.Text(answer))]; }
            return base.CreateResponseMessages(results);
        }
    }
}
