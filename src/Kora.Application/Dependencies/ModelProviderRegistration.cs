using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>Host-owned admission, including explicitly reviewed evaluation, bound to one exact catalogue revision.</summary>
internal sealed record ModelProviderRegistration(
    ModelProviderSelection Selection, ModelQualificationGate Evidence,
    DateTimeOffset ExpiresAt, IModelTurnAdapter? Adapter, Func<bool>? RemainsQualified = null)
{
    internal bool IsQualified(DateTimeOffset now)
    {
        var required = Selection.Provider switch
        {
            ModelProviderIdentity.Ollama => ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
            ModelProviderIdentity.Copilot => ModelQualificationGate.DotNetFinalRequest | ModelQualificationGate.AllPathLifecycle
                | ModelQualificationGate.ExecutionAccount | ModelQualificationGate.IntegratedHost,
            _ => ModelQualificationGate.None,
        };
        var evaluation = (Evidence & ModelQualificationGate.UserReviewedCopilotEvaluation) != ModelQualificationGate.None;
        if (evaluation)
        {
            if (Selection.Provider != ModelProviderIdentity.Copilot
                || (Evidence & ModelQualificationGate.AllPathLifecycle) != ModelQualificationGate.None) { return false; }
            required &= ~ModelQualificationGate.AllPathLifecycle;
        }
        if ((Evidence & ModelQualificationGate.UserReviewedOllamaEvaluation) != ModelQualificationGate.None)
        {
            if (Selection.Provider != ModelProviderIdentity.Ollama
                || (Evidence & ~(ModelQualificationGate.UserReviewedOllamaEvaluation | ModelQualificationGate.IntegratedHost))
                    != ModelQualificationGate.None) { return false; }
            required &= ~ModelQualificationGate.LocalCandidateSelection;
        }
        const ModelQualificationGate known = ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost
            | ModelQualificationGate.DotNetFinalRequest | ModelQualificationGate.AllPathLifecycle | ModelQualificationGate.ExecutionAccount
            | ModelQualificationGate.UserReviewedCopilotEvaluation | ModelQualificationGate.UserReviewedOllamaEvaluation;
        return Selection.IsValid && (Evidence & ~known) == ModelQualificationGate.None && (Evidence & required) == required
            && now < ExpiresAt && Adapter is not null && (RemainsQualified is null || RemainsQualified());
    }
}
