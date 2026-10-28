using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private Task<ModelProviderReply> RunAdapterAsync(ModelTurn turn, CancellationToken token)
    {
        if (turn.QuestionContinuation is not { } questions)
        {
            if (turn.PublishAnswer is { } publish && turn.Context.ToolMode == ModelToolMode.None
                && (turn.Provenance.Selection.Provider == ModelProviderIdentity.Ollama
                    || turn.Registration.Adapter is ClaudeModelTurnAdapter { StreamsAnswer: true })
                && turn.ConversationTarget is not null && turn.Registration.Adapter is IModelAnswerTurnAdapter streaming)
            {
                var previous = string.Empty;
                return streaming.RunWithAnswerAsync(turn.Provenance, turn.Profile, turn.Wire.AsMemory(),
                    (id, input, toolToken) => InvokeToolAsync(turn, id, input, token, toolToken),
                    continuationToken => RevalidateAdapterContinuationAsync(turn, continuationToken), async (update, updateToken) =>
                    {
                        update.Validate();
                        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, updateToken);
                        linked.Token.ThrowIfCancellationRequested();
                        if (update.Provenance != turn.Provenance || !turn.IsRunning
                            || !update.Answer.StartsWith(previous, StringComparison.Ordinal)
                            || System.Text.Encoding.UTF8.GetByteCount(update.Answer)
                                > (turn.Context.Constraints?.OutputUtf8Bytes ?? ModelToolProtocol.MaximumOutputUtf8Bytes)
                            || await RevalidateAdapterContinuationAsync(turn, linked.Token).ConfigureAwait(false) != ModelTurnReason.None)
                        { throw new InvalidDataException("Exact provisional answer admission changed."); }
                        linked.Token.ThrowIfCancellationRequested();
                        if (!turn.IsRunning || !ConsumerAdmissionCurrent(turn))
                        { throw new InvalidDataException("Provisional publication no longer owns the turn."); }
                        await publish(update, linked.Token).ConfigureAwait(false);
                        previous = update.Answer;
                    }, token);
            }
            return turn.Registration.Adapter!.RunAsync(turn.Provenance, turn.Profile, turn.Wire.AsMemory(),
                (id, input, toolToken) => InvokeToolAsync(turn, id, input, token, toolToken),
                continuationToken => RevalidateAdapterContinuationAsync(turn, continuationToken), token);
        }
        if (!ModelToolProtocol.AllowsClarification(turn.Context.ToolMode, turn.Provenance.Selection.Provider)
            || turn.ConversationTarget is null
            || turn.Registration.Adapter is not IModelQuestionTurnAdapter adapter)
        {
            return Task.FromResult(new ModelProviderReply(ModelTurnOutcome.Denied, turn.Provenance.Selection,
                Reason: ModelTurnReason.InvalidProviderResponse));
        }
        return adapter.RunWithQuestionAsync(turn.Provenance, turn.Profile, turn.Wire.AsMemory(),
            (id, input, toolToken) => InvokeToolAsync(turn, id, input, token, toolToken),
            continuationToken => RevalidateAdapterContinuationAsync(turn, continuationToken), async (question, questionToken) =>
            {
                questionToken.ThrowIfCancellationRequested();
                token.ThrowIfCancellationRequested();
                if (!turn.IsRunning || await RevalidateAdapterContinuationAsync(turn, token).ConfigureAwait(false) != ModelTurnReason.None)
                { throw new InvalidOperationException("Exact original conversation admission changed before clarification."); }
                var reply = await questions.AskAsync(question.Question, token).ConfigureAwait(false);
                questionToken.ThrowIfCancellationRequested();
                token.ThrowIfCancellationRequested();
                if (!turn.IsRunning || await RevalidateAdapterContinuationAsync(turn, token).ConfigureAwait(false) != ModelTurnReason.None)
                { throw new InvalidOperationException("Exact original conversation admission changed after clarification."); }
                return new(question, reply.Question.Draft!, reply.Question.AnswerChannel!.Value);
            }, token);
    }

    private async ValueTask<ModelTurnReason> RevalidateAdapterContinuationAsync(ModelTurn turn, CancellationToken token)
    {
        var reason = ModelTurnReason.HostAdmissionClosed;
        try
        {
            reason = await RevalidateContinuationAsync(turn, token).ConfigureAwait(false);
            return reason;
        }
        finally
        {
            // Validation failure retires authority, not ownership of the still-running provider.
            if (reason != ModelTurnReason.None) { turn.Retire(reason); }
        }
    }
}
