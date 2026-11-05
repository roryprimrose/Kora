namespace Kora.Core.Dependencies;

public sealed record CopilotTurnReply(ModelTurnOutcome Outcome, string? Answer = null)
{
    public ModelTokenUsage? TokenUsage { get; init; }
}
