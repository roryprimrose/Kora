using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Kora.Application.Dependencies;

internal sealed partial class BoundedModelTurnAdapter
{
    private async Task<ModelProviderReply> RunAgentFrameworkAsync(ModelTurnProvenance provenance,
        ProviderProfileTuple? profile, ModelToolContext context,
        Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation, TokenTotals tokens,
        CancellationToken token)
    {
        var started = time.GetUtcNow();
        using var bridge = new AgentFrameworkModelChatClient(transport, provenance, profile, context, invokeTool,
            check => CheckContinuationAsync(provenance, started, validateContinuation,
                TimeSpan.FromMilliseconds(context.Constraints!.DeadlineMilliseconds), check),
            tokens.BeginRound, tokens.Observe,
            (round, calls, input, output) => Progress(logger, round, calls, input, output));
        using var functions = new FunctionInvokingChatClient(bridge)
        {
            AllowConcurrentInvocation = false,
            MaximumConsecutiveErrorsPerRequest = 0,
            MaximumIterationsPerRequest = context.Constraints!.MaximumRounds + 1,
        };
        var agent = new ChatClientAgent(functions, new ChatClientAgentOptions
        {
            UseProvidedChatClientAsIs = true,
            ChatOptions = new() { Tools = bridge.Tools },
        });
        try
        {
            var session = await agent.CreateSessionAsync(cancellationToken: token).ConfigureAwait(false);
            var response = await agent.RunAsync(new ChatMessage(ChatRole.User, context.UserRequest),
                session, cancellationToken: token).ConfigureAwait(false);
            var denied = await CheckContinuationAsync(provenance, started, validateContinuation,
                TimeSpan.FromMilliseconds(context.Constraints.DeadlineMilliseconds), token).ConfigureAwait(false);
            if (denied is not null) { return denied; }
            return Finish(provenance, ModelTurnOutcome.Succeeded, ModelTurnReason.None,
                new(bridge.ReadFinalAnswer(response.Messages), null));
        }
        catch (AgentFrameworkModelChatClient.TurnStoppedException stopped)
        {
            return Finish(provenance, stopped.Outcome, stopped.Reason);
        }
    }
}
