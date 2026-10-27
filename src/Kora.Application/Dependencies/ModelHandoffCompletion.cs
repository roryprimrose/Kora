using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>Content-free presentation status; the answer remains in its original conversation.</summary>
public sealed record ModelHandoffCompletion(ModelTurnOutcome Outcome, ModelTurnReason Reason, bool HasAssistantResponse);
