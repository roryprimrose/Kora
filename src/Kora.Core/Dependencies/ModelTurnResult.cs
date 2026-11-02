namespace Kora.Core.Dependencies;

public sealed record ModelTurnResult(
    ModelTurnOutcome Outcome, ModelTurnReason Reason,
    ModelTurnProvenance? Provenance = null, LocalModelResponse? Response = null)
{
    public Conversation.RetainedLocalObservations? Observations { get; init; }
    public Conversation.SubmittedAnswerContext? SubmittedContext { get; init; }
}
