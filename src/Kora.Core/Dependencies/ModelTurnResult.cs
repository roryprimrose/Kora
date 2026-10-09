namespace Kora.Core.Dependencies;

public sealed record ModelTurnResult(
    ModelTurnOutcome Outcome, ModelTurnReason Reason,
    ModelTurnProvenance? Provenance = null, LocalModelResponse? Response = null);
