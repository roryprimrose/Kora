using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;
using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class ClaudeModelProviderFactory(ClaudeAccountBroker accounts, ClaudeMessagesRuntime runtime,
    HostedTransmissionReviewService reviews, TimeProvider time,
    ILogger<BoundedModelTurnAdapter> logger) : IModelProviderRegistrationSource
{
    public async Task<ModelProviderRegistration?> ResolveAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
        CancellationToken cancellationToken)
    {
        if (selection.Provider != ModelProviderIdentity.Claude || profile.Service != ProviderService.Claude) { return null; }
        var catalogue = await accounts.ObserveAsync(cancellationToken).ConfigureAwait(false);
        if (ProviderProfileRules.Resolve(profile, catalogue, time.GetUtcNow())?.Selection != selection) { return null; }
        var current = await accounts.IsProfileCurrentAsync(profile, cancellationToken).ConfigureAwait(false);
        return !current ? null : new(selection,
            ModelQualificationGate.DotNetFinalRequest | ModelQualificationGate.AllPathLifecycle
                | ModelQualificationGate.ExecutionAccount | ModelQualificationGate.IntegratedHost,
            catalogue.ValidUntil, new ClaudeModelTurnAdapter(runtime, reviews, time, logger),
            () => catalogue.IsCurrent(time.GetUtcNow()));
    }
}
