using Kora.Core.Dependencies;

namespace Kora.Application.Dependencies;

/// <summary>Host-owned qualification, bound to one exact catalogue revision; never deserialized from model output.</summary>
internal sealed record ModelProviderRegistration(
    ModelProviderSelection Selection, ModelQualificationGate Evidence,
    DateTimeOffset ExpiresAt, IModelTurnAdapter? Adapter)
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
        const ModelQualificationGate known = ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost
            | ModelQualificationGate.DotNetFinalRequest | ModelQualificationGate.AllPathLifecycle | ModelQualificationGate.ExecutionAccount;
        return Selection.IsValid && (Evidence & ~known) == ModelQualificationGate.None && (Evidence & required) == required
            && now < ExpiresAt && Adapter is not null;
    }
}
