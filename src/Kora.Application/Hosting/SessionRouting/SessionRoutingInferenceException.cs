using Kora.Core.Dependencies;

namespace Kora.Application.Hosting.SessionRouting;

internal sealed class SessionRoutingInferenceException(ModelTurnOutcome outcome, ModelTurnReason reason)
    : InvalidOperationException("Local routing inference failed: " + outcome + " / " + reason + ". Nothing was routed.")
{
    internal ModelTurnOutcome Outcome { get; } = outcome;
}
