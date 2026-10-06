using Kora.Core.Authorization;

namespace Kora.Core.Interaction;

// A foundation receipt is not a dispatch token or evidence that an effect occurred.
public sealed record HostInteractionDecision(
    HostInteractionOutcome Outcome,
    string Reason,
    HostQuestionRecord? Question = null,
    OperationGrant? Grant = null);
