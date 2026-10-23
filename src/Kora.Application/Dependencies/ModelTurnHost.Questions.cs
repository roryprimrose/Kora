using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private Task<ModelProviderReply> RunAdapterAsync(ModelTurn turn, CancellationToken token)
    {
        if (turn.QuestionContinuation is not { } questions)
        {
            return turn.Registration.Adapter!.RunAsync(turn.Provenance, turn.Profile, turn.Wire.AsMemory(),
                (id, input, toolToken) => InvokeToolAsync(turn, id, input, token, toolToken),
                continuationToken => RevalidateContinuationAsync(turn, continuationToken), token);
        }
        if (turn.Context.ToolMode != ModelToolMode.None || turn.ConversationTarget is null
            || turn.Registration.Adapter is not IModelQuestionTurnAdapter adapter)
        {
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Denied, turn.Provenance.Selection,
                Reason: ModelTurnReason.InvalidProviderResponse));
        }
        return adapter.RunWithQuestionAsync(turn.Provenance, turn.Profile, turn.Wire.AsMemory(),
            (id, input, toolToken) => InvokeToolAsync(turn, id, input, token, toolToken),
            continuationToken => RevalidateContinuationAsync(turn, continuationToken), async (question, questionToken) =>
            {
                questionToken.ThrowIfCancellationRequested();
                token.ThrowIfCancellationRequested();
                if (!turn.IsRunning || await RevalidateContinuationAsync(turn, token).ConfigureAwait(false) != ModelTurnReason.None)
                { throw new InvalidOperationException("Exact original conversation admission changed before clarification."); }
                var reply = await questions.AskAsync(question.Question, token).ConfigureAwait(false);
                questionToken.ThrowIfCancellationRequested();
                token.ThrowIfCancellationRequested();
                if (!turn.IsRunning || await RevalidateContinuationAsync(turn, token).ConfigureAwait(false) != ModelTurnReason.None)
                { throw new InvalidOperationException("Exact original conversation admission changed after clarification."); }
                return new(question, reply.Question.Draft!, reply.Question.AnswerChannel!.Value);
            }, token);
    }
}
