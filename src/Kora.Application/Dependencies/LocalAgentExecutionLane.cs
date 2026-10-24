using Kora.Application.Agents;

namespace Kora.Application.Dependencies;

/// <summary>Only the host can issue a lane, after canonical exact-request admission.</summary>
internal sealed class LocalAgentExecutionLane(ModelTurnHost host, BoundedAgentReview review,
    ModelTurn turn, ILocalAgentConcurrencyQualification qualification)
{
    internal ModelTurnHost Host { get; } = host;
    internal BoundedAgentReview Review { get; } = review;
    internal ModelTurn Turn { get; } = turn;
    internal ILocalAgentConcurrencyQualification Qualification { get; } = qualification;
    internal LocalAgentCapacity? Capacity { get; set; }
    internal Task Completion { get; set; } = Task.CompletedTask;
}
