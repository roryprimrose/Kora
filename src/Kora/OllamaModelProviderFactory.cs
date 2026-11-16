using Kora.Application.Dependencies;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Windows.Dependencies;

using Microsoft.Extensions.Logging;

namespace Kora;

internal sealed class OllamaModelProviderFactory(
    WindowsOllamaRuntimeResolver resolver, TimeProvider time, ILoggerFactory logging,
    Func<WindowsOllamaToolEndpointBinding, IModelToolTransport>? createTransport = null) : IModelProviderRegistrationSource
{
    public async Task<ModelProviderRegistration?> ResolveAsync(ModelProviderSelection selection, ProviderProfileTuple profile,
        CancellationToken cancellationToken)
    {
        var receipt = await resolver.ResolveAsync(selection, profile, cancellationToken).ConfigureAwait(false);
        if (receipt is null || !receipt.IsCurrent(time.GetUtcNow())) { return null; }
        var preview = DevelopmentPreviewBuild.Enabled && receipt.IsDevelopmentPreview;
        var evidence = receipt.QualificationEvidence;
        if (!preview && evidence?.IsCurrent(time.GetUtcNow()) != true) { return null; }
        return new(selection,
            preview ? ModelQualificationGate.UserReviewedLocalEvaluation | ModelQualificationGate.IntegratedHost
                : ModelQualificationGate.LocalCandidateSelection | ModelQualificationGate.IntegratedHost,
            receipt.Catalogue.ValidUntil, new OllamaModelTurnAdapter(resolver, receipt, time, logging, createTransport),
            () => receipt.IsCurrent(time.GetUtcNow()));
    }
}