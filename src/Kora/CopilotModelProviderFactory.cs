using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;

namespace Kora;

internal sealed class CopilotModelProviderFactory(OllamaModelProviderFactory local, CopilotAccountBroker accounts,
    CopilotSdkTurnRuntime runtime, HostedTransmissionReviewService reviews, TimeProvider time) : IModelProviderRegistrationSource
{
    public async Task<ModelProviderRegistration?> ResolveAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
        CancellationToken cancellationToken)
    {
        if (selection.Provider == ModelProviderIdentity.Ollama)
        { return await local.ResolveAsync(selection, profile, cancellationToken).ConfigureAwait(false); }
        if (selection.Provider != ModelProviderIdentity.Copilot) { return null; }
        var catalogue = await accounts.ObserveAsync(cancellationToken).ConfigureAwait(false);
        var model = ProviderProfileRules.Resolve(profile, catalogue, time.GetUtcNow());
        if (model?.Selection != selection) { return null; }
        var account = await accounts.ResolveAsync(profile, cancellationToken).ConfigureAwait(false);
        return account is null ? null : new(selection,
            ModelQualificationGate.DotNetFinalRequest | ModelQualificationGate.AllPathLifecycle
                | ModelQualificationGate.ExecutionAccount | ModelQualificationGate.IntegratedHost,
            account.ExpiresAt, new CopilotModelTurnAdapter(runtime, reviews), () => account.ExpiresAt > time.GetUtcNow());
    }
}
