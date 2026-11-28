using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System.Runtime.CompilerServices;
using System.Text;

namespace Kora.Application.Dependencies;

internal sealed partial class BoundedModelTurnAdapter
{
    private async Task<ModelProviderReply> RunAgentFrameworkAsync(ModelTurnProvenance provenance,
        ProviderProfileTuple? profile, ModelToolContext context,
        Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>>? ask,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask>? publish, TokenTotals tokens,
        CancellationToken token)
    {
        var started = time.GetUtcNow();
        // The owning turn's host waits pause this bound; check-in turns have no fixed adapter deadline.
        var turnLifetime = ModelTurnLifetime.For(provenance.Request);
        var deadline = context.ToolMode == ModelToolMode.HostedClarification ? HostedTurnAuthorization.MaximumDuration
            : context.Constraints is { } constraints ? TimeSpan.FromMilliseconds(constraints.DeadlineMilliseconds)
            : ModelToolProtocol.Deadline;
        using var bridge = new AgentFrameworkModelChatClient(transport, provenance, profile, context, invokeTool,
            check => CheckContinuationAsync(provenance, started, validateContinuation, deadline, turnLifetime, check),
            tokens.BeginRound, tokens.Observe,
            (round, calls, input, output) => Progress(logger, round, calls, input, output),
            ask, exception => Fault(logger, exception.GetType().Name));
        using var functions = new ModelChatMessageProjection.QuestionContinuationClient(bridge)
        {
            AllowConcurrentInvocation = false,
            MaximumConsecutiveErrorsPerRequest = 0,
            MaximumIterationsPerRequest = bridge.MaximumRounds + 1,
            AdditionalTools = bridge.QuestionTools,
        };
        var agent = new ChatClientAgent(functions, new ChatClientAgentOptions
        {
            UseProvidedChatClientAsIs = true,
            ChatOptions = new() { Tools = bridge.Tools },
        });
        try
        {
            var session = await agent.CreateSessionAsync(cancellationToken: token).ConfigureAwait(false);
            var input = new ChatMessage(ChatRole.User, context.UserRequest);
            var response = publish is not null && ask is null && context.ToolMode == ModelToolMode.None
                && context.Constraints?.Version != 1
                && provenance.Selection.Provider == ModelProviderIdentity.Ollama && transport is IModelAnswerTransport
                ? await PublishAsync(agent.RunStreamingAsync(input, session, cancellationToken: token),
                    provenance, publish, bridge, token).ToAgentResponseAsync(token).ConfigureAwait(false)
                : await agent.RunAsync(input, session, cancellationToken: token).ConfigureAwait(false);
            var denied = await CheckContinuationAsync(provenance, started, validateContinuation, deadline, turnLifetime, token).ConfigureAwait(false);
            if (denied is not null) { return denied; }
            return Finish(provenance, ModelTurnOutcome.Succeeded, ModelTurnReason.None,
                new(bridge.ReadFinalAnswer(response.Messages), null));
        }
        catch (AgentFrameworkModelChatClient.TurnStoppedException stopped)
        {
            return Finish(provenance, stopped.Outcome, stopped.Reason);
        }
    }

    internal static async IAsyncEnumerable<AgentResponseUpdate> PublishAsync(
        IAsyncEnumerable<AgentResponseUpdate> updates, ModelTurnProvenance provenance,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, AgentFrameworkModelChatClient bridge,
        [EnumeratorCancellation] CancellationToken token)
    {
        var text = new StringBuilder();
        await foreach (var update in updates.WithCancellation(token).ConfigureAwait(false))
        {
            if (update.Contents.Any(content => content is not TextContent)
                || update.Role is { } role && role != ChatRole.Assistant)
            { throw new InvalidDataException("The admitted answer stream contains non-answer content."); }
            if (update.Text.Length != 0)
            {
                text.Append(update.Text);
                var snapshot = new ModelAnswerUpdate(provenance, text.ToString());
                snapshot.Validate();
                await bridge.ValidatePublicationAsync(token).ConfigureAwait(false);
                await publish(snapshot, token).ConfigureAwait(false);
            }
            yield return update;
        }
    }
}
